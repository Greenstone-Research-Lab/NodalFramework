namespace Nodal.Analytics.Observations;

/// <summary>Identifies one node within an observation.</summary>
public sealed record GraphObservationNodeIdentity
{
    /// <summary>Initializes a valid observation node identity.</summary>
    /// <param name="type">The provider-neutral node type.</param>
    /// <param name="key">The canonical provider identity.</param>
    public GraphObservationNodeIdentity(string type, GraphObservationKey key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(key);

        Type = type;
        Key = key;
    }

    /// <summary>Gets the provider-neutral node type.</summary>
    public string Type { get; }

    /// <summary>Gets the canonical provider identity.</summary>
    public GraphObservationKey Key { get; }
}

/// <summary>Represents one immutable node in a canonical observation.</summary>
public sealed class GraphObservationNode
{
    internal GraphObservationNode(
        GraphObservationNodeIdentity identity,
        IReadOnlyDictionary<string, object?> properties)
    {
        Identity = identity;
        Properties = properties;
    }

    /// <summary>Gets the node identity.</summary>
    public GraphObservationNodeIdentity Identity { get; }

    /// <summary>Gets the explicitly projected, immutable node properties.</summary>
    public IReadOnlyDictionary<string, object?> Properties { get; }
}

/// <summary>Represents one immutable, directed relationship in a canonical observation.</summary>
public sealed class GraphObservationRelation
{
    internal GraphObservationRelation(
        string type,
        GraphObservationKey key,
        GraphObservationNodeIdentity source,
        GraphObservationNodeIdentity target,
        IReadOnlyDictionary<string, object?> properties)
    {
        Type = type;
        Key = key;
        Source = source;
        Target = target;
        Properties = properties;
    }

    /// <summary>Gets the provider-neutral relationship type.</summary>
    public string Type { get; }

    /// <summary>Gets the canonical relationship identity.</summary>
    public GraphObservationKey Key { get; }

    /// <summary>Gets the source node identity.</summary>
    public GraphObservationNodeIdentity Source { get; }

    /// <summary>Gets the target node identity.</summary>
    public GraphObservationNodeIdentity Target { get; }

    /// <summary>Gets the explicitly projected, immutable relationship properties.</summary>
    public IReadOnlyDictionary<string, object?> Properties { get; }
}

/// <summary>
/// Contains one bounded, provider-neutral and immutable graph snapshot for analytics.
/// </summary>
public sealed class GraphObservation
{
    internal GraphObservation(
        IReadOnlyList<GraphObservationNode> nodes,
        IReadOnlyList<GraphObservationRelation> relations)
    {
        Nodes = nodes;
        Relations = relations;
    }

    /// <summary>Gets nodes in provider result order.</summary>
    public IReadOnlyList<GraphObservationNode> Nodes { get; }

    /// <summary>Gets directed relationships in provider result order.</summary>
    public IReadOnlyList<GraphObservationRelation> Relations { get; }
}
