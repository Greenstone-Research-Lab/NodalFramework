using Neo4j.Driver;
using Nodal.Core.Migrations;

namespace Nodal.Neo4j;

/// <summary>Executes transactional Neo4j migrations and persists their checksummed history.</summary>
public sealed class Neo4jMigrationExecutor(IDriver driver, string? database = null) : IGraphMigrationExecutor
{
    /// <inheritdoc />
    public async ValueTask<IReadOnlyDictionary<string, string>> GetAppliedMigrationsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var session = driver.AsyncSession(builder => ConfigureSession(builder, database));
        var ids = await session.ExecuteReadAsync(async transaction =>
        {
            var cursor = await transaction.RunAsync(
                "MATCH (`migration`:`__NodalMigration`) " +
                "RETURN `migration`.`Id` AS `id`, `migration`.`Checksum` AS `checksum`").ConfigureAwait(false);
            var records = await cursor.ToListAsync(cancellationToken).ConfigureAwait(false);
            return records.ToDictionary(
                record => record["id"].As<string>(),
                record => record["checksum"].As<string>(),
                StringComparer.Ordinal);
        }).ConfigureAwait(false);
        return ids;
    }

    /// <inheritdoc />
    public ValueTask ApplyAsync(MigrationExecution execution, CancellationToken cancellationToken = default) =>
        ExecuteAsync(execution, upward: true, cancellationToken);

    /// <inheritdoc />
    public ValueTask RevertAsync(MigrationExecution execution, CancellationToken cancellationToken = default) =>
        ExecuteAsync(execution, upward: false, cancellationToken);

    private async ValueTask ExecuteAsync(
        MigrationExecution execution,
        bool upward,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);
        cancellationToken.ThrowIfCancellationRequested();
        var nonTransactional = execution.Commands.FirstOrDefault(command => !command.IsTransactional);
        if (nonTransactional is not null)
        {
            throw new NotSupportedException(
                "Neo4j migration execution requires every command to support the same transaction boundary.");
        }

        await using var session = driver.AsyncSession(builder => ConfigureSession(builder, database));
        await session.ExecuteWriteAsync(async transaction =>
        {
            foreach (var command in execution.Commands)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var cursor = await transaction.RunAsync(command.Text).ConfigureAwait(false);
                await cursor.ConsumeAsync().ConfigureAwait(false);
            }

            var historyCommand = upward
                ? "MERGE (`migration`:`__NodalMigration` {`Id`: $id}) " +
                  "SET `migration`.`Checksum` = $checksum, `migration`.`AppliedAt` = datetime()"
                : "MATCH (`migration`:`__NodalMigration` {`Id`: $id}) DELETE `migration`";
            var history = await transaction.RunAsync(
                historyCommand,
                new Dictionary<string, object>
                {
                    ["id"] = execution.Id,
                    ["checksum"] = execution.Checksum,
                }).ConfigureAwait(false);
            await history.ConsumeAsync().ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    private static void ConfigureSession(SessionConfigBuilder builder, string? database)
    {
        if (!string.IsNullOrWhiteSpace(database))
        {
            builder.WithDatabase(database);
        }
    }
}
