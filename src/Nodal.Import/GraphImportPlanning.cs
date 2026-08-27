using Nodal.Core.ChangeTracking;
using Nodal.Core.Mutations;

namespace Nodal.Import;

/// <summary>Classifies the operational impact of a dry-run risk.</summary>
public enum GraphImportRiskSeverity
{
    /// <summary>The condition deserves review but does not invalidate the plan.</summary>
    Warning,

    /// <summary>The condition can omit or replace intended graph data.</summary>
    Critical,
}

/// <summary>Reports a potential data-impacting condition discovered during planning.</summary>
/// <param name="Code">Stable, machine-readable risk code.</param>
/// <param name="Severity">The operational severity.</param>
/// <param name="IsDestructive">Whether applying the plan can replace or omit intended data.</param>
/// <param name="OccurrenceCount">Number of affected mappings or records.</param>
/// <param name="Message">Payload-safe review guidance.</param>
public sealed record GraphImportRiskIndicator(
    string Code,
    GraphImportRiskSeverity Severity,
    bool IsDestructive,
    long OccurrenceCount,
    string Message);

/// <summary>Configures bounded construction of one provider-neutral mutation plan.</summary>
/// <param name="MaxOperations">Maximum unique node and relation operations allowed in one plan.</param>
public sealed record GraphImportPlanningOptions(int MaxOperations = 5_000)
{
    /// <summary>Validates the planning limit.</summary>
    public void Validate()
    {
        if (MaxOperations <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxOperations), "The maximum operation count must be greater than zero.");
        }
    }
}

/// <summary>Reports the complete, payload-safe result of planning an import batch.</summary>
/// <param name="SourceRecordCount">Number of source records evaluated.</param>
/// <param name="PlannedNodeCount">Number of unique node upserts planned.</param>
/// <param name="PlannedRelationCount">Number of unique relation upserts planned.</param>
/// <param name="MappingDecisions">Explicit mapping decisions used by the planner.</param>
/// <param name="Diagnostics">Record-addressed validation diagnostics.</param>
/// <param name="Risks">Aggregated data-impacting risk indicators.</param>
public sealed record GraphImportDryRunReport(
    long SourceRecordCount,
    int PlannedNodeCount,
    int PlannedRelationCount,
    IReadOnlyList<GraphImportMappingDecision> MappingDecisions,
    IReadOnlyList<GraphImportDiagnostic> Diagnostics,
    IReadOnlyList<GraphImportRiskIndicator> Risks)
{
    /// <summary>Gets whether the report contains no error diagnostics.</summary>
    public bool Succeeded => Diagnostics.All(diagnostic =>
        !diagnostic.Code.StartsWith("ERROR", StringComparison.Ordinal));

    /// <summary>Gets whether applying the plan has at least one destructive-risk indicator.</summary>
    public bool HasDestructiveRisks => Risks.Any(risk => risk.IsDestructive);
}

/// <summary>Pairs an executable provider-neutral mutation plan with its dry-run evidence.</summary>
/// <param name="MutationPlan">The bounded mutation plan accepted by Nodal mutation providers.</param>
/// <param name="DryRun">The reviewable planning evidence.</param>
public sealed record GraphImportPlanResult(
    GraphMutationPlan MutationPlan,
    GraphImportDryRunReport DryRun);

/// <summary>Thrown when an import batch would exceed its configured mutation-plan boundary.</summary>
public sealed class GraphImportPlanLimitExceededException : InvalidOperationException
{
    /// <summary>Initializes the exception with the configured maximum operation count.</summary>
    /// <param name="maxOperations">The maximum operation count accepted by the planner.</param>
    public GraphImportPlanLimitExceededException(int maxOperations)
        : base($"The import batch exceeds the configured limit of {maxOperations} unique mutation operations.") =>
        MaxOperations = maxOperations;

    /// <summary>Gets the configured maximum operation count.</summary>
    public int MaxOperations { get; }
}

