namespace Nodal.Core.Query;

/// <summary>
/// Describes a graph query independently of any database language or transport.
/// </summary>
/// <param name="NodeType">The graph node type or label.</param>
/// <param name="Alias">The stable alias used by provider compilers.</param>
/// <param name="Predicate">The optional filter predicate.</param>
/// <param name="Parameters">The parameter values referenced by the predicate.</param>
/// <param name="Limit">The optional maximum number of results.</param>
/// <param name="Traversals">The ordered relationship traversal steps.</param>
/// <param name="Projection">The canonical result shape requested from the provider.</param>
/// <param name="Offset">The optional number of ordered results to skip.</param>
/// <param name="Orderings">The stable ordering clauses.</param>
/// <param name="Distinct">Whether duplicate results are removed.</param>
/// <param name="TrackingBehavior">Whether materialized objects are tracked.</param>
/// <param name="CycleBehavior">How repeated vertices in a matched path are handled.</param>
public sealed record GraphQueryModel(
    string NodeType,
    string Alias,
    GraphPredicate? Predicate,
    IReadOnlyList<GraphQueryParameter> Parameters,
    int? Limit,
    IReadOnlyList<GraphTraversalStep> Traversals,
    GraphQueryProjection Projection = GraphQueryProjection.Node,
    int? Offset = null,
    IReadOnlyList<GraphOrdering>? Orderings = null,
    bool Distinct = false,
    GraphTrackingBehavior TrackingBehavior = GraphTrackingBehavior.TrackAll,
    GraphCycleBehavior CycleBehavior = GraphCycleBehavior.ProviderDefault)
{
    /// <summary>Gets the normalized ordering clauses.</summary>
    public IReadOnlyList<GraphOrdering> EffectiveOrderings => Orderings ?? [];
    /// <summary>Gets the alias returned by the provider.</summary>
    public string ResultAlias => Traversals.Count == 0 ? Alias : Traversals[^1].TargetAlias;

    /// <summary>Gets the provider-neutral node type returned by the query.</summary>
    public string ResultNodeType => Traversals.Count == 0 ? NodeType : Traversals[^1].TargetNodeType;
}

/// <summary>Defines cycle handling for a graph path match.</summary>
public enum GraphCycleBehavior
{
    /// <summary>Uses the database engine's native path semantics.</summary>
    ProviderDefault,

    /// <summary>Requires every vertex in the matched path to be unique.</summary>
    SimplePath,
}

/// <summary>Defines whether materialized graph objects are registered with the context.</summary>
public enum GraphTrackingBehavior
{
    /// <summary>Uses identity resolution and change tracking.</summary>
    TrackAll,

    /// <summary>Returns detached objects without populating the change tracker.</summary>
    NoTracking,
}

/// <summary>Defines the direction of a provider-neutral ordering clause.</summary>
public enum GraphSortDirection
{
    /// <summary>Orders values from lowest to highest.</summary>
    Ascending,

    /// <summary>Orders values from highest to lowest.</summary>
    Descending,
}

/// <summary>Describes an ordering applied to a query result property.</summary>
public sealed record GraphOrdering(string PropertyName, string Alias, GraphSortDirection Direction);

/// <summary>Defines the canonical result shape requested from a provider.</summary>
public enum GraphQueryProjection
{
    /// <summary>Returns the node reached by the query.</summary>
    Node,

    /// <summary>Returns the source node, final relationship, and reached node as a path.</summary>
    Path,

    /// <summary>Returns every bound node and relationship as a normalized subgraph.</summary>
    Subgraph,

    /// <summary>Returns the number of matched result nodes.</summary>
    Count,
}

/// <summary>Describes the direction of one strongly typed relationship traversal.</summary>
public enum GraphTraversalDirection
{
    /// <summary>Traverses from the declared relationship source to its target.</summary>
    Outgoing,

    /// <summary>Traverses from the declared relationship target back to its source.</summary>
    Incoming,

    /// <summary>Traverses without applying relationship direction.</summary>
    Undirected,
}

/// <summary>
/// Describes one provider-neutral hop from the current node to another node.
/// </summary>
/// <param name="RelationType">The provider-neutral relationship name.</param>
/// <param name="TargetNodeType">The provider-neutral node type reached by the hop.</param>
/// <param name="SourceAlias">The alias at which the hop begins.</param>
/// <param name="RelationAlias">The stable relationship alias.</param>
/// <param name="TargetAlias">The alias reached by the hop.</param>
/// <param name="Direction">The requested traversal direction.</param>
/// <param name="Predicate">The optional predicate applied to the reached node.</param>
/// <param name="RelationPredicate">The optional predicate applied to the relationship.</param>
/// <param name="MinDepth">The inclusive minimum number of repeated hops.</param>
/// <param name="MaxDepth">The inclusive maximum number of repeated hops.</param>
/// <param name="Optional">Whether absence of this traversal preserves the preceding match.</param>
/// <param name="AlternativeRelationTypes">Additional relationship types accepted by the hop.</param>
public sealed record GraphTraversalStep(
    string RelationType,
    string TargetNodeType,
    string SourceAlias,
    string RelationAlias,
    string TargetAlias,
    GraphTraversalDirection Direction,
    GraphPredicate? Predicate,
    GraphPredicate? RelationPredicate = null,
    int MinDepth = 1,
    int MaxDepth = 1,
    bool Optional = false,
    IReadOnlyList<string>? AlternativeRelationTypes = null)
{
    /// <summary>Gets every relationship type accepted by the traversal.</summary>
    public IReadOnlyList<string> RelationTypes => AlternativeRelationTypes is null
        ? [RelationType]
        : [RelationType, .. AlternativeRelationTypes];
}

/// <summary>
/// Represents a safely parameterized query value.
/// </summary>
/// <param name="Name">The generated parameter name.</param>
/// <param name="Value">The runtime value.</param>
/// <param name="ClrType">The declared CLR type.</param>
public sealed record GraphQueryParameter(string Name, object? Value, Type ClrType);
