using Nodal.Core.Migrations;

namespace Nodal.TigerGraph.Tests;

public sealed class TigerGraphMigrationCheckpointStoreTests
{
    [Fact]
    public async Task DelegatesCheckpointLifecycleAndCancellationToTransport()
    {
        using var cancellation = new CancellationTokenSource();
        var checkpoint = new MigrationBackfillCheckpoint(
            "normalize",
            "page-2",
            25,
            DateTimeOffset.UtcNow);
        var transport = new RecordingCheckpointTransport(checkpoint);
        var store = new TigerGraphMigrationCheckpointStore(transport);

        var loaded = await store.GetAsync(checkpoint.BackfillName, cancellation.Token);
        await store.SaveAsync(checkpoint, cancellation.Token);
        await store.RemoveAsync(checkpoint.BackfillName, cancellation.Token);

        Assert.Same(checkpoint, loaded);
        Assert.Same(checkpoint, transport.SavedCheckpoint);
        Assert.Equal(checkpoint.BackfillName, transport.ReadName);
        Assert.Equal(checkpoint.BackfillName, transport.RemovedName);
        Assert.All(transport.CancellationTokens, token => Assert.Equal(cancellation.Token, token));
    }

    [Fact]
    public void RejectsMissingTransport()
    {
        Assert.Throws<ArgumentNullException>(
            () => new TigerGraphMigrationCheckpointStore(null!));
    }

    private sealed class RecordingCheckpointTransport(MigrationBackfillCheckpoint checkpoint)
        : ITigerGraphBackfillCheckpointTransport
    {
        public List<CancellationToken> CancellationTokens { get; } = [];

        public string? ReadName { get; private set; }

        public string? RemovedName { get; private set; }

        public MigrationBackfillCheckpoint? SavedCheckpoint { get; private set; }

        public ValueTask<MigrationBackfillCheckpoint?> GetCheckpointAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            ReadName = name;
            CancellationTokens.Add(cancellationToken);
            return ValueTask.FromResult<MigrationBackfillCheckpoint?>(checkpoint);
        }

        public ValueTask SaveCheckpointAsync(
            MigrationBackfillCheckpoint value,
            CancellationToken cancellationToken = default)
        {
            SavedCheckpoint = value;
            CancellationTokens.Add(cancellationToken);
            return ValueTask.CompletedTask;
        }

        public ValueTask RemoveCheckpointAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            RemovedName = name;
            CancellationTokens.Add(cancellationToken);
            return ValueTask.CompletedTask;
        }
    }
}
