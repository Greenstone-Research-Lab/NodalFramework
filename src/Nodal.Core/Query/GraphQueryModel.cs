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
public sealed record GraphQueryModel(
    string NodeType,
    string Alias,
    GraphPredicate? Predicate,
    IReadOnlyList<GraphQueryParameter> Parameters,
    int? Limit,
    IReadOnlyList<GraphTraversalStep> Traversals,
    GraphQueryProjection Projection = GraphQueryProjection.Node)
{
    /// <summary>Gets the alias returned by the provider.</summary>
    public string ResultAlias => Traversals.Count == 0 ? Alias : Traversals[^1].TargetAlias;

    /// <summary>Gets the provider-neutral node type returned by the query.</summary>
    public string ResultNodeType => Traversals.Count == 0 ? NodeType : Traversals[^1].TargetNodeType;
}

/// <summary>Defines the canonical result shape requested from a provider.</summary>
public enum GraphQueryProjection
{
    /// <summary>Returns the node reached by the query.</summary>
    Node,

    /// <summary>Returns the source node, final relationship, and reached node as a path.</summary>
    Path,
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
public sealed record GraphTraversalStep(
    string RelationType,
    string TargetNodeType,
    string SourceAlias,
    string RelationAlias,
    string TargetAlias,
    GraphTraversalDirection Direction,
    GraphPredicate? Predicate,
    GraphPredicate? RelationPredicate = null);

/// <summary>
/// Represents a safely parameterized query value.
/// </summary>
/// <param name="Name">The generated parameter name.</param>
/// <param name="Value">The runtime value.</param>
/// <param name="ClrType">The declared CLR type.</param>
public sealed record GraphQueryParameter(string Name, object? Value, Type ClrType);
