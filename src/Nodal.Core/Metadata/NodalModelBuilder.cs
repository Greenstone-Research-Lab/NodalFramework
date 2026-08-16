using System.Linq.Expressions;
using System.Reflection;
using Nodal.Core.Query;

namespace Nodal.Core.Metadata;

/// <summary>
/// Builds provider-neutral mappings between CLR domain types and graph concepts.
/// </summary>
public sealed class NodalModelBuilder
{
    private readonly Dictionary<Type, MutableNodeMetadata> nodes = [];
    private readonly Dictionary<(Type, Type, Type), MutableRelationMetadata> relations = [];

    /// <summary>Begins fluent configuration of a graph node type.</summary>
    public GraphNodeBuilder<TNode> Node<TNode>() => new(DiscoverNode(typeof(TNode)));

    /// <summary>Begins fluent configuration of a strongly typed graph relationship.</summary>
    public GraphRelationBuilder<TSource, TRelation, TTarget> Relation<TSource, TRelation, TTarget>()
        where TRelation : notnull => new(DiscoverRelation(typeof(TSource), typeof(TRelation), typeof(TTarget)));

    internal void DiscoverContext(Type contextType)
    {
        foreach (var property in contextType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            var propertyType = property.PropertyType;
            if (!propertyType.IsGenericType)
            {
                continue;
            }

            var definition = propertyType.GetGenericTypeDefinition();
            var arguments = propertyType.GetGenericArguments();
            if (definition == typeof(GraphSet<>))
            {
                DiscoverNode(arguments[0]);
            }
            else if (definition == typeof(RelationSet<,,>))
            {
                DiscoverRelation(arguments[0], arguments[1], arguments[2]);
            }
        }
    }

    internal NodalModel Build()
    {
        var immutableNodes = nodes.Values.Select(node =>
        {
            if (node.KeyProperty is null)
            {
                throw new InvalidOperationException($"Node type '{node.ClrType}' does not define a key.");
            }

            if (!node.Properties.ContainsKey(node.KeyProperty))
            {
                throw new InvalidOperationException(
                    $"Key property '{node.KeyProperty}' on node type '{node.ClrType}' cannot be ignored.");
            }

            EnsureUniqueGraphPropertyNames(node.ClrType, node.Properties.Values);

            return new GraphNodeMetadata(node.ClrType, node.Name, node.KeyProperty, node.Properties);
        });
        var immutableRelations = relations.Values.Select(relation =>
        {
            EnsureUniqueGraphPropertyNames(relation.ClrType, relation.Properties.Values);
            return new GraphRelationMetadata(
                relation.ClrType,
                relation.Name,
                relation.SourceType,
                relation.TargetType,
                relation.Directed,
                relation.Properties);
        });
        return new NodalModel(immutableNodes, immutableRelations);
    }

    private static void EnsureUniqueGraphPropertyNames(
        Type clrType,
        IEnumerable<GraphPropertyMetadata> properties)
    {
        var duplicate = properties
            .GroupBy(property => property.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Type '{clrType}' maps more than one CLR property to graph property '{duplicate.Key}'.");
        }
    }

    private MutableNodeMetadata DiscoverNode(Type nodeType)
    {
        if (nodes.TryGetValue(nodeType, out var existing))
        {
            return existing;
        }

        var properties = DiscoverProperties(nodeType);
        var attributedKeys = nodeType.GetProperties().Where(property => property.IsDefined(typeof(GraphKeyAttribute))).ToArray();
        if (attributedKeys.Length > 1)
        {
            throw new InvalidOperationException($"Node type '{nodeType}' defines more than one [GraphKey].");
        }

        var conventionalKey = nodeType.GetProperty("Id") ?? nodeType.GetProperty($"{nodeType.Name}Id");
        var metadata = new MutableNodeMetadata(
            nodeType,
            nodeType.GetCustomAttribute<GraphNodeAttribute>()?.Name ?? nodeType.Name,
            attributedKeys.SingleOrDefault()?.Name ?? conventionalKey?.Name,
            properties);
        nodes.Add(nodeType, metadata);
        return metadata;
    }

