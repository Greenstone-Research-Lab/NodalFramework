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
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(executeBatch);

        string? continuationToken = null;

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
                return;
            }

            if (string.IsNullOrWhiteSpace(result.ContinuationToken))
            {
                throw new InvalidOperationException(
                    $"Backfill '{request.Name}' must provide a continuation " +
                    "token when more batches remain.");
            }

            continuationToken = result.ContinuationToken;
        }
    }
}
