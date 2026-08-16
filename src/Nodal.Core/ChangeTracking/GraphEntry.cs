using System.Reflection;
using Nodal.Core.Metadata;

namespace Nodal.Core.ChangeTracking;

/// <summary>Provides common state for a node or relationship tracked by a context.</summary>
public abstract class GraphEntry
{
    internal GraphEntry(GraphEntryState state) => State = state;

    /// <summary>Gets the current unit-of-work state.</summary>
    public GraphEntryState State { get; internal set; }
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

    IReadOnlyDictionary<string, object?> IGraphNodeEntry.ReadProperties() => ReadProperties();
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
