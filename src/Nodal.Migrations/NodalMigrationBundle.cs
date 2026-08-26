using System.Collections.Immutable;
using Nodal.Core.Migrations;

namespace Nodal.Migrations;

/// <summary>
/// Describes the provider and command material used to create an immutable migration bundle.
/// </summary>
/// <param name="MigrationId">Stable migration identifier.</param>
/// <param name="ProviderName">Target graph provider name.</param>
/// <param name="ProviderVersion">Verified target provider version.</param>
/// <param name="FrameworkVersion">Nodal package version that produced the commands.</param>
/// <param name="Requirements">Provider capabilities required before execution.</param>
/// <param name="Commands">Ordered provider commands.</param>
public sealed record NodalMigrationBundleManifest(
    string MigrationId,
    string ProviderName,
    string ProviderVersion,
    string FrameworkVersion,
    IReadOnlyList<string> Requirements,
    IReadOnlyList<NodalMigrationBundleCommand> Commands)
{
    /// <summary>Returns a validated manifest with deterministic capability ordering.</summary>
    public NodalMigrationBundleManifest Normalize()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(MigrationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ProviderName);
        ArgumentException.ThrowIfNullOrWhiteSpace(ProviderVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(FrameworkVersion);
        ArgumentNullException.ThrowIfNull(Requirements);
        ArgumentNullException.ThrowIfNull(Commands);

        if (Requirements.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Bundle requirements cannot contain an empty value.", nameof(Requirements));
        }

        foreach (var command in Commands)
        {
            ArgumentNullException.ThrowIfNull(command);
            command.Validate();
        }

        if (!Commands.Any(command => command.Direction == NodalMigrationBundleDirection.Up))
        {
            throw new ArgumentException("A migration bundle must contain at least one upward command.", nameof(Commands));
        }

        return this with
        {
            Requirements = Requirements
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToImmutableArray(),
            Commands = Commands.ToImmutableArray(),
        };
    }
}

/// <summary>
/// Represents one ordered provider command without connection credentials or runtime secrets.
/// </summary>
/// <param name="Name">Stable command name used in diagnostics.</param>
/// <param name="Text">Provider command text.</param>
/// <param name="Transactional">Whether the provider executes the command transactionally.</param>
/// <param name="Destructive">Whether execution can remove or rewrite persisted data.</param>
/// <param name="Kind">The execution channel required by the provider command.</param>
/// <param name="Direction">Whether the command applies or reverts the migration.</param>
public sealed record NodalMigrationBundleCommand(
    string Name,
    string Text,
    bool Transactional,
    bool Destructive,
    MigrationCommandKind Kind = MigrationCommandKind.Schema,
    NodalMigrationBundleDirection Direction = NodalMigrationBundleDirection.Up)
{
    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(Text);
        NodalMigrationBundleSecretGuard.ThrowIfSensitive(Text);
    }
}

/// <summary>Identifies whether a bundled provider command applies or reverts a migration.</summary>
public enum NodalMigrationBundleDirection
{
    /// <summary>Applies the migration.</summary>
    Up,

    /// <summary>Reverts the migration.</summary>
    Down,
}

/// <summary>
/// Contains a versioned migration artifact whose checksum covers provider identity,
/// requirements, and ordered commands.
/// </summary>
/// <param name="FormatVersion">Bundle wire-format version.</param>
/// <param name="MigrationId">Stable migration identifier.</param>
/// <param name="ProviderName">Target graph provider name.</param>
/// <param name="ProviderVersion">Verified target provider version.</param>
/// <param name="FrameworkVersion">Nodal package version that produced the bundle.</param>
/// <param name="Requirements">Deterministically ordered provider capabilities.</param>
/// <param name="Commands">Ordered provider commands.</param>
/// <param name="Checksum">Lowercase SHA-256 checksum of the canonical manifest.</param>
public sealed record NodalMigrationBundle(
    int FormatVersion,
    string MigrationId,
    string ProviderName,
    string ProviderVersion,
    string FrameworkVersion,
    IReadOnlyList<string> Requirements,
    IReadOnlyList<NodalMigrationBundleCommand> Commands,
    string Checksum)
{
    /// <summary>Gets the bundle format understood by this package.</summary>
    public const int CurrentFormatVersion = 1;

    internal NodalMigrationBundleManifest ToManifest() => new(
        MigrationId,
        ProviderName,
        ProviderVersion,
        FrameworkVersion,
        Requirements,
        Commands);
}

/// <summary>Base exception for invalid or unsafe immutable migration bundle content.</summary>
public abstract class NodalMigrationBundleException : Exception
{
    /// <summary>Initializes a bundle exception with a safe diagnostic message.</summary>
    protected NodalMigrationBundleException(string message)
        : base(message)
    {
    }
}

/// <summary>Indicates that immutable migration bundle content no longer matches its checksum.</summary>
public sealed class NodalMigrationBundleChecksumException(string migrationId)
    : NodalMigrationBundleException($"Migration bundle '{migrationId}' failed checksum validation.");

/// <summary>Indicates that a migration bundle contains material resembling a credential.</summary>
public sealed class NodalMigrationBundleSecretException()
    : NodalMigrationBundleException("Migration bundle command text contains credential-like material.");

/// <summary>Indicates that an applied migration has a different immutable checksum.</summary>
public sealed class NodalMigrationBundleAppliedChecksumException(string migrationId)
    : NodalMigrationBundleException(
        $"Applied migration '{migrationId}' has a different checksum. Immutable migration drift is not allowed.");

/// <summary>Indicates that destructive bundle execution requires explicit approval.</summary>
public sealed class NodalMigrationBundleApprovalRequiredException(string migrationId)
    : NodalMigrationBundleException(
        $"Migration bundle '{migrationId}' contains destructive work and requires explicit approval.");

/// <summary>Indicates that a bundle cannot be reverted because it has no downward commands.</summary>
public sealed class NodalMigrationBundleIrreversibleException(string migrationId)
    : NodalMigrationBundleException(
        $"Migration bundle '{migrationId}' does not contain downward commands and cannot be reverted.");
