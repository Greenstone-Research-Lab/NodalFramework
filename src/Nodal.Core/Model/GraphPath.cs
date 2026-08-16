namespace Nodal.Core.Model;

/// <summary>
/// Represents one strongly typed hop while preserving both endpoint nodes and the relationship payload.
/// </summary>
/// <typeparam name="TSource">The node from which the traversal started.</typeparam>
/// <typeparam name="TRelation">The relationship payload type.</typeparam>
/// <typeparam name="TTarget">The node reached by the traversal.</typeparam>
/// <param name="Source">The materialized source node.</param>
/// <param name="Relation">The materialized relationship payload.</param>
/// <param name="Target">The materialized target node.</param>
public sealed record GraphPath<TSource, TRelation, TTarget>(
    TSource Source,
    TRelation Relation,
    TTarget Target)
    where TRelation : notnull;
