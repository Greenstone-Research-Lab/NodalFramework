using System.Globalization;
using Neo4j.Driver;
using Nodal.Core.Migrations;

namespace Nodal.Neo4j;

/// <summary>Persists bounded backfill checkpoints as dedicated Neo4j metadata nodes.</summary>
/// <param name="driver">The shared Neo4j driver used to execute checkpoint operations.</param>
/// <param name="database">The optional Neo4j database name.</param>
public sealed class Neo4jMigrationCheckpointStore(
    IDriver driver,
    string? database = null) : IMigrationBackfillCheckpointStore
{
    private const string Label = "__NodalBackfillCheckpoint";
    private readonly IDriver driver = driver ?? throw new ArgumentNullException(nameof(driver));
    private readonly string? database = database;

    /// <inheritdoc />
    public async ValueTask<MigrationBackfillCheckpoint?> GetAsync(string backfillName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backfillName);
        await using var session = driver.AsyncSession(builder => Configure(builder));
        return await session.ExecuteReadAsync(async transaction =>
        {
            var cursor = await transaction.RunAsync(
                $"MATCH (checkpoint:{Label} {{Name: $name}}) RETURN checkpoint.Token AS token, checkpoint.Processed AS processed, checkpoint.UpdatedAt AS updatedAt",
                new Dictionary<string, object> { ["name"] = backfillName }).ConfigureAwait(false);
            var records = await cursor.ToListAsync(cancellationToken).ConfigureAwait(false);
            if (records.Count == 0) return null;
            var record = records[0];
            return new MigrationBackfillCheckpoint(
                backfillName,
                record["token"].As<string?>(),
                record["processed"].As<int>(),
                DateTimeOffset.Parse(record["updatedAt"].As<string>(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask SaveAsync(MigrationBackfillCheckpoint checkpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        await using var session = driver.AsyncSession(builder => Configure(builder));
        await session.ExecuteWriteAsync(async transaction =>
        {
            var cursor = await transaction.RunAsync(
                $"MERGE (checkpoint:{Label} {{Name: $name}}) SET checkpoint.Token = $token, checkpoint.Processed = $processed, checkpoint.UpdatedAt = $updatedAt",
                new Dictionary<string, object?>
                {
                    ["name"] = checkpoint.BackfillName,
                    ["token"] = checkpoint.ContinuationToken,
                    ["processed"] = checkpoint.Processed,
                    ["updatedAt"] = checkpoint.UpdatedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                }).ConfigureAwait(false);
            await cursor.ConsumeAsync().ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask RemoveAsync(string backfillName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backfillName);
        await using var session = driver.AsyncSession(builder => Configure(builder));
        await session.ExecuteWriteAsync(async transaction =>
        {
            var cursor = await transaction.RunAsync(
                $"MATCH (checkpoint:{Label} {{Name: $name}}) DELETE checkpoint",
                new Dictionary<string, object> { ["name"] = backfillName }).ConfigureAwait(false);
            await cursor.ConsumeAsync().ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    private void Configure(SessionConfigBuilder builder)
    {
        if (!string.IsNullOrWhiteSpace(database)) builder.WithDatabase(database);
    }
}
