using System.Collections.Frozen;
using System.Collections.ObjectModel;
using Nodal.Core.Execution;

namespace Nodal.Analytics.Observations;

/// <summary>Materializes normalized provider results as bounded canonical observations.</summary>
public static class GraphObservationMaterializer
{
    /// <summary>Creates an immutable observation from an already normalized provider result.</summary>
    /// <param name="result">The provider-neutral query result.</param>
    /// <param name="options">Bounds and explicit property projections, or defaults when omitted.</param>
    /// <returns>A self-contained immutable observation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A configured limit is not positive.</exception>
    /// <exception cref="ArgumentException">An identity, type, projection, or endpoint is invalid.</exception>
    /// <exception cref="GraphObservationLimitExceededException">The normalized result exceeds a bound.</exception>
    public static GraphObservation Materialize(
        GraphQueryResult result,
        GraphObservationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        options ??= new GraphObservationOptions();
        Validate(options);

        var relations = result.RelationRecords;
        EnsureWithinLimit("node", result.Nodes.Count, options.MaxNodes);
        EnsureWithinLimit("relationship", relations.Count, options.MaxRelations);

        var nodeProjection = CopyProjection(options.NodeProperties, nameof(options.NodeProperties));
        var relationProjection = CopyProjection(options.RelationProperties, nameof(options.RelationProperties));
        var nodes = MaterializeNodes(result.Nodes, nodeProjection, options);
        var nodesByKey = nodes.GroupBy(node => node.Identity.Key).ToFrozenDictionary(group => group.Key);
        var materializedRelations = MaterializeRelations(relations, relationProjection, nodesByKey, options);

        return new GraphObservation(
            Array.AsReadOnly(nodes),
            Array.AsReadOnly(materializedRelations));
    }

    private static GraphObservationNode[] MaterializeNodes(
        IReadOnlyList<GraphNodeRecord> records,
        IReadOnlySet<string> projection,
        GraphObservationOptions options)
    {
        var nodes = new GraphObservationNode[records.Count];
        var identities = new HashSet<GraphObservationNodeIdentity>();

        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index] ?? throw new ArgumentException("A normalized node cannot be null.", nameof(records));
            var type = RequireName(record.Type, "Node type");
            var identity = new GraphObservationNodeIdentity(type, GraphObservationKey.From(record.Id));
            if (!identities.Add(identity))
            {
                throw new ArgumentException($"Duplicate node identity '{identity.Type}/{identity.Key}'.", nameof(records));
            }

            nodes[index] = new GraphObservationNode(
                identity,
                ObservationValueFreezer.Project(
                    record.Properties,
                    projection,
                    options.MaxPropertyCollectionItems,
                    options.MaxPropertyDepth));
        }

        return nodes;
    }

    private static GraphObservationRelation[] MaterializeRelations(
        IReadOnlyList<GraphRelationRecord> records,
        IReadOnlySet<string> projection,
        IReadOnlyDictionary<GraphObservationKey, IGrouping<GraphObservationKey, GraphObservationNode>> nodesByKey,
        GraphObservationOptions options)
    {
        var relations = new GraphObservationRelation[records.Count];
        var identities = new HashSet<(string Type, GraphObservationKey Key)>();

        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index] ?? throw new ArgumentException("A normalized relationship cannot be null.", nameof(records));
            var type = RequireName(record.Type, "Relationship type");
            var key = GraphObservationKey.From(record.Id);
            if (!identities.Add((type, key)))
            {
                throw new ArgumentException($"Duplicate relationship identity '{type}/{key}'.", nameof(records));
            }

            relations[index] = new GraphObservationRelation(
                type,
                key,
                ResolveEndpoint(record.SourceId, "source", nodesByKey),
                ResolveEndpoint(record.TargetId, "target", nodesByKey),
                ObservationValueFreezer.Project(
                    record.Properties,
                    projection,
                    options.MaxPropertyCollectionItems,
                    options.MaxPropertyDepth));
        }

        return relations;
    }

    private static GraphObservationNodeIdentity ResolveEndpoint(
        object endpoint,
        string endpointName,
        IReadOnlyDictionary<GraphObservationKey, IGrouping<GraphObservationKey, GraphObservationNode>> nodesByKey)
    {
        var key = GraphObservationKey.From(endpoint);
        if (!nodesByKey.TryGetValue(key, out var matches))
        {
            throw new ArgumentException($"Relationship {endpointName} '{key}' does not exist in the observation.");
        }

        using var enumerator = matches.GetEnumerator();
        _ = enumerator.MoveNext();
        var match = enumerator.Current;
        if (enumerator.MoveNext())
        {
            throw new ArgumentException($"Relationship {endpointName} '{key}' is ambiguous across node types.");
        }

        return match.Identity;
    }

    private static FrozenSet<string> CopyProjection(IReadOnlySet<string> projection, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(projection, parameterName);
        var copy = new HashSet<string>(StringComparer.Ordinal);
        foreach (var propertyName in projection)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                throw new ArgumentException("Projected property names cannot be empty.", parameterName);
            }

            copy.Add(propertyName);
        }

        return copy.ToFrozenSet(StringComparer.Ordinal);
    }

    private static void Validate(GraphObservationOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxNodes, nameof(options.MaxNodes));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxRelations, nameof(options.MaxRelations));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            options.MaxPropertyCollectionItems,
            nameof(options.MaxPropertyCollectionItems));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxPropertyDepth, nameof(options.MaxPropertyDepth));
    }

    private static string RequireName(string value, string description)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{description} cannot be empty.");
        }

        return value;
    }

    private static void EnsureWithinLimit(string elementKind, int actualCount, int maximumCount)
    {
        if (actualCount > maximumCount)
        {
            throw new GraphObservationLimitExceededException(elementKind, actualCount, maximumCount);
        }
    }
}
