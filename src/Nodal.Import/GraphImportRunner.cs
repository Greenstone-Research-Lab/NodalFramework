using System.Runtime.CompilerServices;

namespace Nodal.Import;

/// <summary>Streams source records into bounded handler invocations without retaining the full source in memory.</summary>
/// <typeparam name="TRecord">The source record type.</typeparam>
public sealed class GraphImportRunner<TRecord>(IGraphImportBatchHandler<TRecord> handler)
{
    /// <summary>Runs an ordered source through the configured batch handler.</summary>
    /// <param name="source">Asynchronous source records.</param>
    /// <param name="options">Bounded execution options.</param>
    /// <param name="cancellationToken">Token that cancels source consumption and handler execution.</param>
    /// <returns>A complete, payload-safe import report.</returns>
    public async ValueTask<GraphImportResult> RunAsync(
        IAsyncEnumerable<TRecord> source,
        GraphImportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(handler);
        options ??= new GraphImportOptions();
        options.Validate();

        var records = new List<TRecord>(options.BatchSize);
        var diagnostics = new List<GraphImportDiagnostic>();
        long read = 0;
        var nodes = 0;
        var relations = 0;

        await foreach (var record in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            records.Add(record);
            read++;
            if (records.Count == options.BatchSize)
            {
                var result = await ProcessBatchAsync(records, read - records.Count + 1, options, cancellationToken).ConfigureAwait(false);
                nodes += result.ImportedNodeCount;
                relations += result.ImportedRelationCount;
                diagnostics.AddRange(result.Diagnostics);
                if (!result.Succeeded && options.FailureMode == GraphImportFailureMode.FailFast)
                {
                    return new GraphImportResult(read, nodes, relations, diagnostics);
                }
                records.Clear();
            }
        }

        if (records.Count > 0)
        {
            var result = await ProcessBatchAsync(records, read - records.Count + 1, options, cancellationToken).ConfigureAwait(false);
            nodes += result.ImportedNodeCount;
            relations += result.ImportedRelationCount;
            diagnostics.AddRange(result.Diagnostics);
        }

        return new GraphImportResult(read, nodes, relations, diagnostics);
    }

    private ValueTask<GraphImportBatchResult> ProcessBatchAsync(
        List<TRecord> records,
        long firstRecordNumber,
        GraphImportOptions options,
        CancellationToken cancellationToken) =>
        handler.HandleAsync(new GraphImportBatch<TRecord>(firstRecordNumber, records.ToArray()), options, cancellationToken);
}
