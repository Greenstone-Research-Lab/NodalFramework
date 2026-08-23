using System.Security.Cryptography;
using System.Text;
using Nodal.Core.Migrations;

namespace Nodal.Migrations;

/// <summary>
/// Represents a dry-run plan and its provider preflight findings.
/// </summary>
public sealed record MigrationPlan
{
    /// <summary>
    /// Initializes a migration plan with pending executions and preflight findings.
    /// </summary>
    /// <param name="executions">The pending migration executions.</param>
    /// <param name="preflight">The preflight findings keyed by migration identifier.</param>
    public MigrationPlan(
        IReadOnlyList<MigrationExecution> executions,
        IReadOnlyDictionary<string, MigrationPreflightResult> preflight)
    {
        ArgumentNullException.ThrowIfNull(executions);
        ArgumentNullException.ThrowIfNull(preflight);

        Executions = executions;
        Preflight = preflight;
    }

    /// <summary>
    /// Gets the pending migration executions.
    /// </summary>
    public IReadOnlyList<MigrationExecution> Executions { get; }

    /// <summary>
    /// Gets preflight findings keyed by migration identifier.
    /// </summary>
    public IReadOnlyDictionary<string, MigrationPreflightResult> Preflight { get; }

    /// <summary>
    /// Gets whether the target graph is already up to date.
    /// </summary>
    public bool IsEmpty => Executions.Count == 0;
}