/// <summary>Compiles one bounded source batch into provider-neutral node and relation upserts.</summary>
/// <typeparam name="TRecord">The source record type.</typeparam>
public sealed class GraphImportPlanner<TRecord>
{
    /// <summary>Builds a dependency-safe mutation plan and its dry-run report.</summary>
    /// <param name="batch">The bounded source batch.</param>
    /// <param name="mapping">The explicit node and relation mapping.</param>
    /// <param name="options">Optional operation-count boundary.</param>
    /// <returns>The mutation plan and review evidence. The method performs no database I/O.</returns>
    public GraphImportPlanResult Plan(
        GraphImportBatch<TRecord> batch,
        GraphImportMapping<TRecord> mapping,
        GraphImportPlanningOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(batch.Records);
        if (batch.FirstRecordNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batch), "The first source record number must be greater than zero.");
        }
        options ??= new GraphImportPlanningOptions();
        options.Validate();

        var diagnostics = new List<GraphImportDiagnostic>();
        var nodes = new Dictionary<GraphImportNodeIdentity, CreateNodeOperation>();
        var relations = new Dictionary<GraphImportRelationIdentity, CreateRelationOperation>();
        long duplicateNodes = 0;
        long duplicateRelations = 0;
        long omittedMappings = 0;

        for (var index = 0; index < batch.Records.Count; index++)
        {
            var record = batch.Records[index];
            var recordNumber = batch.FirstRecordNumber + index;
            var identities = PlanNodes(record, recordNumber, mapping.Nodes, nodes, diagnostics, ref duplicateNodes, ref omittedMappings);
            PlanRelations(record, recordNumber, mapping.Relations, identities, relations, diagnostics, ref duplicateRelations, ref omittedMappings);
            ThrowIfLimitExceeded(nodes.Count + relations.Count, options.MaxOperations);
        }

        var operations = nodes.Values.Cast<GraphMutationOperation>()
            .Concat(relations.Values)
            .ToArray();
        var risks = BuildRisks(nodes.Values, relations.Values, duplicateNodes, duplicateRelations, omittedMappings);
        var report = new GraphImportDryRunReport(
            batch.Records.Count,
            nodes.Count,
            relations.Count,
            mapping.Decisions,
            diagnostics,
            risks);
        return new GraphImportPlanResult(new GraphMutationPlan(operations), report);
    }

    private static Dictionary<string, GraphIdentity> PlanNodes(
        TRecord record,
        long recordNumber,
        IReadOnlyList<GraphImportNodeMapping<TRecord>> mappings,
        Dictionary<GraphImportNodeIdentity, CreateNodeOperation> nodes,
        List<GraphImportDiagnostic> diagnostics,
        ref long duplicateNodes,
        ref long omittedMappings)
    {
        var identities = new Dictionary<string, GraphIdentity>(StringComparer.Ordinal);
        foreach (var mapping in mappings)
        {
            var key = mapping.KeySelector(record);
            if (IsMissingKey(key))
            {
                omittedMappings++;
                diagnostics.Add(new GraphImportDiagnostic(
                    recordNumber,
                    "ERROR-IMPORT-MISSING-NODE-KEY",
                    $"Node mapping '{mapping.Name}' did not produce a stable identity."));
                continue;
            }

            var identity = new GraphIdentity(mapping.ClrType, mapping.NodeType, mapping.KeyProperty, key!);
            identities.Add(mapping.Name, identity);
            var operation = new CreateNodeOperation(identity, SelectProperties(record, mapping.Properties));
            var importIdentity = new GraphImportNodeIdentity(mapping.NodeType, mapping.KeyProperty, key!);
            if (nodes.ContainsKey(importIdentity))
            {
                duplicateNodes++;
                diagnostics.Add(new GraphImportDiagnostic(
                    recordNumber,
                    "WARNING-IMPORT-DUPLICATE-NODE",
                    $"Node mapping '{mapping.Name}' produced a duplicate identity; the later record wins within this batch."));
            }
            nodes[importIdentity] = operation;
        }
        return identities;
    }

    private static void PlanRelations(
        TRecord record,
        long recordNumber,
        IReadOnlyList<GraphImportRelationMapping<TRecord>> mappings,
        Dictionary<string, GraphIdentity> identities,
        Dictionary<GraphImportRelationIdentity, CreateRelationOperation> relations,
        List<GraphImportDiagnostic> diagnostics,
        ref long duplicateRelations,
        ref long omittedMappings)
    {
        foreach (var mapping in mappings)
        {
            if (!identities.TryGetValue(mapping.SourceMappingName, out var source) ||
                !identities.TryGetValue(mapping.TargetMappingName, out var target))
            {
                omittedMappings++;
                diagnostics.Add(new GraphImportDiagnostic(
                    recordNumber,
                    "ERROR-IMPORT-MISSING-RELATION-ENDPOINT",
                    $"Relation mapping '{mapping.Name}' was omitted because an endpoint identity is unavailable."));
                continue;
            }

            var identity = new GraphImportRelationIdentity(source, mapping.RelationType, target, mapping.Directed);
            var operation = new CreateRelationOperation(
                source,
                mapping.RelationType,
                target,
                mapping.Directed,
                SelectProperties(record, mapping.Properties));
            if (relations.ContainsKey(identity))
            {
                duplicateRelations++;
                diagnostics.Add(new GraphImportDiagnostic(
                    recordNumber,
                    "WARNING-IMPORT-DUPLICATE-RELATION",
                    $"Relation mapping '{mapping.Name}' produced a duplicate relation; the later record wins within this batch."));
            }
            relations[identity] = operation;
        }
    }

    private static Dictionary<string, object?> SelectProperties(
        TRecord record,
        IReadOnlyList<GraphImportPropertyMapping<TRecord>> mappings) =>
        mappings.ToDictionary(mapping => mapping.Name, mapping => mapping.ValueSelector(record), StringComparer.Ordinal);

    private static bool IsMissingKey(object? key) =>
        key is null || key is string text && string.IsNullOrWhiteSpace(text);

    private static void ThrowIfLimitExceeded(int operationCount, int maxOperations)
    {
        if (operationCount > maxOperations)
        {
            throw new GraphImportPlanLimitExceededException(maxOperations);
        }
    }

    private static List<GraphImportRiskIndicator> BuildRisks(
        IEnumerable<CreateNodeOperation> nodes,
        IEnumerable<CreateRelationOperation> relations,
        long duplicateNodes,
        long duplicateRelations,
        long omittedMappings)
    {
        var risks = new List<GraphImportRiskIndicator>();
        var propertyUpserts = nodes.Count(node => node.Properties.Count > 0) +
            relations.Count(relation => relation.Properties.Count > 0);
        AddRisk(
            risks,
            propertyUpserts,
            "NODAL-IMPORT-PROPERTY-OVERWRITE",
            GraphImportRiskSeverity.Warning,
            true,
            "Upsert operations can replace mapped properties on existing graph elements.");
        AddRisk(
            risks,
            duplicateNodes,
            "NODAL-IMPORT-DUPLICATE-NODE",
            GraphImportRiskSeverity.Warning,
            true,
            "Duplicate node identities were coalesced using deterministic last-record-wins semantics.");
        AddRisk(
            risks,
            duplicateRelations,
            "NODAL-IMPORT-DUPLICATE-RELATION",
            GraphImportRiskSeverity.Warning,
            true,
            "Duplicate relations were coalesced using deterministic last-record-wins semantics.");
        AddRisk(
            risks,
            omittedMappings,
            "NODAL-IMPORT-OMITTED-MAPPING",
            GraphImportRiskSeverity.Critical,
            true,
            "One or more intended graph elements were omitted because a stable identity was unavailable.");
        return risks;
    }

    private static void AddRisk(
        List<GraphImportRiskIndicator> risks,
        long count,
        string code,
        GraphImportRiskSeverity severity,
        bool isDestructive,
        string message)
    {
        if (count > 0)
        {
            risks.Add(new GraphImportRiskIndicator(code, severity, isDestructive, count, message));
        }
    }

    private sealed record GraphImportRelationIdentity(
        GraphIdentity Source,
        string RelationType,
        GraphIdentity Target,
        bool Directed);

    private sealed record GraphImportNodeIdentity(
        string NodeType,
        string KeyProperty,
        object Value);
}
