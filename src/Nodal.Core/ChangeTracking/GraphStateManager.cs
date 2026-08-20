using System.Reflection;
using Nodal.Core.Metadata;

namespace Nodal.Core.ChangeTracking;

internal sealed class GraphStateManager
{
    private readonly Dictionary<(Type Type, object Key), GraphEntry> identityMap = [];
    private readonly List<GraphEntry> entries = [];

    public IReadOnlyList<GraphEntry> Entries => entries;

    public GraphNodeEntry<TNode> Add<TNode>(TNode node, GraphNodeMetadata metadata) =>
        TrackNode(node, metadata, GraphEntryState.Added);

    public GraphNodeEntry<TNode> Update<TNode>(TNode node, GraphNodeMetadata metadata) =>
        TrackNode(node, metadata, GraphEntryState.Modified);

    public GraphNodeEntry<TNode> Attach<TNode>(TNode node, GraphNodeMetadata metadata) =>
        TrackNode(node, metadata, GraphEntryState.Unchanged);

    public GraphNodeEntry<TNode> TrackFromQuery<TNode>(TNode node, GraphNodeMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(node);
        var identity = CreateIdentity(node, metadata);
        if (identityMap.TryGetValue((metadata.ClrType, identity.Value), out var tracked))
        {
            return (GraphNodeEntry<TNode>)tracked;
        }

        var entry = new GraphNodeEntry<TNode>(node, metadata, identity, GraphEntryState.Unchanged);
        entry.CaptureSnapshot(entry.ReadProperties());
        identityMap.Add((metadata.ClrType, identity.Value), entry);
        entries.Add(entry);
        return entry;
    }

    public GraphRelationEntry<TSource, TRelation, TTarget> TrackRelationFromQuery<TSource, TRelation, TTarget>(
        TSource source,
        TRelation relation,
        TTarget target,
        object providerId,
        GraphNodeMetadata sourceMetadata,
        GraphNodeMetadata targetMetadata,
        GraphRelationMetadata relationMetadata)
        where TRelation : notnull
    {
        var existing = entries.OfType<GraphRelationEntry<TSource, TRelation, TTarget>>()
            .FirstOrDefault(entry => Equals(entry.ProviderId, providerId));
        if (existing is not null)
        {
            return existing;
        }

        var entry = new GraphRelationEntry<TSource, TRelation, TTarget>(
            source,
            relation,
            target,
            CreateIdentity(source, sourceMetadata),
            CreateIdentity(target, targetMetadata),
            relationMetadata,
            GraphEntryState.Unchanged,
            providerId);
        entry.CaptureSnapshot(entry.ReadProperties());
        entries.Add(entry);
        return entry;
    }

    public GraphNodeEntry<TNode> Remove<TNode>(TNode node, GraphNodeMetadata metadata)
    {
        var entry = TrackNode(node, metadata, GraphEntryState.Deleted);
        if (entry.State == GraphEntryState.Added)
        {
            Detach(entry);
        }
        else
        {
            entry.State = GraphEntryState.Deleted;
        }

        return entry;
    }

    public GraphRelationEntry<TSource, TRelation, TTarget> Connect<TSource, TRelation, TTarget>(
        TSource source,
        TRelation relation,
        TTarget target,
        GraphNodeMetadata sourceMetadata,
        GraphNodeMetadata targetMetadata,
        GraphRelationMetadata relationMetadata)
        where TRelation : notnull
    {
        ArgumentNullException.ThrowIfNull(relation);
        var existing = entries.OfType<GraphRelationEntry<TSource, TRelation, TTarget>>().FirstOrDefault(entry =>
            ReferenceEquals(entry.Source, source) &&
            ReferenceEquals(entry.Relation, relation) &&
            ReferenceEquals(entry.Target, target));
        if (existing is not null)
        {
            existing.State = GraphEntryState.Added;
            return existing;
        }

        var entry = new GraphRelationEntry<TSource, TRelation, TTarget>(
            source,
            relation,
            target,
            CreateIdentity(source, sourceMetadata),
            CreateIdentity(target, targetMetadata),
            relationMetadata,
            GraphEntryState.Added);
        entries.Add(entry);
        return entry;
    }

    public GraphRelationEntry<TSource, TRelation, TTarget> Disconnect<TSource, TRelation, TTarget>(
        TSource source,
        TRelation relation,
        TTarget target,
        GraphNodeMetadata sourceMetadata,
        GraphNodeMetadata targetMetadata,
        GraphRelationMetadata relationMetadata)
        where TRelation : notnull
    {
        ArgumentNullException.ThrowIfNull(relation);
        var existing = entries.OfType<GraphRelationEntry<TSource, TRelation, TTarget>>().FirstOrDefault(entry =>
            ReferenceEquals(entry.Source, source) &&
            ReferenceEquals(entry.Relation, relation) &&
            ReferenceEquals(entry.Target, target));
        if (existing is not null)
        {
            if (existing.State == GraphEntryState.Added)
            {
                entries.Remove(existing);
                existing.State = GraphEntryState.Detached;
            }
            else
            {
                existing.State = GraphEntryState.Deleted;
            }

            return existing;
        }

        var entry = new GraphRelationEntry<TSource, TRelation, TTarget>(
            source,
            relation,
            target,
            CreateIdentity(source, sourceMetadata),
            CreateIdentity(target, targetMetadata),
            relationMetadata,
            GraphEntryState.Deleted);
        entries.Add(entry);
        return entry;
    }

