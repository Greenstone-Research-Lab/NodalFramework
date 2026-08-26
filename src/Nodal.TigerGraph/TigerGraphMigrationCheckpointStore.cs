using Nodal.Core.Migrations;

namespace Nodal.TigerGraph;

/// <summary>Adapts an explicit TigerGraph checkpoint transport to Nodal's durable store contract.</summary>
/// <param name="transport">The deployment-specific durable checkpoint transport.</param>
public sealed class TigerGraphMigrationCheckpointStore(
    ITigerGraphBackfillCheckpointTransport transport) : IMigrationBackfillCheckpointStore
{
    private readonly ITigerGraphBackfillCheckpointTransport transport =
        transport ?? throw new ArgumentNullException(nameof(transport));

    /// <inheritdoc />
    public ValueTask<MigrationBackfillCheckpoint?> GetAsync(string backfillName, CancellationToken cancellationToken = default) =>
        transport.GetCheckpointAsync(backfillName, cancellationToken);

    /// <inheritdoc />
    public ValueTask SaveAsync(MigrationBackfillCheckpoint checkpoint, CancellationToken cancellationToken = default) =>
        transport.SaveCheckpointAsync(checkpoint, cancellationToken);

    /// <inheritdoc />
    public ValueTask RemoveAsync(string backfillName, CancellationToken cancellationToken = default) =>
        transport.RemoveCheckpointAsync(backfillName, cancellationToken);
}
