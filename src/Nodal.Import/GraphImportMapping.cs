namespace Nodal.Import;

/// <summary>Identifies the graph construct produced by an explicit import mapping.</summary>
public enum GraphImportMappingKind
{
    /// <summary>The mapping produces graph nodes.</summary>
    Node,

    /// <summary>The mapping produces graph relations.</summary>
    Relation,
}

/// <summary>Describes how an import mapping writes its graph target.</summary>
public enum GraphImportWriteBehavior
{
    /// <summary>Creates the target when absent and updates mapped properties when it already exists.</summary>
    Upsert,
}

/// <summary>Describes one explicit mapping decision included in a dry-run report.</summary>
/// <param name="Kind">The graph construct produced by the mapping.</param>
/// <param name="MappingName">The stable name used to reference this mapping.</param>
/// <param name="TargetName">The provider-neutral node or relation type.</param>
/// <param name="WriteBehavior">The write behavior represented by the generated operations.</param>
/// <param name="PropertyNames">The graph property names selected by the mapping.</param>
/// <param name="SourceMappingName">The source node mapping for a relation, when applicable.</param>
/// <param name="TargetMappingName">The target node mapping for a relation, when applicable.</param>
public sealed record GraphImportMappingDecision(
    GraphImportMappingKind Kind,
    string MappingName,
    string TargetName,
    GraphImportWriteBehavior WriteBehavior,
    IReadOnlyList<string> PropertyNames,
    string? SourceMappingName = null,
    string? TargetMappingName = null);

/// <summary>Starts explicit, provider-neutral graph import mapping configuration.</summary>
public static class GraphImportMapping
{
    /// <summary>Creates a mapping builder for one source record type.</summary>
    /// <typeparam name="TRecord">The source record type.</typeparam>
    /// <returns>A new explicit mapping builder.</returns>
    /// <example>
    /// <code>
    /// var mapping = GraphImportMapping.For&lt;OrderRow&gt;()
    ///     .Node&lt;Customer&gt;("customer", "Customer", "Id", row =&gt; row.CustomerId,
    ///         node =&gt; node.Property("Name", row =&gt; row.CustomerName))
    ///     .Node&lt;Order&gt;("order", "Order", "Id", row =&gt; row.OrderId)
    ///     .Relation("placed", "customer", "order", "PLACED")
    ///     .Build();
    /// </code>
    /// </example>
    public static GraphImportMappingBuilder<TRecord> For<TRecord>() => new();
}

/// <summary>Builds explicit node and relation mappings for one source record type.</summary>
/// <typeparam name="TRecord">The source record type.</typeparam>
public sealed class GraphImportMappingBuilder<TRecord>
{
    private readonly List<GraphImportNodeMapping<TRecord>> nodes = [];
    private readonly List<GraphImportRelationMapping<TRecord>> relations = [];
    private readonly HashSet<string> names = new(StringComparer.Ordinal);

    /// <summary>Adds a node mapping whose identity and properties are selected from each source record.</summary>
    /// <typeparam name="TNode">The domain node type represented by the mapping.</typeparam>
    /// <param name="mappingName">A stable name used by relation mappings.</param>
    /// <param name="nodeType">The provider-neutral graph node type.</param>
    /// <param name="keyProperty">The graph property containing the stable identity.</param>
    /// <param name="keySelector">Selects the stable identity from a source record.</param>
    /// <param name="configure">Optionally configures graph property mappings.</param>
    /// <returns>The current builder.</returns>
    public GraphImportMappingBuilder<TRecord> Node<TNode>(
        string mappingName,
        string nodeType,
        string keyProperty,
        Func<TRecord, object?> keySelector,
        Action<GraphImportPropertyBuilder<TRecord>>? configure = null)
    {
        ValidateName(mappingName, nameof(mappingName));
        ValidateName(nodeType, nameof(nodeType));
        ValidateName(keyProperty, nameof(keyProperty));
        ArgumentNullException.ThrowIfNull(keySelector);
        AddName(mappingName);

        var properties = new GraphImportPropertyBuilder<TRecord>();
        configure?.Invoke(properties);
        if (properties.Mappings.Any(property => string.Equals(property.Name, keyProperty, StringComparison.Ordinal)))
        {
            throw new ArgumentException("The stable key is supplied by the key selector and cannot also be mapped as a property.", nameof(configure));
        }

        nodes.Add(new GraphImportNodeMapping<TRecord>(
            mappingName,
            typeof(TNode),
            nodeType,
            keyProperty,
            keySelector,
            properties.Mappings));
        return this;
    }