/// <summary>
/// Coordinates idempotent migration planning, execution, and explicit rollback through a provider runtime.
/// </summary>
public sealed class MigrationRunner(IGraphMigrationProvider provider)
{
    /// <summary>Builds a side-effect-free plan containing migrations not present in provider history.</summary>
    public async ValueTask<MigrationPlan> PlanAsync(
        IEnumerable<NodalMigration> migrations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(migrations);
        var ordered = migrations.ToArray();
        EnsureUniqueIds(ordered);
        var applied = await provider.MigrationExecutor
            .GetAppliedMigrationsAsync(cancellationToken)
            .ConfigureAwait(false);
        var executions = new List<MigrationExecution>();
        var preflight = new Dictionary<string, MigrationPreflightResult>(
            StringComparer.Ordinal);

        var analyzer = new MigrationPreflightAnalyzer(
            provider.MigrationDialect);
        foreach (var migration in ordered)
        {
            var operations = BuildOperations(
                migration,
                upward: true);

            preflight[migration.Id] = analyzer.Analyze(
                operations);

            var execution = BuildExecution(
                migration.Id,
                operations);
            if (applied.TryGetValue(migration.Id, out var checksum))
            {
                if (!string.Equals(checksum, execution.Checksum, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Applied migration '{migration.Id}' differs from its current definition.");
                }

                continue;
            }

            executions.Add(execution);
        }

        return new MigrationPlan(
                    executions,
                    preflight);
    }



    /// <summary>
    /// Applies migrations with explicit execution safety options.
    /// </summary>
    public ValueTask<MigrationPlan> MigrateAsync(
        IEnumerable<NodalMigration> migrations,
        MigrationExecutionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(migrations);

        return MigrateWithOptionsAsync(
            migrations.ToArray(),
            options,
            cancellationToken);
    }

    private async ValueTask<MigrationPlan> MigrateWithOptionsAsync(
        IReadOnlyList<NodalMigration> migrations,
        MigrationExecutionOptions options,
        CancellationToken cancellationToken)
    {
        if (provider is not IGraphMigrationLockProvider lockProvider)
        {
            return await MigrateCoreAsync(
                    migrations,
                    options,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await using var lease = await lockProvider.MigrationLock
            .AcquireAsync(
                lockProvider.MigrationLockScope,
                cancellationToken)
            .ConfigureAwait(false);

        return await MigrateCoreAsync(
                migrations,
                options,
                cancellationToken)
            .ConfigureAwait(false);
    }


    /// <summary>
    /// Applies each pending migration in declaration order.
    /// </summary>
    /// <remarks>
    /// When the provider exposes migration locking, the complete migration
    /// application runs under one provider-scoped lease. Planning remains
    /// side-effect free and does not acquire a lock.
    /// </remarks>
    public async ValueTask<MigrationPlan> MigrateAsync(
        IEnumerable<NodalMigration> migrations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(migrations);

        var ordered = migrations.ToArray();

        if (provider is not IGraphMigrationLockProvider lockProvider)
        {
            return await MigrateCoreAsync(ordered, new MigrationExecutionOptions(), cancellationToken)
                .ConfigureAwait(false);
        }

        await using var lease = await lockProvider.MigrationLock
            .AcquireAsync(
                lockProvider.MigrationLockScope,
                cancellationToken)
            .ConfigureAwait(false);

        return await MigrateCoreAsync(ordered, new MigrationExecutionOptions(), cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask<MigrationPlan> MigrateCoreAsync(
        IReadOnlyList<NodalMigration> migrations,
        MigrationExecutionOptions options,
        CancellationToken cancellationToken)
    {
        var plan = await PlanAsync(migrations, cancellationToken)
            .ConfigureAwait(false);

        EnsureDestructiveApproval(
            migrations,
            plan,
            options);

        var history = provider is IGraphMigrationHistoryProvider historyProvider
            ? historyProvider.MigrationHistory
            : null;

        foreach (var execution in plan.Executions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var startedAt = DateTimeOffset.UtcNow;

            if (history is not null)
            {
                await history.SaveMigrationHistoryAsync(
                    new MigrationHistoryEntry(
                        execution.Id,
                        execution.Checksum,
                        MigrationExecutionState.Applying,
                        StartedAt: startedAt),
                    cancellationToken).ConfigureAwait(false);
            }

            try
            {
                await provider.MigrationExecutor
                    .ApplyAsync(execution, cancellationToken)
                    .ConfigureAwait(false);

                if (history is not null)
                {
                    await history.SaveMigrationHistoryAsync(
                        new MigrationHistoryEntry(
                            execution.Id,
                            execution.Checksum,
                            MigrationExecutionState.Applied,
                            StartedAt: startedAt,
                            CompletedAt: DateTimeOffset.UtcNow),
                        cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                if (history is not null)
                {
                    var failure = new MigrationExecutionFailure(
                        exception.Message,
                        exception.GetType().FullName
                        ?? exception.GetType().Name,
                        DateTimeOffset.UtcNow);

                    await history.SaveMigrationHistoryAsync(
                        new MigrationHistoryEntry(
                            execution.Id,
                            execution.Checksum,
                            MigrationExecutionState.Failed,
                            StartedAt: startedAt,
                            CompletedAt: DateTimeOffset.UtcNow,
                            Failure: failure),
                        cancellationToken).ConfigureAwait(false);
                }

                throw;
            }
        }

        return plan;
    }


    /// <summary>Reverts one explicitly selected reversible migration.</summary>
    public async ValueTask<MigrationExecution> RevertAsync(
        NodalMigration migration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(migration);
        if (!migration.IsReversible)
        {
            throw new InvalidOperationException($"Migration '{migration.Id}' is marked as irreversible.");
        }

        var applied = await provider.MigrationExecutor
            .GetAppliedMigrationsAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!applied.ContainsKey(migration.Id))
        {
            throw new InvalidOperationException($"Migration '{migration.Id}' has not been applied.");
        }

        var execution = BuildExecution(migration.Id, BuildOperations(migration, upward: false));
        await provider.MigrationExecutor.RevertAsync(execution, cancellationToken).ConfigureAwait(false);
        return execution;
    }

    private MigrationExecution BuildUpExecution(NodalMigration migration) =>
        BuildExecution(migration.Id, BuildOperations(migration, upward: true));

    private void EnsureDestructiveApproval(
    IReadOnlyList<NodalMigration> migrations,
    MigrationPlan plan,
    MigrationExecutionOptions options)
    {
        if (options.AllowDestructiveOperations)
        {
            return;
        }

        var migrationsById = migrations.ToDictionary(
            migration => migration.Id,
            StringComparer.Ordinal);

        var analyzer = new MigrationPreflightAnalyzer(
            provider.MigrationDialect);

        foreach (var execution in plan.Executions)
        {
            var migration = migrationsById[execution.Id];

            var result = analyzer.Analyze(
                BuildOperations(migration, upward: true));

            if (!result.RequiresApproval)
            {
                continue;
            }

            var destructiveOperations = result.Issues
                .Where(issue =>
                    issue.Kind is MigrationPreflightKind.Destructive)
                .Select(issue => issue.OperationType.Name)
                .Distinct(StringComparer.Ordinal);

            var operationNames = string.Join(
                ", ",
                destructiveOperations);

            throw new InvalidOperationException(
                $"Migration '{migration.Id}' contains destructive " +
                $"operations ({operationNames}). " +
                "Set AllowDestructiveOperations to true to approve execution.");
        }
    }

    private MigrationExecution BuildExecution(
    string id,
    IReadOnlyList<MigrationOperation> operations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(operations);

        var preflight = new MigrationPreflightAnalyzer(
            provider.MigrationDialect)
            .Analyze(operations);

        // Unsupported operations must fail before any provider transport call.
        preflight.ThrowIfInvalid();

        var commands = provider.MigrationDialect
            .Compile(operations);

        var canonical = MigrationCanonicalizer
            .Build(operations, commands);

        var checksum = Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();

        return new MigrationExecution(
            id,
            checksum,
            commands);
    }

    private static IReadOnlyList<MigrationOperation> BuildOperations(NodalMigration migration, bool upward)
    {
        var builder = new MigrationBuilder();
        if (upward)
        {
            migration.Up(builder);
        }
        else
        {
            migration.Down(builder);
        }

        return builder.Operations;
    }

    private static void EnsureUniqueIds(
    IEnumerable<NodalMigration> migrations)
    {
        var ordered = migrations.ToArray();

        var emptyId = ordered.FirstOrDefault(
            migration => string.IsNullOrWhiteSpace(migration.Id));

        if (emptyId is not null)
        {
            throw new InvalidOperationException(
                "Every migration must define a non-empty stable identifier.");
        }

        var duplicate = ordered
            .GroupBy(
                migration => migration.Id,
                StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Migration identifier '{duplicate.Key}' is duplicated.");
        }

        for (var index = 1; index < ordered.Length; index++)
        {
            var previous = ordered[index - 1].Id;
            var current = ordered[index].Id;

            if (string.Compare(
                    previous,
                    current,
                    StringComparison.Ordinal) >= 0)
            {
                throw new InvalidOperationException(
                    $"Migrations must be declared in strictly increasing " +
                    $"identifier order. '{previous}' must come before '{current}'.");
            }
        }
    }
}
