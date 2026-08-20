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

/// <summary>Preserves one provider result row and its node-to-measurement association.</summary>
/// <param name="Node">The normalized node returned by the row, when present.</param>
/// <param name="Values">The scalar measurements returned beside the node.</param>
public sealed record GraphResultRow(
    GraphNodeRecord? Node,
    IReadOnlyDictionary<string, object?> Values);

/// <summary>Preserves one ordered provider route before domain materialization.</summary>
/// <param name="Nodes">Ordered normalized nodes.</param>
/// <param name="Relations">Ordered normalized relationships.</param>
/// <param name="TotalCost">Optional weighted total cost.</param>
public sealed record GraphRouteRecord(
    IReadOnlyList<GraphNodeRecord> Nodes,
    IReadOnlyList<GraphRelationRecord> Relations,
    double? TotalCost = null);

/// <summary>
/// Contains normalized graph records returned by a provider command.
/// </summary>
/// <param name="Nodes">The normalized node records.</param>
/// <param name="Relations">The normalized relationship records.</param>
/// <param name="Paths">The normalized path associations.</param>
/// <param name="Scalars">Named scalar values returned by an aggregate query.</param>
/// <param name="Rows">Provider result rows used by analytics operations.</param>
/// <param name="Routes">Ordered route records returned by path-finding operations.</param>
public sealed record GraphQueryResult(
    IReadOnlyList<GraphNodeRecord> Nodes,
    IReadOnlyList<GraphRelationRecord>? Relations = null,
    IReadOnlyList<GraphPathRecord>? Paths = null,
    IReadOnlyDictionary<string, object?>? Scalars = null,
    IReadOnlyList<GraphResultRow>? Rows = null,
    IReadOnlyList<GraphRouteRecord>? Routes = null)
{
    /// <summary>Gets normalized relationships, or an empty collection for node-only results.</summary>
    public IReadOnlyList<GraphRelationRecord> RelationRecords => Relations ?? [];

    /// <summary>Gets normalized paths, or an empty collection for node-only results.</summary>
    public IReadOnlyList<GraphPathRecord> PathRecords => Paths ?? [];

    /// <summary>Gets named scalar values, or an empty dictionary for graph-record results.</summary>
    public IReadOnlyDictionary<string, object?> ScalarValues => Scalars ??
        new Dictionary<string, object?>();

    /// <summary>Gets row-preserving analytics values, or an empty collection.</summary>
    public IReadOnlyList<GraphResultRow> ResultRows => Rows ?? [];

    /// <summary>Gets normalized route records, or an empty collection.</summary>
    public IReadOnlyList<GraphRouteRecord> RouteRecords => Routes ?? [];
}