    private MutableRelationMetadata DiscoverRelation(Type sourceType, Type relationType, Type targetType)
    {
        DiscoverNode(sourceType);
        DiscoverNode(targetType);
        var key = (sourceType, relationType, targetType);
        if (relations.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var attribute = relationType.GetCustomAttribute<GraphRelationAttribute>();
        var metadata = new MutableRelationMetadata(
            relationType,
            attribute?.Name ?? relationType.Name,
            sourceType,
            targetType,
            attribute?.Directed ?? true,
            DiscoverProperties(relationType));
        relations.Add(key, metadata);
        return metadata;
    }

    private static Dictionary<string, GraphPropertyMetadata> DiscoverProperties(Type type) => type
        .GetProperties(BindingFlags.Instance | BindingFlags.Public)
        .Where(property => property.GetIndexParameters().Length == 0)
        .Where(property => !property.IsDefined(typeof(GraphIgnoreAttribute), true))
        .ToDictionary(
            property => property.Name,
            property => new GraphPropertyMetadata(
                property.Name,
                property.GetCustomAttribute<GraphPropertyAttribute>(true)?.Name ?? property.Name,
                property.PropertyType));

    internal sealed class MutableNodeMetadata(
        Type clrType,
        string name,
        string? keyProperty,
        Dictionary<string, GraphPropertyMetadata> properties)
    {
        public Type ClrType { get; } = clrType;
        public string Name { get; set; } = name;
        public string? KeyProperty { get; set; } = keyProperty;
        public Dictionary<string, GraphPropertyMetadata> Properties { get; } = properties;
    }

    internal sealed class MutableRelationMetadata(
        Type clrType,
        string name,
        Type sourceType,
        Type targetType,
        bool directed,
        Dictionary<string, GraphPropertyMetadata> properties)
    {
        public Type ClrType { get; } = clrType;
        public string Name { get; set; } = name;
        public Type SourceType { get; } = sourceType;
        public Type TargetType { get; } = targetType;
        public bool Directed { get; set; } = directed;
        public Dictionary<string, GraphPropertyMetadata> Properties { get; } = properties;
    }
}

/// <summary>Configures a strongly typed graph node mapping.</summary>
public sealed class GraphNodeBuilder<TNode>
{
    private readonly NodalModelBuilder.MutableNodeMetadata metadata;

    internal GraphNodeBuilder(NodalModelBuilder.MutableNodeMetadata metadata) => this.metadata = metadata;

    /// <summary>Overrides the graph node name discovered from attributes or conventions.</summary>
    public GraphNodeBuilder<TNode> HasName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        metadata.Name = name;
        return this;
    }

    /// <summary>Overrides the key discovered from attributes or conventions.</summary>
    public GraphNodeBuilder<TNode> HasKey<TKey>(Expression<Func<TNode, TKey>> property)
    {
        metadata.KeyProperty = GetDirectMember(property).Name;
        return this;
    }

    private static MemberInfo GetDirectMember<TKey>(Expression<Func<TNode, TKey>> property)
    {
        ArgumentNullException.ThrowIfNull(property);
        var body = property.Body is UnaryExpression unary ? unary.Operand : property.Body;
        return body is MemberExpression member && member.Expression == property.Parameters[0]
            ? member.Member
            : throw new ArgumentException("A direct node property is required.", nameof(property));
    }
}

/// <summary>Configures a strongly typed graph relationship mapping.</summary>
public sealed class GraphRelationBuilder<TSource, TRelation, TTarget>
    where TRelation : notnull
{
    private readonly NodalModelBuilder.MutableRelationMetadata metadata;

    internal GraphRelationBuilder(NodalModelBuilder.MutableRelationMetadata metadata) => this.metadata = metadata;

    /// <summary>Overrides the relationship name discovered from attributes or conventions.</summary>
    public GraphRelationBuilder<TSource, TRelation, TTarget> HasName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        metadata.Name = name;
        return this;
    }

    /// <summary>Configures whether the relationship is directed.</summary>
    public GraphRelationBuilder<TSource, TRelation, TTarget> IsDirected(bool directed = true)
    {
        metadata.Directed = directed;
        return this;
    }
}
