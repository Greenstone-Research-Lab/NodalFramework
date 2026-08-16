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

    /// <summary>Applies each pending migration in declaration order.</summary>
    public async ValueTask<MigrationPlan> MigrateAsync(
        IEnumerable<NodalMigration> migrations,
        CancellationToken cancellationToken = default)
    {
        var plan = await PlanAsync(migrations, cancellationToken).ConfigureAwait(false);
        foreach (var execution in plan.Executions)
        {
            await provider.MigrationExecutor.ApplyAsync(execution, cancellationToken).ConfigureAwait(false);
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
        var canonical = string.Join("\n", commands.Select(command =>
            $"{command.Kind}|{command.IsTransactional}|{command.Text}"));
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
