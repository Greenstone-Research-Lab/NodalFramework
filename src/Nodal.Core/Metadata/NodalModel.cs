namespace Nodal.Core.Metadata;

/// <summary>
/// Contains immutable, provider-neutral metadata for a graph domain model.
/// </summary>
public sealed class NodalModel
{
    private readonly Dictionary<Type, GraphNodeMetadata> nodes;
    private readonly Dictionary<(Type Source, Type Relation, Type Target), GraphRelationMetadata> relations;

    internal NodalModel(
        IEnumerable<GraphNodeMetadata> nodes,
        IEnumerable<GraphRelationMetadata> relations)
    {
        this.nodes = nodes.ToDictionary(node => node.ClrType);
        this.relations = relations.ToDictionary(
            relation => (relation.SourceType, relation.ClrType, relation.TargetType));
    }

    /// <summary>Gets the registered node mappings in deterministic CLR-name order.</summary>
    public IReadOnlyList<GraphNodeMetadata> Nodes => nodes.Values
        .OrderBy(node => node.Name, StringComparer.Ordinal)
        .ThenBy(node => node.ClrType.FullName, StringComparer.Ordinal)
        .ToArray();

    /// <summary>Gets the registered relationship mappings in deterministic name order.</summary>
    public IReadOnlyList<GraphRelationMetadata> Relations => relations.Values
        .OrderBy(relation => relation.Name, StringComparer.Ordinal)
        .ThenBy(relation => relation.SourceType.FullName, StringComparer.Ordinal)
        .ThenBy(relation => relation.TargetType.FullName, StringComparer.Ordinal)
        .ToArray();

    /// <summary>Gets the metadata registered for a node type.</summary>
    public GraphNodeMetadata GetNode<TNode>() => GetNode(typeof(TNode));

    /// <summary>Gets the metadata registered for a CLR node type.</summary>
    public GraphNodeMetadata GetNode(Type nodeType)
    {
        ArgumentNullException.ThrowIfNull(nodeType);
        return nodes.TryGetValue(nodeType, out var metadata)
            ? metadata
            : throw new InvalidOperationException($"Node type '{nodeType}' is not part of the Nodal model.");
    }

    /// <summary>Gets a strongly typed relationship mapping.</summary>
    public GraphRelationMetadata GetRelation<TSource, TRelation, TTarget>() =>
        relations.TryGetValue((typeof(TSource), typeof(TRelation), typeof(TTarget)), out var metadata)
            ? metadata
            : throw new InvalidOperationException(
                $"Relationship '{typeof(TSource).Name}-{typeof(TRelation).Name}->{typeof(TTarget).Name}' is not part of the Nodal model.");
}

/// <summary>Describes the provider-neutral mapping of a graph node.</summary>
/// <param name="ClrType">The domain CLR type.</param>
/// <param name="Name">The graph node type or label.</param>
/// <param name="KeyProperty">The CLR property used as the graph identifier.</param>
/// <param name="Properties">The persisted property mappings keyed by CLR property name.</param>
public sealed record GraphNodeMetadata(
    Type ClrType,
    string Name,
    string KeyProperty,
    IReadOnlyDictionary<string, GraphPropertyMetadata> Properties);

/// <summary>Describes a strongly typed graph relationship mapping.</summary>
/// <param name="ClrType">The relationship POCO type.</param>
/// <param name="Name">The graph relationship type.</param>
/// <param name="SourceType">The source node CLR type.</param>
/// <param name="TargetType">The target node CLR type.</param>
/// <param name="Directed">Whether the relationship is directed.</param>
/// <param name="Properties">The persisted relationship property mappings.</param>
public sealed record GraphRelationMetadata(
    Type ClrType,
    string Name,
    Type SourceType,
    Type TargetType,
    bool Directed,
    IReadOnlyDictionary<string, GraphPropertyMetadata> Properties);

/// <summary>Maps a CLR property to its provider-neutral graph name.</summary>
/// <param name="ClrName">The CLR property name.</param>
/// <param name="Name">The graph property name.</param>
/// <param name="ClrType">The property CLR type.</param>
public sealed record GraphPropertyMetadata(string ClrName, string Name, Type ClrType);
