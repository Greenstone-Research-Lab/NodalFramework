using System.Collections.Immutable;
using Nodal.Core.Migrations;

namespace Nodal.Migrations;

/// <summary>Describes the verified provider runtime targeted by an immutable migration bundle.</summary>
public sealed record NodalMigrationBundleTarget(
    string ProviderName,
    string ProviderVersion,
    IReadOnlySet<string> Capabilities)
{
    internal NodalMigrationBundleTarget Normalize()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ProviderName);
        ArgumentException.ThrowIfNullOrWhiteSpace(ProviderVersion);
        ArgumentNullException.ThrowIfNull(Capabilities);
        if (Capabilities.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Target capabilities cannot contain an empty value.", nameof(Capabilities));
        }

        return this with { Capabilities = Capabilities.ToImmutableHashSet(StringComparer.Ordinal) };
    }
}

/// <summary>Controls safety-sensitive immutable bundle execution.</summary>
public sealed record NodalMigrationBundleExecutionOptions
{
    /// <summary>Gets whether destructive commands have been explicitly approved.</summary>
    public bool AllowDestructiveOperations { get; init; }

    /// <summary>Gets whether validation should run without changing provider state.</summary>
    public bool DryRun { get; init; }
}

/// <summary>Identifies the idempotent result of one bundle execution request.</summary>
public enum NodalMigrationBundleExecutionOutcome
{
    /// <summary>The migration was applied.</summary>
    Applied,

    /// <summary>The same migration and checksum were already applied.</summary>
    AlreadyApplied,

    /// <summary>The migration was reverted.</summary>
    Reverted,

    /// <summary>The migration was already absent.</summary>
    AlreadyReverted,

    /// <summary>An apply request was validated without provider mutation.</summary>
    ApplyPlanned,

    /// <summary>A revert request was validated without provider mutation.</summary>
    RevertPlanned,
}

/// <summary>Reports the deterministic outcome of immutable bundle execution.</summary>
public sealed record NodalMigrationBundleExecutionResult(
    string MigrationId,
    string Checksum,
    NodalMigrationBundleExecutionOutcome Outcome,
    int CommandCount);

