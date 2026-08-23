using System.Security.Cryptography;
using System.Text;
using Nodal.Core.Migrations;

namespace Nodal.Migrations;

/// <summary>Represents an immutable dry-run plan containing only pending migrations.</summary>
public sealed record MigrationPlan(IReadOnlyList<MigrationExecution> Executions)
{
    /// <summary>Gets whether the target graph is already up to date.</summary>
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
        foreach (var migration in ordered)
        {
            var execution = BuildUpExecution(migration);
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

        return new MigrationPlan(executions);
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
            return await MigrateCoreAsync(ordered, cancellationToken)
                .ConfigureAwait(false);
        }

        await using var lease = await lockProvider.MigrationLock
            .AcquireAsync(
                lockProvider.MigrationLockScope,
                cancellationToken)
            .ConfigureAwait(false);

        return await MigrateCoreAsync(ordered, cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask<MigrationPlan> MigrateCoreAsync(
        IReadOnlyList<NodalMigration> migrations,
        CancellationToken cancellationToken)
    {
        var plan = await PlanAsync(migrations, cancellationToken)
            .ConfigureAwait(false);

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

    private MigrationExecution BuildExecution(string id, IReadOnlyList<MigrationOperation> operations)
    {
        var commands = provider.MigrationDialect.Compile(operations);
        var canonical = MigrationCanonicalizer.Build(operations, commands);
        var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        return new MigrationExecution(id, checksum, commands);
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

    private static void EnsureUniqueIds(IEnumerable<NodalMigration> migrations)
    {
        var duplicate = migrations.GroupBy(migration => migration.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Migration identifier '{duplicate.Key}' is duplicated.");
        }
    }
}
