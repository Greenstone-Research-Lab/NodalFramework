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
                "WHERE `migration`.`State` IS NULL OR `migration`.`State` = 'Applied' " +
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
        var hasTransactional = execution.Commands.Any(command => command.IsTransactional);
        var hasNonTransactional = execution.Commands.Any(command => !command.IsTransactional);
        if (hasTransactional && hasNonTransactional)
        {
            throw new NotSupportedException(
                "Neo4j cannot combine graph-write and schema migration commands in one execution.");
        }

        if (hasNonTransactional)
        {
            await ExecuteCommandsAsync(execution.Commands, cancellationToken).ConfigureAwait(false);
            await ExecuteHistoryAsync(execution, upward, cancellationToken).ConfigureAwait(false);
            return;
        }

        await ExecuteTransactionAsync(async transaction =>
        {
            await RunCommandsAsync(transaction, execution.Commands, cancellationToken).ConfigureAwait(false);
            await RunHistoryAsync(transaction, execution, upward).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    private async ValueTask ExecuteCommandsAsync(
        IReadOnlyList<MigrationCommand> commands,
        CancellationToken cancellationToken) =>
        await ExecuteTransactionAsync(transaction =>
            RunCommandsAsync(transaction, commands, cancellationToken)).ConfigureAwait(false);

    private async ValueTask ExecuteHistoryAsync(
        MigrationExecution execution,
        bool upward,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await ExecuteTransactionAsync(transaction =>
            RunHistoryAsync(transaction, execution, upward)).ConfigureAwait(false);
    }

    private async ValueTask ExecuteTransactionAsync(Func<IAsyncQueryRunner, Task> work)
    {
        await using var session = driver.AsyncSession(builder => ConfigureSession(builder, database));
        await session.ExecuteWriteAsync(work).ConfigureAwait(false);
    }

    private static async Task RunCommandsAsync(
        IAsyncQueryRunner transaction,
        IReadOnlyList<MigrationCommand> commands,
        CancellationToken cancellationToken)
    {
        foreach (var command in commands)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cursor = await transaction.RunAsync(command.Text).ConfigureAwait(false);
            await cursor.ConsumeAsync().ConfigureAwait(false);
        }
    }

    private static async Task RunHistoryAsync(
        IAsyncQueryRunner transaction,
        MigrationExecution execution,
        bool upward)
    {
        var historyCommand = upward
            ? "MERGE (`migration`:`__NodalMigration` {`Id`: $id}) " +
              "SET `migration`.`Checksum` = $checksum, " +
              "`migration`.`State` = 'Applied', " +
              "`migration`.`AppliedAt` = datetime(), " +
              "`migration`.`CompletedAt` = datetime(), " +
              "`migration`.`FailureMessage` = null, " +
              "`migration`.`FailureType` = null, " +
              "`migration`.`FailureAt` = null"
            : "MATCH (`migration`:`__NodalMigration` {`Id`: $id}) DELETE `migration`";
        var history = await transaction.RunAsync(
            historyCommand,
            new Dictionary<string, object>
            {
                ["id"] = execution.Id,
                ["checksum"] = execution.Checksum,
            }).ConfigureAwait(false);
        await history.ConsumeAsync().ConfigureAwait(false);
    }

    private static void ConfigureSession(SessionConfigBuilder builder, string? database)
    {
        if (!string.IsNullOrWhiteSpace(database))
        {
            builder.WithDatabase(database);
        }
    }
}