    /// <summary>Adds a relation mapping between two node mappings.</summary>
    /// <param name="mappingName">A stable name for the relation mapping.</param>
    /// <param name="sourceMappingName">The source node mapping name.</param>
    /// <param name="targetMappingName">The target node mapping name.</param>
    /// <param name="relationType">The provider-neutral graph relation type.</param>
    /// <param name="directed">Whether relation direction is semantically significant.</param>
    /// <param name="configure">Optionally configures graph property mappings.</param>
    /// <returns>The current builder.</returns>
    public GraphImportMappingBuilder<TRecord> Relation(
        string mappingName,
        string sourceMappingName,
        string targetMappingName,
        string relationType,
        bool directed = true,
        Action<GraphImportPropertyBuilder<TRecord>>? configure = null)
    {
        ValidateName(mappingName, nameof(mappingName));
        ValidateName(sourceMappingName, nameof(sourceMappingName));
        ValidateName(targetMappingName, nameof(targetMappingName));
        ValidateName(relationType, nameof(relationType));
        AddName(mappingName);

        var properties = new GraphImportPropertyBuilder<TRecord>();
        configure?.Invoke(properties);
        relations.Add(new GraphImportRelationMapping<TRecord>(
            mappingName,
            sourceMappingName,
            targetMappingName,
            relationType,
            directed,
            properties.Mappings));
        return this;
    }

    /// <summary>Validates references and creates an immutable mapping definition.</summary>
    /// <returns>The immutable import mapping.</returns>
    public GraphImportMapping<TRecord> Build()
    {
        if (nodes.Count == 0)
        {
            throw new InvalidOperationException("An import mapping must define at least one node mapping.");
        }

        var nodeNames = nodes.Select(node => node.Name).ToHashSet(StringComparer.Ordinal);
        var unresolved = relations.FirstOrDefault(relation =>
            !nodeNames.Contains(relation.SourceMappingName) || !nodeNames.Contains(relation.TargetMappingName));
        if (unresolved is not null)
        {
            throw new InvalidOperationException(
                $"Relation mapping '{unresolved.Name}' references an undefined source or target node mapping.");
        }

        return new GraphImportMapping<TRecord>(nodes.ToArray(), relations.ToArray());
    }

    private static void ValidateName(string value, string parameterName) =>
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

    private void AddName(string name)
    {
        if (!names.Add(name))
        {
            throw new ArgumentException($"Import mapping name '{name}' is already defined.", nameof(name));
        }
    }
}

/// <summary>Builds an explicit property projection from a source record.</summary>
/// <typeparam name="TRecord">The source record type.</typeparam>
public sealed class GraphImportPropertyBuilder<TRecord>
{
    private readonly List<GraphImportPropertyMapping<TRecord>> mappings = [];
    private readonly HashSet<string> names = new(StringComparer.Ordinal);

    internal IReadOnlyList<GraphImportPropertyMapping<TRecord>> Mappings => mappings;

    /// <summary>Adds a graph property selected from each source record.</summary>
    /// <param name="propertyName">The provider-neutral graph property name.</param>
    /// <param name="valueSelector">Selects the property value.</param>
    /// <returns>The current property builder.</returns>
    public GraphImportPropertyBuilder<TRecord> Property(string propertyName, Func<TRecord, object?> valueSelector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(valueSelector);
        if (!names.Add(propertyName))
        {
            throw new ArgumentException($"Graph property '{propertyName}' is already mapped.", nameof(propertyName));
        }
        mappings.Add(new GraphImportPropertyMapping<TRecord>(propertyName, valueSelector));
        return this;
    }
}

/// <summary>Contains an immutable explicit mapping for one source record type.</summary>
/// <typeparam name="TRecord">The source record type.</typeparam>
public sealed class GraphImportMapping<TRecord>
{
    private readonly IReadOnlyList<GraphImportMappingDecision> decisions;

    internal GraphImportMapping(
        IReadOnlyList<GraphImportNodeMapping<TRecord>> nodes,
        IReadOnlyList<GraphImportRelationMapping<TRecord>> relations)
    {
        Nodes = nodes;
        Relations = relations;
        decisions = Nodes.Select(node => new GraphImportMappingDecision(
                GraphImportMappingKind.Node,
                node.Name,
                node.NodeType,
                GraphImportWriteBehavior.Upsert,
                node.Properties.Select(property => property.Name).ToArray()))
            .Concat(Relations.Select(relation => new GraphImportMappingDecision(
                GraphImportMappingKind.Relation,
                relation.Name,
                relation.RelationType,
                GraphImportWriteBehavior.Upsert,
                relation.Properties.Select(property => property.Name).ToArray(),
                relation.SourceMappingName,
                relation.TargetMappingName)))
            .ToArray();
    }

    internal IReadOnlyList<GraphImportNodeMapping<TRecord>> Nodes { get; }

    internal IReadOnlyList<GraphImportRelationMapping<TRecord>> Relations { get; }

    /// <summary>Gets the reviewable mapping decisions in dependency-safe order.</summary>
    public IReadOnlyList<GraphImportMappingDecision> Decisions => decisions;
}

internal sealed record GraphImportPropertyMapping<TRecord>(string Name, Func<TRecord, object?> ValueSelector);

internal sealed record GraphImportNodeMapping<TRecord>(
    string Name,
    Type ClrType,
    string NodeType,
    string KeyProperty,
    Func<TRecord, object?> KeySelector,
    IReadOnlyList<GraphImportPropertyMapping<TRecord>> Properties);

internal sealed record GraphImportRelationMapping<TRecord>(
    string Name,
    string SourceMappingName,
    string TargetMappingName,
    string RelationType,
    bool Directed,
    IReadOnlyList<GraphImportPropertyMapping<TRecord>> Properties);
