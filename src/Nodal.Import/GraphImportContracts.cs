namespace Nodal.Import;

/// <summary>Controls the outcome when an import handler reports a row-level failure.</summary>
public enum GraphImportFailureMode
{
    /// <summary>Stops after the first failed batch.</summary>
    FailFast,

    /// <summary>Records a failed batch and continues with subsequent batches.</summary>
    Continue,
}

/// <summary>Configures bounded execution of a graph import.</summary>
/// <param name="BatchSize">Maximum source records passed to one handler invocation.</param>
/// <param name="FailureMode">Defines whether a reported batch failure stops the import.</param>
/// <param name="ValidateOnly">Indicates that a handler should validate without persisting changes.</param>
public sealed record GraphImportOptions(
    int BatchSize = 500,
    GraphImportFailureMode FailureMode = GraphImportFailureMode.FailFast,
    bool ValidateOnly = false)
{
    /// <summary>Validates the supplied options.</summary>
    public void Validate()
    {
        if (BatchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(BatchSize), "The import batch size must be greater than zero.");
        }
    }
}

/// <summary>Describes a single bounded portion of an ordered source.</summary>
/// <typeparam name="TRecord">The source record type.</typeparam>
/// <param name="FirstRecordNumber">One-based source record number of the first item.</param>
/// <param name="Records">Records in source order.</param>
public sealed record GraphImportBatch<TRecord>(long FirstRecordNumber, IReadOnlyList<TRecord> Records);

/// <summary>Describes a validation, mapping, or persistence observation during an import.</summary>
/// <param name="RecordNumber">One-based source record number, when known.</param>
/// <param name="Code">Stable, machine-readable diagnostic code.</param>
/// <param name="Message">Safe diagnostic message that does not need to include source payload data.</param>
public sealed record GraphImportDiagnostic(long? RecordNumber, string Code, string Message);

/// <summary>Reports the outcome of one import batch.</summary>
/// <param name="ImportedNodeCount">Number of nodes accepted by the handler.</param>
/// <param name="ImportedRelationCount">Number of relations accepted by the handler.</param>
/// <param name="Diagnostics">Diagnostics produced while processing the batch.</param>
public sealed record GraphImportBatchResult(
    int ImportedNodeCount,
    int ImportedRelationCount,
    IReadOnlyList<GraphImportDiagnostic> Diagnostics)
{
    /// <summary>Gets whether the handler reported at least one error diagnostic.</summary>
    public bool Succeeded => Diagnostics.All(diagnostic => !diagnostic.Code.StartsWith("ERROR", StringComparison.Ordinal));
}

/// <summary>Aggregates the observable result of a completed import.</summary>
/// <param name="ReadRecordCount">Number of source records read.</param>
/// <param name="ImportedNodeCount">Number of nodes accepted across all batches.</param>
/// <param name="ImportedRelationCount">Number of relations accepted across all batches.</param>
/// <param name="Diagnostics">Diagnostics in source order.</param>
public sealed record GraphImportResult(
    long ReadRecordCount,
    int ImportedNodeCount,
    int ImportedRelationCount,
    IReadOnlyList<GraphImportDiagnostic> Diagnostics)
{
    /// <summary>Gets whether no error diagnostics were reported.</summary>
    public bool Succeeded => Diagnostics.All(diagnostic => !diagnostic.Code.StartsWith("ERROR", StringComparison.Ordinal));
}

/// <summary>Handles one bounded group of source records at the application composition boundary.</summary>
/// <typeparam name="TRecord">The source record type.</typeparam>
public interface IGraphImportBatchHandler<TRecord>
{
    /// <summary>Validates or persists a batch of source records.</summary>
    /// <param name="batch">The ordered bounded source batch.</param>
    /// <param name="options">Current import options.</param>
    /// <param name="cancellationToken">Token that cancels processing.</param>
    /// <returns>The observable result of processing the batch.</returns>
    ValueTask<GraphImportBatchResult> HandleAsync(
        GraphImportBatch<TRecord> batch,
        GraphImportOptions options,
        CancellationToken cancellationToken = default);
}
