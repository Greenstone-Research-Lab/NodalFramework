namespace Nodal.Core.Migrations;

/// <summary>Persists resumable state for a bounded migration backfill.</summary>
public interface IMigrationBackfillCheckpointStore
{
    /// <summary>Reads the last durably acknowledged checkpoint for a backfill.</summary>
    ValueTask<MigrationBackfillCheckpoint?> GetAsync(
        string backfillName,
        CancellationToken cancellationToken = default);

    /// <summary>Atomically replaces the checkpoint after a successful batch.</summary>
    ValueTask SaveAsync(
        MigrationBackfillCheckpoint checkpoint,
        CancellationToken cancellationToken = default);

    /// <summary>Removes the checkpoint after successful completion.</summary>
    ValueTask RemoveAsync(
        string backfillName,
        CancellationToken cancellationToken = default);
}

/// <summary>Durable continuation state acknowledged for one backfill.</summary>
public sealed record MigrationBackfillCheckpoint(
    string BackfillName,
    string? ContinuationToken,
    int Processed,
    DateTimeOffset UpdatedAt);
