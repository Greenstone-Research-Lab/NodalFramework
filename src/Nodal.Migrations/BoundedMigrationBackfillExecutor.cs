using Nodal.Core.Migrations;

namespace Nodal.Migrations;

/// <summary>
/// Executes backfill batches sequentially with a bounded batch-size contract.
/// </summary>
public sealed class BoundedMigrationBackfillExecutor : IMigrationBackfillExecutor
{
    /// <inheritdoc />
    public async ValueTask ExecuteAsync(
        MigrationBackfillRequest request,
        Func<MigrationBackfillContext, CancellationToken, ValueTask<MigrationBackfillBatchResult>> executeBatch,
        CancellationToken cancellationToken = default)
        => await ExecuteCoreAsync(request, executeBatch, checkpointStore: null, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask ExecuteAsync(
        MigrationBackfillRequest request,
        Func<MigrationBackfillContext, CancellationToken, ValueTask<MigrationBackfillBatchResult>> executeBatch,
        IMigrationBackfillCheckpointStore checkpointStore,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpointStore);
        await ExecuteCoreAsync(request, executeBatch, checkpointStore, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask ExecuteCoreAsync(
        MigrationBackfillRequest request,
        Func<MigrationBackfillContext, CancellationToken, ValueTask<MigrationBackfillBatchResult>> executeBatch,
        IMigrationBackfillCheckpointStore? checkpointStore,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(executeBatch);

        var checkpoint = checkpointStore is null
            ? null
            : await checkpointStore.GetAsync(request.Name, cancellationToken).ConfigureAwait(false);
        string? continuationToken = checkpoint?.ContinuationToken;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await executeBatch(
                    new MigrationBackfillContext(
                        continuationToken,
                        request.BatchSize),
                    cancellationToken)
                .ConfigureAwait(false);

            if (result.Processed < 0 || result.Processed > request.BatchSize)
            {
                throw new InvalidOperationException(
                    $"Backfill '{request.Name}' reported {result.Processed} " +
                    $"records for a batch of {request.BatchSize}.");
            }

            if (result.IsCompleted)
            {
                if (checkpointStore is not null)
                {
                    await checkpointStore.RemoveAsync(request.Name, cancellationToken).ConfigureAwait(false);
                }
                return;
            }

            if (string.IsNullOrWhiteSpace(result.ContinuationToken))
            {
                throw new InvalidOperationException(
                    $"Backfill '{request.Name}' must provide a continuation " +
                    "token when more batches remain.");
            }

            continuationToken = result.ContinuationToken;
            if (checkpointStore is not null)
            {
                await checkpointStore.SaveAsync(
                    new MigrationBackfillCheckpoint(
                        request.Name,
                        continuationToken,
                        result.Processed,
                        DateTimeOffset.UtcNow),
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
