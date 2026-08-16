using Nodal.Core.Query;

namespace Nodal.Core.Execution;

internal sealed class ProviderGraphQueryExecutor(
    IGraphProvider provider,
    ChangeTracking.GraphStateManager stateManager,
    Func<Metadata.NodalModel> modelAccessor) : IGraphQueryExecutor
{
    public async ValueTask<IReadOnlyList<TNode>> ExecuteAsync<TNode>(
        GraphQueryModel query,
        CancellationToken cancellationToken)
    {
        var command = provider.QueryCompiler.Compile(query);
        var result = await provider.CommandExecutor.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
        var materialized = provider.ResultMaterializer.Materialize<TNode>(result);
        var metadata = modelAccessor().GetNode<TNode>();
        return materialized.Select(node => stateManager.TrackFromQuery(node, metadata).Node).ToArray();
    }

    public async ValueTask<IReadOnlyList<Model.GraphPath<TSource, TRelation, TTarget>>> ExecutePathsAsync<TSource, TRelation, TTarget>(
        GraphQueryModel query,
        CancellationToken cancellationToken)
        where TRelation : notnull
    {
        var command = provider.QueryCompiler.Compile(query);
        var result = await provider.CommandExecutor.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
        var paths = provider.ResultMaterializer.MaterializePaths<TSource, TRelation, TTarget>(result);
        var model = modelAccessor();
        var sourceMetadata = model.GetNode<TSource>();
        var targetMetadata = model.GetNode<TTarget>();
        var relationMetadata = model.GetRelation<TSource, TRelation, TTarget>();
        return paths.Select((path, index) =>
        {
            var source = stateManager.TrackFromQuery(path.Source, sourceMetadata).Node;
            var target = stateManager.TrackFromQuery(path.Target, targetMetadata).Node;
            var providerId = result.PathRecords[index].Relation.Id;
            var relation = stateManager.TrackRelationFromQuery(
                source,
                path.Relation,
                target,
                providerId,
                sourceMetadata,
                targetMetadata,
                relationMetadata).Relation;
            return new Model.GraphPath<TSource, TRelation, TTarget>(source, relation, target);
        }).ToArray();
    }
}

/// <summary>
/// Executes provider-neutral graph queries and returns domain objects.
/// </summary>
public interface IGraphQueryExecutor
{
    /// <summary>
    /// Executes a query asynchronously.
    /// </summary>
    ValueTask<IReadOnlyList<TNode>> ExecuteAsync<TNode>(
        GraphQueryModel query,
        CancellationToken cancellationToken = default);

    /// <summary>Executes a path projection asynchronously.</summary>
    ValueTask<IReadOnlyList<Model.GraphPath<TSource, TRelation, TTarget>>> ExecutePathsAsync<TSource, TRelation, TTarget>(
        GraphQueryModel query,
        CancellationToken cancellationToken = default)
        where TRelation : notnull;
}
