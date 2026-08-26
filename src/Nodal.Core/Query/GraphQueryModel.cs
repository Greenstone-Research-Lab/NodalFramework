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
/// <param name="ExistencePatterns">Correlated relationship patterns that must exist or not exist for a result node.</param>
/// <param name="MatchPatterns">Additional required relationship patterns bound to aliases in the same query.</param>
/// <param name="RowProjection">The provider-side scalar and aggregate columns requested from the query.</param>
/// <param name="SetOperation">The optional set operation that supplies this query's result nodes.</param>
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
    GraphCycleBehavior CycleBehavior = GraphCycleBehavior.ProviderDefault,
    IReadOnlyList<GraphExistencePattern>? ExistencePatterns = null,
    IReadOnlyList<GraphTraversalStep>? MatchPatterns = null,
    GraphRowProjection? RowProjection = null,
    GraphSetOperation? SetOperation = null)
{
    /// <summary>Gets the normalized ordering clauses.</summary>
    public IReadOnlyList<GraphOrdering> EffectiveOrderings => Orderings ?? [];
    /// <summary>Gets the alias returned by the provider.</summary>
    public string ResultAlias => Traversals.Count == 0 ? Alias : Traversals[^1].TargetAlias;

    /// <summary>Gets the provider-neutral node type returned by the query.</summary>
    public string ResultNodeType => Traversals.Count == 0 ? NodeType : Traversals[^1].TargetNodeType;

    /// <summary>Gets the correlated existence patterns, or an empty collection when none were requested.</summary>
    public IReadOnlyList<GraphExistencePattern> EffectiveExistencePatterns => ExistencePatterns ?? [];

    /// <summary>Gets additional required patterns, or an empty collection when none were requested.</summary>
    public IReadOnlyList<GraphTraversalStep> EffectiveMatchPatterns => MatchPatterns ?? [];
}

/// <summary>
/// Describes one correlated relationship pattern used as an exists or not-exists predicate.
/// </summary>
/// <param name="RelationType">The provider-neutral relationship name.</param>
/// <param name="TargetNodeType">The node type reached by the relationship.</param>
/// <param name="SourceAlias">The outer query alias correlated with this pattern.</param>
/// <param name="RelationAlias">The stable relationship alias inside the correlated pattern.</param>
/// <param name="TargetAlias">The stable target-node alias inside the correlated pattern.</param>
/// <param name="Direction">The requested relationship direction.</param>
/// <param name="TargetPredicate">An optional predicate applied to the correlated target node.</param>
/// <param name="RelationPredicate">An optional predicate applied to the correlated relationship payload.</param>
/// <param name="Negated">Whether the pattern must be absent rather than present.</param>
public sealed record GraphExistencePattern(
    string RelationType,
    string TargetNodeType,
    string SourceAlias,
    string RelationAlias,
    string TargetAlias,
    GraphTraversalDirection Direction,
    GraphPredicate? TargetPredicate,
    GraphPredicate? RelationPredicate,
    bool Negated = false);

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

    /// <summary>Returns named scalar and aggregate values without materializing source nodes.</summary>
    Row,
}

/// <summary>Defines the value produced by one provider-side result-row column.</summary>
public enum GraphRowColumnKind
{
    /// <summary>Returns a mapped property from a bound node.</summary>
    Property,

    /// <summary>Counts bound values.</summary>
    Count,

    /// <summary>Sums numeric bound-property values.</summary>
    Sum,

    /// <summary>Averages numeric bound-property values.</summary>
    Average,

    /// <summary>Returns the minimum bound-property value.</summary>
    Minimum,

    /// <summary>Returns the maximum bound-property value.</summary>
    Maximum,
}

/// <summary>Describes one named scalar or aggregate value in a result-row projection.</summary>
/// <param name="Name">The stable result-column name.</param>
/// <param name="Kind">The scalar or aggregate operation.</param>
/// <param name="SourceAlias">The alias whose value or property is read.</param>
/// <param name="PropertyName">The mapped property name for property-based columns.</param>
/// <param name="Distinct">Whether duplicate values are removed before a count operation.</param>
public sealed record GraphRowColumn(
    string Name,
    GraphRowColumnKind Kind,
    string SourceAlias,
    string? PropertyName = null,
    bool Distinct = false);

/// <summary>Describes the named columns returned from a provider-side row projection.</summary>
/// <param name="Columns">The ordered projected columns.</param>
/// <param name="Orderings">The optional ordering applied to projected row columns.</param>
/// <param name="HavingPredicates">The optional aggregate-stage predicates applied to projected row columns.</param>
public sealed record GraphRowProjection(
    IReadOnlyList<GraphRowColumn> Columns,
    IReadOnlyList<GraphRowOrdering>? Orderings = null,
    IReadOnlyList<GraphRowPredicate>? HavingPredicates = null)
{
    /// <summary>Gets the normalized projected-row ordering clauses.</summary>
    public IReadOnlyList<GraphRowOrdering> EffectiveOrderings => Orderings ?? [];

    /// <summary>Gets the normalized aggregate-stage predicates.</summary>
    public IReadOnlyList<GraphRowPredicate> EffectiveHavingPredicates => HavingPredicates ?? [];
}

/// <summary>Describes ordering over one projected row column.</summary>
/// <param name="ColumnName">The named projected column to order.</param>
/// <param name="Direction">The requested ordering direction.</param>
public sealed record GraphRowOrdering(string ColumnName, GraphSortDirection Direction);

/// <summary>Describes one parameterized predicate over a projected row column.</summary>
/// <param name="ColumnName">The named projected column to filter.</param>
/// <param name="Operator">The comparison operation.</param>
/// <param name="ParameterName">The generated query parameter containing the comparison value.</param>
public sealed record GraphRowPredicate(
    string ColumnName,
    GraphComparisonOperator Operator,
    string ParameterName);

/// <summary>Defines portable set combination operations over compatible graph-node queries.</summary>
public enum GraphSetOperationKind
{
    /// <summary>Combines results and removes duplicates.</summary>
    Union,

    /// <summary>Combines results while preserving duplicates.</summary>
    UnionAll,
}

/// <summary>Describes the compatible node-query operands of one portable set operation.</summary>
/// <param name="Kind">The requested combination operation.</param>
/// <param name="Left">The first node-query operand.</param>
/// <param name="Right">The second node-query operand.</param>
public sealed record GraphSetOperation(GraphSetOperationKind Kind, GraphQueryModel Left, GraphQueryModel Right);

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
