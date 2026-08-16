namespace Nodal.Core.Execution;

/// <summary>
/// Represents a provider-normalized node returned by a graph database.
/// </summary>
/// <param name="Type">The provider-neutral node type.</param>
/// <param name="Id">The node identifier.</param>
/// <param name="Properties">The normalized node properties.</param>
public sealed record GraphNodeRecord(
    string Type,
    object Id,
    IReadOnlyDictionary<string, object?> Properties);

/// <summary>Represents a provider-normalized relationship returned by a graph database.</summary>
public sealed record GraphRelationRecord(
    string Type,
    object Id,
    object SourceId,
    object TargetId,
    IReadOnlyDictionary<string, object?> Properties);

/// <summary>Preserves one source–relationship–target association.</summary>
public sealed record GraphPathRecord(
    GraphNodeRecord Source,
    GraphRelationRecord Relation,
    GraphNodeRecord Target);

/// <summary>
/// Contains normalized graph records returned by a provider command.
/// </summary>
/// <param name="Nodes">The normalized node records.</param>
/// <param name="Relations">The normalized relationship records.</param>
/// <param name="Paths">The normalized path associations.</param>
public sealed record GraphQueryResult(
    IReadOnlyList<GraphNodeRecord> Nodes,
    IReadOnlyList<GraphRelationRecord>? Relations = null,
    IReadOnlyList<GraphPathRecord>? Paths = null)
{
    /// <summary>Gets normalized relationships, or an empty collection for node-only results.</summary>
    public IReadOnlyList<GraphRelationRecord> RelationRecords => Relations ?? [];

    /// <summary>Gets normalized paths, or an empty collection for node-only results.</summary>
    public IReadOnlyList<GraphPathRecord> PathRecords => Paths ?? [];
}