    public GraphRelationEntry<TSource, TRelation, TTarget> UpdateRelation<TSource, TRelation, TTarget>(
        TSource source,
        TRelation relation,
        TTarget target,
        GraphNodeMetadata sourceMetadata,
        GraphNodeMetadata targetMetadata,
        GraphRelationMetadata relationMetadata)
        where TRelation : notnull
    {
        ArgumentNullException.ThrowIfNull(relation);
        var existing = entries.OfType<GraphRelationEntry<TSource, TRelation, TTarget>>().FirstOrDefault(entry =>
            ReferenceEquals(entry.Source, source) &&
            ReferenceEquals(entry.Relation, relation) &&
            ReferenceEquals(entry.Target, target));
        if (existing is not null)
        {
            if (existing.State != GraphEntryState.Added)
            {
                existing.State = GraphEntryState.Modified;
            }

            return existing;
        }

        var entry = new GraphRelationEntry<TSource, TRelation, TTarget>(
            source,
            relation,
            target,
            CreateIdentity(source, sourceMetadata),
            CreateIdentity(target, targetMetadata),
            relationMetadata,
            GraphEntryState.Modified);
        entries.Add(entry);
        return entry;
    }

    public void AcceptAllChanges()
    {
        foreach (var entry in entries.ToArray())
        {
            if (entry.State == GraphEntryState.Deleted)
            {
                Detach(entry);
            }
            else if (entry.State is GraphEntryState.Added or GraphEntryState.Modified)
            {
                entry.State = GraphEntryState.Unchanged;
                entry.IsExplicitlyModified = false;
                entry.ModifiedProperties = new HashSet<string>();
                entry.CaptureSnapshot(ReadProperties(entry));
            }
        }
    }

    public void DetectChanges()
    {
        foreach (var entry in entries.Where(candidate =>
                     candidate.State is GraphEntryState.Unchanged or GraphEntryState.Modified))
        {
            var current = ReadProperties(entry);
            var changed = current
                .Where(property => !entry.OriginalValues.TryGetValue(property.Key, out var original) ||
                                   !Equals(original, property.Value))
                .Select(property => property.Key)
                .ToHashSet(StringComparer.Ordinal);
            entry.ModifiedProperties = changed;
            if (!entry.IsExplicitlyModified)
            {
                entry.State = changed.Count == 0 ? GraphEntryState.Unchanged : GraphEntryState.Modified;
            }
        }
    }

    public void DetachEntry(GraphEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Detach(entry);
    }

    public GraphNodeEntry<TNode> GetEntry<TNode>(TNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var entry = entries.OfType<GraphNodeEntry<TNode>>()
            .FirstOrDefault(candidate => ReferenceEquals(candidate.Node, node));
        return entry ?? throw new InvalidOperationException("The supplied node is not tracked by this context.");
    }

    public void AcceptReload<TNode>(TNode node)
    {
        var entry = GetEntry(node);
        entry.State = GraphEntryState.Unchanged;
        entry.IsExplicitlyModified = false;
        entry.ModifiedProperties = new HashSet<string>();
        entry.CaptureSnapshot(entry.ReadProperties());
    }

    private GraphNodeEntry<TNode> TrackNode<TNode>(
        TNode node,
        GraphNodeMetadata metadata,
        GraphEntryState requestedState)
    {
        ArgumentNullException.ThrowIfNull(node);
        var identity = CreateIdentity(node, metadata);
        if (identityMap.TryGetValue((metadata.ClrType, identity.Value), out var tracked))
        {
            var typed = (GraphNodeEntry<TNode>)tracked;
            if (!ReferenceEquals(typed.Node, node))
            {
                throw new InvalidOperationException(
                    $"A different instance of node '{metadata.ClrType}' with key '{identity.Value}' is already tracked.");
            }

            if (typed.State != GraphEntryState.Added)
            {
                typed.State = requestedState;
                typed.IsExplicitlyModified |= requestedState == GraphEntryState.Modified;
            }

            return typed;
        }

        var entry = new GraphNodeEntry<TNode>(node, metadata, identity, requestedState);
        entry.IsExplicitlyModified = requestedState == GraphEntryState.Modified;
        entry.CaptureSnapshot(entry.ReadProperties());
        identityMap.Add((metadata.ClrType, identity.Value), entry);
        entries.Add(entry);
        return entry;
    }

    private static GraphIdentity CreateIdentity<TNode>(TNode node, GraphNodeMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(node);
        var property = metadata.ClrType.GetProperty(
            metadata.KeyProperty,
            BindingFlags.Instance | BindingFlags.Public)!;
        var value = property.GetValue(node) ?? throw new InvalidOperationException(
            $"Key property '{metadata.KeyProperty}' on node '{metadata.ClrType}' cannot be null.");
        return new GraphIdentity(
            metadata.ClrType,
            metadata.Name,
            metadata.Properties[metadata.KeyProperty].Name,
            value);
    }

    private void Detach(GraphEntry entry)
    {
        entries.Remove(entry);
        entry.State = GraphEntryState.Detached;
        if (entry is IGraphNodeEntry nodeEntry)
        {
            identityMap.Remove((nodeEntry.Identity.ClrType, nodeEntry.Identity.Value));
        }
    }

    private static IReadOnlyDictionary<string, object?> ReadProperties(GraphEntry entry) => entry switch
    {
        IGraphNodeEntry node => node.ReadProperties(),
        IGraphRelationEntry relation => relation.ReadProperties(),
        _ => throw new NotSupportedException($"Entry type '{entry.GetType().Name}' cannot be snapshot tracked."),
    };
}
