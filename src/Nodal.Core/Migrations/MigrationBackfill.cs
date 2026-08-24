namespace Nodal.Core.Migrations;

/// <summary>Describes a bounded, resumable data backfill.</summary>
public sealed record MigrationBackfillRequest
{
    /// <summary>Initializes a validated backfill request.</summary>
    public MigrationBackfillRequest(string name, int batchSize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        Name = name;
        BatchSize = batchSize;
    }

    /// <summary>Gets the stable backfill name.</summary>
    public string Name { get; }

    /// <summary>Gets the maximum number of records processed per batch.</summary>
    public int BatchSize { get; }
}

/// <summary>Provides durable continuation information for one batch.</summary>
public sealed record MigrationBackfillContext(
    string? ContinuationToken,
    int BatchSize);

/// <summary>Reports one completed backfill batch.</summary>
public sealed record MigrationBackfillBatchResult(
    int Processed,
    string? ContinuationToken,
    bool IsCompleted)
{
    /// <summary>Gets whether another batch should be requested.</summary>
    public bool HasMore => !IsCompleted;
}

/// <summary>Executes bounded backfill batches with cancellation support.</summary>
public interface IMigrationBackfillExecutor
{
    /// <summary>Executes a backfill until completion or cancellation.</summary>
    ValueTask ExecuteAsync(
        MigrationBackfillRequest request,
        Func<MigrationBackfillContext, CancellationToken, ValueTask<MigrationBackfillBatchResult>> executeBatch,
        CancellationToken cancellationToken = default);

    /// <summary>Executes a backfill while persisting checkpoints after each successful batch.</summary>
    ValueTask ExecuteAsync(
        MigrationBackfillRequest request,
        Func<MigrationBackfillContext, CancellationToken, ValueTask<MigrationBackfillBatchResult>> executeBatch,
        IMigrationBackfillCheckpointStore checkpointStore,
        CancellationToken cancellationToken = default);
}
