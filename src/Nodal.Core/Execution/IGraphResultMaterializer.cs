namespace Nodal.Core.Execution;

/// <summary>
/// Materializes provider-normalized graph records as domain objects.
/// </summary>
public interface IGraphResultMaterializer
{
    /// <summary>
    /// Converts normalized records to the requested node type.
    /// </summary>
    IReadOnlyList<TNode> Materialize<TNode>(GraphQueryResult result);

    /// <summary>Converts normalized path records to strongly typed graph paths.</summary>
    IReadOnlyList<Model.GraphPath<TSource, TRelation, TTarget>> MaterializePaths<TSource, TRelation, TTarget>(
        GraphQueryResult result)
        where TRelation : notnull;
}
