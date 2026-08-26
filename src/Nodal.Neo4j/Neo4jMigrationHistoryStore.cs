using System.Globalization;
using Neo4j.Driver;
using Nodal.Core.Migrations;

namespace Nodal.Neo4j;

/// <summary>
/// Persists stateful Nodal migration history in Neo4j.
/// </summary>
public sealed class Neo4jMigrationHistoryStore : IGraphMigrationHistoryStore
{
    private const string HistoryLabel = "__NodalMigration";

    private readonly IDriver driver;
    private readonly string? database;

    /// <summary>
    /// Initializes a Neo4j migration history store.
    /// </summary>
    /// <param name="driver">The shared Neo4j driver.</param>
    /// <param name="database">The optional Neo4j database name.</param>
    public Neo4jMigrationHistoryStore(
        IDriver driver,
        string? database = null)
    {
        ArgumentNullException.ThrowIfNull(driver);

        this.driver = driver;
        this.database = database;
    }

    /// <inheritdoc />
    public async ValueTask<
        IReadOnlyDictionary<string, MigrationHistoryEntry>>
        GetMigrationHistoryAsync(
            CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var session = driver.AsyncSession(
            builder => ConfigureSession(builder, database));

        return await session.ExecuteReadAsync(
            async transaction =>
            {
                var cursor = await transaction.RunAsync(
                    """
                    MATCH (migration:__NodalMigration)
                    RETURN
                        migration.Id AS id,
                        migration.Checksum AS checksum,
                        coalesce(migration.State, 'Applied') AS state,
                        coalesce(migration.StartedAt, '') AS startedAt,
                        coalesce(migration.CompletedAt, '') AS completedAt,
                        coalesce(migration.FailureMessage, '') AS failureMessage,
                        coalesce(migration.FailureType, '') AS failureType,
                        coalesce(migration.FailureAt, '') AS failureAt
                    """).ConfigureAwait(false);

                var records = await cursor
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                return records.ToDictionary(
                    record => record["id"].As<string>(),
                    ParseEntry,
                    StringComparer.Ordinal);
            }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask SaveMigrationHistoryAsync(
        MigrationHistoryEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();

        await using var session = driver.AsyncSession(
            builder => ConfigureSession(builder, database));

        await session.ExecuteWriteAsync(
            async transaction =>
            {
                var cursor = await transaction.RunAsync(
                    """
                    MERGE (migration:__NodalMigration {Id: $id})
                    SET migration.Checksum = $checksum,
                        migration.State = $state,
                        migration.StartedAt = $startedAt,
                        migration.CompletedAt = $completedAt,
                        migration.FailureMessage = $failureMessage,
                        migration.FailureType = $failureType,
                        migration.FailureAt = $failureAt
                    """,
                    new Dictionary<string, object?>
                    {
                        ["id"] = entry.Id,
                        ["checksum"] = entry.Checksum,
                        ["state"] = entry.State.ToString(),
                        ["startedAt"] = Format(entry.StartedAt),
                        ["completedAt"] = Format(entry.CompletedAt),
                        ["failureMessage"] = entry.Failure?.Message,
                        ["failureType"] = entry.Failure?.ErrorType,
                        ["failureAt"] = Format(entry.Failure?.OccurredAt)
                    }).ConfigureAwait(false);

                await cursor.ConsumeAsync().ConfigureAwait(false);
            }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask RemoveMigrationHistoryAsync(
        string migrationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(migrationId);
        cancellationToken.ThrowIfCancellationRequested();

        await using var session = driver.AsyncSession(
            builder => ConfigureSession(builder, database));

        await session.ExecuteWriteAsync(
            async transaction =>
            {
                var cursor = await transaction.RunAsync(
                    """
                    MATCH (migration:__NodalMigration {Id: $id})
                    DELETE migration
                    """,
                    new Dictionary<string, object>
                    {
                        ["id"] = migrationId
                    }).ConfigureAwait(false);

                await cursor.ConsumeAsync().ConfigureAwait(false);
            }).ConfigureAwait(false);
    }

    private static MigrationHistoryEntry ParseEntry(IRecord record)
    {
        var id = record["id"].As<string>();
        var checksum = record["checksum"].As<string>();
        var state = ParseState(record["state"].As<string>());
        var startedAt = ParseTimestamp(record["startedAt"].As<string>());
        var completedAt = ParseTimestamp(record["completedAt"].As<string>());
        var failureMessage = record["failureMessage"].As<string>();
        var failureType = record["failureType"].As<string>();
        var failureAt = ParseTimestamp(record["failureAt"].As<string>());

        MigrationExecutionFailure? failure = null;

        if (!string.IsNullOrWhiteSpace(failureMessage) &&
            !string.IsNullOrWhiteSpace(failureType) &&
            failureAt.HasValue)
        {
            failure = new MigrationExecutionFailure(
                failureMessage,
                failureType,
                failureAt.Value);
        }

        return new MigrationHistoryEntry(
            id,
            checksum,
            state,
            startedAt,
            completedAt,
            failure);
    }

    private static MigrationExecutionState ParseState(string value) =>
        Enum.TryParse<MigrationExecutionState>(
            value,
            ignoreCase: true,
            out var state)
            ? state
            : MigrationExecutionState.Applied;

    private static string? Format(DateTimeOffset? value) =>
        value?.ToUniversalTime().ToString(
            "O",
            CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseTimestamp(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : DateTimeOffset.Parse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind);

    private static void ConfigureSession(
        SessionConfigBuilder builder,
        string? database)
    {
        if (!string.IsNullOrWhiteSpace(database))
        {
            builder.WithDatabase(database);
        }
    }
}
