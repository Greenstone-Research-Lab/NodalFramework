using Nodal.Core.ChangeTracking;
using Nodal.Core.Metadata;

namespace Nodal.Core.Query;

/// <summary>
/// Represents a strongly typed relationship root between two graph node types.
/// </summary>
/// <typeparam name="TSource">The source node type.</typeparam>
/// <typeparam name="TRelation">The relationship POCO type.</typeparam>
/// <typeparam name="TTarget">The target node type.</typeparam>
public sealed class RelationSet<TSource, TRelation, TTarget>
    where TRelation : notnull
{
    private readonly GraphStateManager stateManager;
    private readonly GraphNodeMetadata sourceMetadata;
    private readonly GraphNodeMetadata targetMetadata;

    internal RelationSet(
        GraphRelationMetadata metadata,
        GraphNodeMetadata sourceMetadata,
        GraphNodeMetadata targetMetadata,
        GraphStateManager stateManager)
    {
        Metadata = metadata;
        this.sourceMetadata = sourceMetadata;
        this.targetMetadata = targetMetadata;
        this.stateManager = stateManager;
    }

    /// <summary>Gets the provider-neutral relationship mapping.</summary>
    public GraphRelationMetadata Metadata { get; }

    internal GraphNodeMetadata SourceMetadata => sourceMetadata;

    internal GraphNodeMetadata TargetMetadata => targetMetadata;

    /// <summary>Adds a relationship and its payload to the current unit of work.</summary>
    public GraphRelationEntry<TSource, TRelation, TTarget> Connect(
        TSource source,
        TRelation relation,
        TTarget target) => stateManager.Connect(
            source,
            relation,
            target,
            sourceMetadata,
            targetMetadata,
            Metadata);

    /// <summary>Marks a specific relationship instance for deletion.</summary>
    public GraphRelationEntry<TSource, TRelation, TTarget> Disconnect(
        TSource source,
        TRelation relation,
        TTarget target) => stateManager.Disconnect(
            source,
            relation,
            target,
            sourceMetadata,
            targetMetadata,
            Metadata);

    /// <summary>Marks the mapped properties of a relationship for update.</summary>
    public GraphRelationEntry<TSource, TRelation, TTarget> Update(
        TSource source,
        TRelation relation,
        TTarget target) => stateManager.UpdateRelation(
            source,
            relation,
            target,
            sourceMetadata,
            targetMetadata,
            Metadata);
}