/// <summary>
/// Supplies a provider-composed execution boundary to provider-neutral deployment tooling.
/// </summary>
/// <remarks>
/// Implementations own provider construction and secret loading. Bundle content and CLI
/// arguments must never be used as a credential source.
/// </remarks>
public interface INodalMigrationBundleExecutionHost
{
    /// <summary>Applies an immutable bundle to the configured provider target.</summary>
    ValueTask<NodalMigrationBundleExecutionResult> ApplyAsync(
        NodalMigrationBundle bundle,
        NodalMigrationBundleExecutionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reverts an immutable bundle from the configured provider target.</summary>
    ValueTask<NodalMigrationBundleExecutionResult> RevertAsync(
        NodalMigrationBundle bundle,
        NodalMigrationBundleExecutionOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Applies and reverts immutable migration bundles through provider-neutral migration contracts.
/// </summary>
public sealed class NodalMigrationBundleExecutor : INodalMigrationBundleExecutionHost
{
    private readonly IGraphMigrationProvider provider;
    private readonly NodalMigrationBundleTarget target;

    /// <summary>Creates an executor for one verified provider target.</summary>
    public NodalMigrationBundleExecutor(
        IGraphMigrationProvider provider,
        NodalMigrationBundleTarget target)
    {
        this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
        this.target = (target ?? throw new ArgumentNullException(nameof(target))).Normalize();
    }

    /// <summary>Applies a verified bundle idempotently.</summary>
    public ValueTask<NodalMigrationBundleExecutionResult> ApplyAsync(
        NodalMigrationBundle bundle,
        NodalMigrationBundleExecutionOptions? options = null,
        CancellationToken cancellationToken = default) =>
        ExecuteWithOptionalLockAsync(bundle, options ?? new(), revert: false, cancellationToken);

    /// <summary>Reverts a verified bundle idempotently.</summary>
    public ValueTask<NodalMigrationBundleExecutionResult> RevertAsync(
        NodalMigrationBundle bundle,
        NodalMigrationBundleExecutionOptions? options = null,
        CancellationToken cancellationToken = default) =>
        ExecuteWithOptionalLockAsync(bundle, options ?? new(), revert: true, cancellationToken);

    private async ValueTask<NodalMigrationBundleExecutionResult> ExecuteWithOptionalLockAsync(
        NodalMigrationBundle bundle,
        NodalMigrationBundleExecutionOptions options,
        bool revert,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var verified = VerifyTarget(bundle);
        if (provider is not IGraphMigrationLockProvider lockProvider)
        {
            return await ExecuteCoreAsync(verified, options, revert, cancellationToken).ConfigureAwait(false);
        }

        await using var lease = await lockProvider.MigrationLock
            .AcquireAsync(lockProvider.MigrationLockScope, cancellationToken)
            .ConfigureAwait(false);
        return await ExecuteCoreAsync(verified, options, revert, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<NodalMigrationBundleExecutionResult> ExecuteCoreAsync(
        NodalMigrationBundle bundle,
        NodalMigrationBundleExecutionOptions options,
        bool revert,
        CancellationToken cancellationToken)
    {
        var applied = await provider.MigrationExecutor
            .GetAppliedMigrationsAsync(cancellationToken)
            .ConfigureAwait(false);
        var exists = applied.TryGetValue(bundle.MigrationId, out var appliedChecksum);

        if (exists && !string.Equals(appliedChecksum, bundle.Checksum, StringComparison.Ordinal))
        {
            throw new NodalMigrationBundleAppliedChecksumException(bundle.MigrationId);
        }

        if (revert)
        {
            return await RevertCoreAsync(bundle, options, exists, cancellationToken).ConfigureAwait(false);
        }

        if (exists)
        {
            return Result(bundle, NodalMigrationBundleExecutionOutcome.AlreadyApplied, 0);
        }

        var commands = BuildCommands(bundle, NodalMigrationBundleDirection.Up);
        RequireApproval(bundle, commands, options);
        if (options.DryRun)
        {
            return Result(bundle, NodalMigrationBundleExecutionOutcome.ApplyPlanned, commands.Length);
        }

        await provider.MigrationExecutor
            .ApplyAsync(new MigrationExecution(bundle.MigrationId, bundle.Checksum, commands), cancellationToken)
            .ConfigureAwait(false);
        return Result(bundle, NodalMigrationBundleExecutionOutcome.Applied, commands.Length);
    }

    private async ValueTask<NodalMigrationBundleExecutionResult> RevertCoreAsync(
        NodalMigrationBundle bundle,
        NodalMigrationBundleExecutionOptions options,
        bool exists,
        CancellationToken cancellationToken)
    {
        if (!exists)
        {
            return Result(bundle, NodalMigrationBundleExecutionOutcome.AlreadyReverted, 0);
        }

        var commands = BuildCommands(bundle, NodalMigrationBundleDirection.Down);
        if (commands.Length == 0)
        {
            throw new NodalMigrationBundleIrreversibleException(bundle.MigrationId);
        }

        if (!options.AllowDestructiveOperations)
        {
            throw new NodalMigrationBundleApprovalRequiredException(bundle.MigrationId);
        }

        if (options.DryRun)
        {
            return Result(bundle, NodalMigrationBundleExecutionOutcome.RevertPlanned, commands.Length);
        }

        await provider.MigrationExecutor
            .RevertAsync(new MigrationExecution(bundle.MigrationId, bundle.Checksum, commands), cancellationToken)
            .ConfigureAwait(false);
        return Result(bundle, NodalMigrationBundleExecutionOutcome.Reverted, commands.Length);
    }

    private NodalMigrationBundle VerifyTarget(NodalMigrationBundle bundle)
    {
        var verified = NodalMigrationBundleSerializer.Verify(bundle);
        if (!provider.SupportsMigrationExecution)
        {
            throw new NodalCapabilityNotSupportedException(
                target.ProviderName,
                "NODAL-MIGRATION-EXECUTION",
                "Administrative migration execution is not configured.");
        }

        if (!string.Equals(verified.ProviderName, target.ProviderName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Bundle provider '{verified.ProviderName}' does not match target provider '{target.ProviderName}'.");
        }

        if (!string.Equals(verified.ProviderVersion, target.ProviderVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Bundle provider version '{verified.ProviderVersion}' does not match target version '{target.ProviderVersion}'.");
        }

        var missing = verified.Requirements.Where(requirement => !target.Capabilities.Contains(requirement)).ToArray();
        if (missing.Length > 0)
        {
            throw new NodalCapabilityNotSupportedException(
                target.ProviderName,
                missing[0],
                "The immutable migration bundle requires a capability that is not available on the target.");
        }

        return verified;
    }

    private static MigrationCommand[] BuildCommands(
        NodalMigrationBundle bundle,
        NodalMigrationBundleDirection direction) =>
        bundle.Commands
            .Where(command => command.Direction == direction)
            .Select(command => new MigrationCommand(command.Text, command.Transactional, command.Kind))
            .ToArray();

    private static void RequireApproval(
        NodalMigrationBundle bundle,
        MigrationCommand[] commands,
        NodalMigrationBundleExecutionOptions options)
    {
        if (!options.AllowDestructiveOperations && bundle.Commands
            .Where(command => command.Direction == NodalMigrationBundleDirection.Up)
            .Any(command => command.Destructive))
        {
            throw new NodalMigrationBundleApprovalRequiredException(bundle.MigrationId);
        }

        if (commands.Length == 0)
        {
            throw new InvalidOperationException($"Migration bundle '{bundle.MigrationId}' has no upward commands.");
        }
    }

    private static NodalMigrationBundleExecutionResult Result(
        NodalMigrationBundle bundle,
        NodalMigrationBundleExecutionOutcome outcome,
        int commandCount) => new(bundle.MigrationId, bundle.Checksum, outcome, commandCount);
}
