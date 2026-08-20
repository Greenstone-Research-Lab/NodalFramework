using System.Linq.Expressions;
using System.Reflection;
using Nodal.Core.Metadata;

namespace Nodal.Core.ChangeTracking;

/// <summary>Provides common state for a node or relationship tracked by a context.</summary>
public abstract class GraphEntry
{
    internal GraphEntry(GraphEntryState state) => State = state;

    /// <summary>Gets the current unit-of-work state.</summary>
    public GraphEntryState State { get; internal set; }

    /// <summary>Gets mapped provider property names whose values differ from the original snapshot.</summary>
    public IReadOnlySet<string> ModifiedProperties { get; internal set; } = new HashSet<string>();

    /// <summary>Gets the mapped property snapshot captured when the entry became unchanged.</summary>
    public IReadOnlyDictionary<string, object?> OriginalValues { get; private set; } =
        new Dictionary<string, object?>();

    internal bool IsExplicitlyModified { get; set; }

    internal void CaptureSnapshot(IReadOnlyDictionary<string, object?> values) =>
        OriginalValues = new Dictionary<string, object?>(values, StringComparer.Ordinal);
}

/// <summary>Represents a graph node tracked by the current context.</summary>
/// <typeparam name="TNode">The node POCO type.</typeparam>
public sealed class GraphNodeEntry<TNode> : GraphEntry, IGraphNodeEntry
{
    internal GraphNodeEntry(TNode node, GraphNodeMetadata metadata, GraphIdentity identity, GraphEntryState state)
        : base(state)
    {
        Node = node;
        Metadata = metadata;
        Identity = identity;
    }

    /// <summary>Gets the tracked node instance.</summary>
    public TNode Node { get; }

    /// <summary>Gets the stable graph identity.</summary>
    public GraphIdentity Identity { get; }

    internal GraphNodeMetadata Metadata { get; }

    internal IReadOnlyDictionary<string, object?> ReadProperties() => Metadata.Properties.Values.ToDictionary(
        property => property.Name,
        property => Metadata.ClrType.GetProperty(property.ClrName, BindingFlags.Instance | BindingFlags.Public)!
            .GetValue(Node));

    /// <summary>Gets the current mapped provider property values.</summary>
    public IReadOnlyDictionary<string, object?> CurrentValues => ReadProperties();

    /// <summary>Gets change-control access for one direct mapped property.</summary>
    public GraphPropertyEntry Property<TProperty>(
        Expression<Func<TNode, TProperty>> propertyExpression)
    {
        ArgumentNullException.ThrowIfNull(propertyExpression);
        Expression body = propertyExpression.Body;
        while (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } conversion)
        {
            body = conversion.Operand;
        }

        if (body is not MemberExpression member ||
            member.Expression != propertyExpression.Parameters[0] ||
            !Metadata.Properties.TryGetValue(member.Member.Name, out var metadata))
        {
            throw new NotSupportedException(
                $"Expression '{propertyExpression}' must select a direct mapped property.");
        }

        return new GraphPropertyEntry(this, metadata.Name);
    }

    IReadOnlyDictionary<string, object?> IGraphNodeEntry.ReadProperties() => ReadProperties();
}

/// <summary>Controls modification state for one mapped graph property.</summary>
public sealed class GraphPropertyEntry
{
    private readonly GraphEntry entry;

    internal GraphPropertyEntry(GraphEntry entry, string propertyName)
    {
        this.entry = entry;
        PropertyName = propertyName;
    }

    /// <summary>Gets the provider property name.</summary>
    public string PropertyName { get; }

    /// <summary>Gets or sets whether this property participates in the next update.</summary>
    public bool IsModified
    {
        get => entry.ModifiedProperties.Contains(PropertyName);
        set
        {
            var properties = entry.ModifiedProperties.ToHashSet(StringComparer.Ordinal);
            if (value)
            {
                properties.Add(PropertyName);
                entry.State = GraphEntryState.Modified;
            }
            else
            {
                properties.Remove(PropertyName);
                if (properties.Count == 0 && !entry.IsExplicitlyModified)
                {
                    entry.State = GraphEntryState.Unchanged;
                }
            }

            entry.ModifiedProperties = properties;
        }
    }
}

internal interface IGraphNodeEntry
{
    GraphIdentity Identity { get; }

    IReadOnlyDictionary<string, object?> ReadProperties();
}

/// <summary>Represents a relationship and its two endpoint identities in the current context.</summary>
/// <typeparam name="TSource">The source node POCO type.</typeparam>
/// <typeparam name="TRelation">The relationship POCO type.</typeparam>
/// <typeparam name="TTarget">The target node POCO type.</typeparam>
public sealed class GraphRelationEntry<TSource, TRelation, TTarget> : GraphEntry, IGraphRelationEntry
    where TRelation : notnull
{
    internal GraphRelationEntry(
        TSource source,
        TRelation relation,
        TTarget target,
        GraphIdentity sourceIdentity,
        GraphIdentity targetIdentity,
        GraphRelationMetadata metadata,
        GraphEntryState state,
        object? providerId = null)
        : base(state)
    {
        Source = source;
        Relation = relation;
        Target = target;
        SourceIdentity = sourceIdentity;
        TargetIdentity = targetIdentity;
        Metadata = metadata;
        ProviderId = providerId;
    }

    /// <summary>Gets the source node instance.</summary>
    public TSource Source { get; }

    /// <summary>Gets the relationship payload instance.</summary>
    public TRelation Relation { get; }

    /// <summary>Gets the target node instance.</summary>
    public TTarget Target { get; }

    /// <summary>Gets the source node identity.</summary>
    public GraphIdentity SourceIdentity { get; }

    /// <summary>Gets the target node identity.</summary>
    public GraphIdentity TargetIdentity { get; }

    /// <summary>Gets the provider relationship identity when the entry originated from a query.</summary>
    public object? ProviderId { get; }

    internal GraphRelationMetadata Metadata { get; }

    internal IReadOnlyDictionary<string, object?> ReadProperties() => Metadata.Properties.Values.ToDictionary(
        property => property.Name,
        property => Metadata.ClrType.GetProperty(property.ClrName, BindingFlags.Instance | BindingFlags.Public)!
            .GetValue(Relation));

    GraphRelationMetadata IGraphRelationEntry.Metadata => Metadata;

    object? IGraphRelationEntry.ProviderId => ProviderId;

    IReadOnlyDictionary<string, object?> IGraphRelationEntry.ReadProperties() => ReadProperties();
}

internal interface IGraphRelationEntry
{
    GraphIdentity SourceIdentity { get; }

    GraphIdentity TargetIdentity { get; }

    GraphRelationMetadata Metadata { get; }

    object? ProviderId { get; }

    IReadOnlyDictionary<string, object?> ReadProperties();
}
