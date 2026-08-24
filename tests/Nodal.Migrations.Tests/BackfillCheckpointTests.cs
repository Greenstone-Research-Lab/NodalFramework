using Nodal.Core.Migrations;
using Nodal.Migrations;

namespace Nodal.Migrations.Tests;

public sealed class BackfillCheckpointTests
{
    [Fact]
    public async Task ResumesFromCheckpointAndRemovesItAfterCompletion()
    {
        var store = new RecordingCheckpointStore(new MigrationBackfillCheckpoint(
            "normalize", "page-2", 2, DateTimeOffset.UtcNow));
        var seen = new List<string?>();

        await new BoundedMigrationBackfillExecutor().ExecuteAsync(
            new MigrationBackfillRequest("normalize", 2),
            (context, _) =>
            {
                seen.Add(context.ContinuationToken);
                return ValueTask.FromResult(new MigrationBackfillBatchResult(1, null, true));
            },
            store);

        Assert.Equal(["page-2"], seen);
        Assert.Equal("normalize", store.RemovedName);
    }

    [Fact]
    public async Task PersistsCheckpointOnlyAfterIncompleteBatch()
    {
        var store = new RecordingCheckpointStore();
        await new BoundedMigrationBackfillExecutor().ExecuteAsync(
            new MigrationBackfillRequest("normalize", 2),
            (context, _) => ValueTask.FromResult(
                context.ContinuationToken is null
                    ? new MigrationBackfillBatchResult(2, "page-2", false)
                    : new MigrationBackfillBatchResult(1, null, true)),
            store);

        Assert.Equal("page-2", store.Saved!.ContinuationToken);
        Assert.Equal(2, store.Saved.Processed);
    }

    private sealed class RecordingCheckpointStore(MigrationBackfillCheckpoint? checkpoint = null) : IMigrationBackfillCheckpointStore
    {
        private readonly MigrationBackfillCheckpoint? checkpoint = checkpoint;
        public MigrationBackfillCheckpoint? Saved { get; private set; }
        public string? RemovedName { get; private set; }

        public ValueTask<MigrationBackfillCheckpoint?> GetAsync(string backfillName, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(checkpoint);

        public ValueTask SaveAsync(MigrationBackfillCheckpoint value, CancellationToken cancellationToken = default)
        {
            Saved = value;
            return ValueTask.CompletedTask;
        }

        public ValueTask RemoveAsync(string backfillName, CancellationToken cancellationToken = default)
        {
            RemovedName = backfillName;
            return ValueTask.CompletedTask;
        }
    }
}
