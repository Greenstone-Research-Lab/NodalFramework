using Nodal.Core.Migrations;

namespace Nodal.TigerGraph;

/// <summary>Provides durable checkpoint storage through a supported TigerGraph channel.</summary>
public interface ITigerGraphBackfillCheckpointTransport
{
    /// <summary>Reads one checkpoint by backfill name.</summary>
    ValueTask<MigrationBackfillCheckpoint?> GetCheckpointAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Stores one checkpoint atomically.</summary>
    ValueTask SaveCheckpointAsync(MigrationBackfillCheckpoint checkpoint, CancellationToken cancellationToken = default);

    /// <summary>Removes one completed checkpoint.</summary>
    ValueTask RemoveCheckpointAsync(string name, CancellationToken cancellationToken = default);
}
