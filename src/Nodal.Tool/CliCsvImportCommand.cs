using System.Text.Json;
using System.Text.Json.Serialization;
using Nodal.Core.Mutations;
using Nodal.Import;
using Nodal.Import.Csv;

namespace Nodal.Tool;

internal static class CliCsvImportCommand
{
    private const int EvidenceFormatVersion = 1;

    public static async Task<int> RunAsync(
        CliArguments request,
        TextWriter output,
        ICliFileSystem fileSystem,
        IGraphMutationExecutor? executor,
        CancellationToken cancellationToken)
    {
        request.EnsureOnly(
            "--input",
            "--mapping",
            "--evidence",
            "--batch-size",
            "--max-operations",
            "--apply",
            "--approve-destructive");
        var inputPath = request.Require("--input");
        var mappingPath = request.Require("--mapping");
        var evidencePath = request.Require("--evidence");
        var batchSize = ParsePositiveInteger(request, "--batch-size", 500);
        var maxOperations = ParsePositiveInteger(request, "--max-operations", 5_000);
        var apply = ParseBoolean(request, "--apply", defaultValue: false);
        var approveDestructive = ParseBoolean(request, "--approve-destructive", defaultValue: false);

        var mappingJson = await fileSystem.ReadAllTextAsync(mappingPath, cancellationToken).ConfigureAwait(false);
        var definition = CsvGraphImportDefinitionSerializer.Deserialize(mappingJson);
        var mapping = CsvGraphImportDefinitionCompiler.Compile(definition);
        var validation = await RunPassAsync(
            fileSystem,
            inputPath,
            mapping,
            batchSize,
            maxOperations,
            executor: null,
            cancellationToken).ConfigureAwait(false);

        var outcome = "dryRun";
        var applied = false;
        var result = validation.Result;
        var appliedBatchCount = 0;
        var allAppliedBatchesAtomic = false;
        if (apply && result.Succeeded)
        {
            if (validation.HasDestructiveRisks && !approveDestructive)
            {
                result = result with
                {
                    Diagnostics =
                    [
                        .. result.Diagnostics,
                        new GraphImportDiagnostic(
                            null,
                            "ERROR-IMPORT-DESTRUCTIVE-APPROVAL-REQUIRED",
                            "Import apply requires explicit destructive-risk approval."),
                    ],
                };
                outcome = "approvalRequired";
            }
            else
            {
                executor ??= CliImportExecutionHostLoader.LoadFromEnvironment();
                var execution = await RunPassAsync(
                    fileSystem,
                    inputPath,
                    mapping,
                    batchSize,
                    maxOperations,
                    executor,
                    cancellationToken).ConfigureAwait(false);
                result = execution.Result;
                appliedBatchCount = execution.AppliedBatchCount;
                allAppliedBatchesAtomic = execution.AllAppliedBatchesAtomic;
                applied = result.Succeeded;
                outcome = applied ? "applied" : "applyFailed";
            }
        }
        else if (!result.Succeeded)
        {
            outcome = "invalid";
        }

        var evidence = new CsvImportEvidence(
            EvidenceFormatVersion,
            outcome,
            result.Succeeded,
            applied,
            result.ReadRecordCount,
            result.ImportedNodeCount,
            result.ImportedRelationCount,
            validation.BatchCount,
            appliedBatchCount,
            allAppliedBatchesAtomic,
            mapping.Decisions,
            result.Diagnostics,
            validation.Risks);
        await fileSystem.WriteAllTextAsync(
            evidencePath,
            string.Concat(CsvImportEvidenceSerializer.Serialize(evidence), Environment.NewLine),
            cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync(
            $"CSV import {outcome}: records={result.ReadRecordCount} nodes={result.ImportedNodeCount} " +
            $"relations={result.ImportedRelationCount} evidence-written=true").ConfigureAwait(false);
        return result.Succeeded ? NodalCli.Success : NodalCli.InvalidData;
    }

    private static async ValueTask<CsvImportPassResult> RunPassAsync(
        ICliFileSystem fileSystem,
        string inputPath,
        GraphImportMapping<CsvImportRecord> mapping,
        int batchSize,
        int maxOperations,
        IGraphMutationExecutor? executor,
        CancellationToken cancellationToken)
    {
        var handler = new CsvImportBatchHandler(mapping, maxOperations, executor);
        var runner = new GraphImportRunner<CsvImportRecord>(handler);
        using var reader = fileSystem.OpenText(inputPath);
        var result = await runner.RunAsync(
            CsvImportReader.ReadAsync(reader, cancellationToken),
            new GraphImportOptions(batchSize, GraphImportFailureMode.FailFast, ValidateOnly: executor is null),
            cancellationToken).ConfigureAwait(false);
        return new CsvImportPassResult(
            result,
            handler.BatchCount,
            handler.AppliedBatchCount,
            handler.AllAppliedBatchesAtomic,
            handler.Risks);
    }

    private static int ParsePositiveInteger(CliArguments request, string name, int defaultValue)
    {
        var value = request.Optional(name, defaultValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (!int.TryParse(
                value,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed) || parsed <= 0)
        {
            throw new CliUsageException($"Option '{name}' must be a positive integer.");
        }

        return parsed;
    }

    private static bool ParseBoolean(CliArguments request, string name, bool defaultValue)
    {
        var value = request.Optional(name, defaultValue.ToString().ToLowerInvariant());
        return value switch
        {
            "true" => true,
            "false" => false,
            _ => throw new CliUsageException($"Option '{name}' must be 'true' or 'false'."),
        };
    }

    private sealed class CsvImportBatchHandler(
        GraphImportMapping<CsvImportRecord> mapping,
        int maxOperations,
        IGraphMutationExecutor? executor) : IGraphImportBatchHandler<CsvImportRecord>
    {
        private readonly List<GraphImportRiskIndicator> risks = [];

        public int BatchCount { get; private set; }

        public int AppliedBatchCount { get; private set; }

        public bool AllAppliedBatchesAtomic { get; private set; } = true;

        public IReadOnlyList<GraphImportRiskIndicator> Risks => risks
            .GroupBy(risk => new { risk.Code, risk.Severity, risk.IsDestructive, risk.Message })
            .Select(group => new GraphImportRiskIndicator(
                group.Key.Code,
                group.Key.Severity,
                group.Key.IsDestructive,
                group.Sum(risk => risk.OccurrenceCount),
                group.Key.Message))
            .OrderBy(risk => risk.Code, StringComparer.Ordinal)
            .ToArray();

        public async ValueTask<GraphImportBatchResult> HandleAsync(
            GraphImportBatch<CsvImportRecord> batch,
            GraphImportOptions options,
            CancellationToken cancellationToken = default)
        {
            BatchCount++;
            GraphImportPlanResult planned;
            try
            {
                planned = new GraphImportPlanner<CsvImportRecord>().Plan(
                    batch,
                    mapping,
                    new GraphImportPlanningOptions(maxOperations));
            }
            catch (GraphImportPlanLimitExceededException)
            {
                return new GraphImportBatchResult(
                    0,
                    0,
                    [new GraphImportDiagnostic(
                        batch.FirstRecordNumber,
                        "ERROR-IMPORT-OPERATION-LIMIT",
                        $"The batch exceeds the configured limit of {maxOperations} unique mutation operations.")]);
            }
            risks.AddRange(planned.DryRun.Risks);
            if (!planned.DryRun.Succeeded || options.ValidateOnly)
            {
                return new GraphImportBatchResult(
                    planned.DryRun.PlannedNodeCount,
                    planned.DryRun.PlannedRelationCount,
                    planned.DryRun.Diagnostics);
            }

            if (executor is null)
            {
                throw new InvalidOperationException("Import apply requires a graph mutation executor.");
            }

            var applied = await executor.ExecuteAsync(planned.MutationPlan, cancellationToken).ConfigureAwait(false);
            AppliedBatchCount++;
            AllAppliedBatchesAtomic &= applied.IsAtomic;
            return new GraphImportBatchResult(
                applied.AffectedNodes,
                applied.AffectedRelations,
                planned.DryRun.Diagnostics);
        }
    }

    private sealed record CsvImportPassResult(
        GraphImportResult Result,
        int BatchCount,
        int AppliedBatchCount,
        bool AllAppliedBatchesAtomic,
        IReadOnlyList<GraphImportRiskIndicator> Risks)
    {
        public bool HasDestructiveRisks => Risks.Any(risk => risk.IsDestructive);
    }
}

internal sealed record CsvImportEvidence(
    int FormatVersion,
    string Outcome,
    bool Succeeded,
    bool Applied,
    long SourceRecordCount,
    int PlannedNodeCount,
    int PlannedRelationCount,
    int ValidatedBatchCount,
    int AppliedBatchCount,
    bool AllAppliedBatchesAtomic,
    IReadOnlyList<GraphImportMappingDecision> MappingDecisions,
    IReadOnlyList<GraphImportDiagnostic> Diagnostics,
    IReadOnlyList<GraphImportRiskIndicator> Risks);

internal static class CsvImportEvidenceSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Serialize(CsvImportEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        return JsonSerializer.Serialize(evidence, Options);
    }
}
