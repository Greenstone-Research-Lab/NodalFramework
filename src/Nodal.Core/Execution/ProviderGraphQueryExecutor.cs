using Nodal.Core.Analytics;
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
        ValidateCapabilities(query);
        var command = provider.QueryCompiler.Compile(query);
        var result = await provider.CommandExecutor.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
        var materialized = provider.ResultMaterializer.Materialize<TNode>(result);
        if (query.TrackingBehavior == GraphTrackingBehavior.NoTracking)
        {
            return materialized;
        }
        var metadata = modelAccessor().GetNode<TNode>();
        return materialized.Select(node => stateManager.TrackFromQuery(node, metadata).Node).ToArray();
    }

    public async ValueTask<IReadOnlyList<Model.GraphPath<TSource, TRelation, TTarget>>> ExecutePathsAsync<TSource, TRelation, TTarget>(
        GraphQueryModel query,
        CancellationToken cancellationToken)
        where TRelation : notnull
    {
        ValidateCapabilities(query);
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

    public async ValueTask<GraphQueryResult> ExecuteSubgraphAsync(
        GraphQueryModel query,
        CancellationToken cancellationToken)
    {
        ValidateCapabilities(query);
        var command = provider.QueryCompiler.Compile(query);
        return await provider.CommandExecutor.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<GraphResultRow>> ExecuteRowsAsync(
        GraphQueryModel query,
        CancellationToken cancellationToken)
    {
        ValidateCapabilities(query);
        var command = provider.QueryCompiler.Compile(query);
        var result = await provider.CommandExecutor.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
        return result.ResultRows;
    }

    public async ValueTask<int> ExecuteCountAsync(GraphQueryModel query, CancellationToken cancellationToken)
    {
        ValidateCapabilities(query);
        var command = provider.QueryCompiler.Compile(query);
        var result = await provider.CommandExecutor.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
        if (!result.ScalarValues.TryGetValue("nodal_count", out var value))
        {
            throw new InvalidOperationException("The provider did not return the expected 'nodal_count' scalar.");
        }

        return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private void ValidateCapabilities(GraphQueryModel query)
    {
        if (provider is IGraphQueryCapabilityProvider capabilityProvider)
        {
            GraphQueryPreflight.Validate(query, capabilityProvider.QueryCapabilities);
        }
    }

    public async ValueTask<IReadOnlyList<GraphAnalyticsRecord<TNode>>> ExecuteAnalyticsAsync<TNode>(
        GraphAnalyticsQueryModel query,
        CancellationToken cancellationToken)
    {
        if (provider is not IGraphAnalyticsProvider analyticsProvider)
        {
            throw new NotSupportedException($"Graph provider '{provider.GetType().Name}' does not support analytics.");
        }
        if (!analyticsProvider.AnalyticsCapabilities.Supports(query.Algorithm))
        {
            throw new NotSupportedException(
                $"Graph provider '{provider.GetType().Name}' does not support analytics algorithm '{query.Algorithm}'.");
        }
        var capabilities = analyticsProvider.AnalyticsCapabilities;
        var algorithmSupportsWeights = !capabilities.AlgorithmDetails.TryGetValue(query.Algorithm, out var details) ||
            details.SupportsWeights;
        if (query.RelationshipWeightProperty is not null &&
            (!capabilities.SupportsWeightedRelationships || !algorithmSupportsWeights))
        {
            throw new NotSupportedException(
                $"Graph provider '{provider.GetType().Name}' does not support weighted analytics relationships.");
        }

        var command = analyticsProvider.AnalyticsCompiler.Compile(query);
        var result = await provider.CommandExecutor.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
        var metadata = modelAccessor().GetNode<TNode>();
        return result.ResultRows.Select(row =>
        {
            if (row.Node is null)
            {
                return new GraphAnalyticsRecord<TNode>(default, row.Values);
            }
            var materialized = provider.ResultMaterializer.Materialize<TNode>(new GraphQueryResult([row.Node])).Single();
            var node = query.Nodes.TrackingBehavior == GraphTrackingBehavior.NoTracking
                ? materialized
                : stateManager.TrackFromQuery(materialized, metadata).Node;
            return new GraphAnalyticsRecord<TNode>(node, row.Values);
        }).ToArray();
    }

    public async ValueTask<IReadOnlyList<Model.GraphRoute<TNode, TRelation>>> ExecuteRoutesAsync<TNode, TRelation>(
        GraphAnalyticsQueryModel query,
        CancellationToken cancellationToken)
        where TRelation : notnull
    {
        if (provider is not IGraphAnalyticsProvider analyticsProvider ||
            !analyticsProvider.AnalyticsCapabilities.Supports(query.Algorithm))
        {
            throw new NotSupportedException(
                $"Graph provider '{provider.GetType().Name}' does not support path algorithm '{query.Algorithm}'.");
        }
        var capabilities = analyticsProvider.AnalyticsCapabilities;
        if ((query.Algorithm is GraphAnalyticsAlgorithm.Dijkstra or GraphAnalyticsAlgorithm.AStar or
             GraphAnalyticsAlgorithm.YenKShortestPaths) && query.RelationshipWeightProperty is null)
        {
            throw new InvalidOperationException(
                $"Path algorithm '{query.Algorithm}' requires a numeric relationship weight selector.");
        }
        if (query.Algorithm == GraphAnalyticsAlgorithm.AStar &&
            (!query.EffectiveConfiguration.ContainsKey("latitudeProperty") ||
             !query.EffectiveConfiguration.ContainsKey("longitudeProperty")))
        {
            throw new InvalidOperationException("A-star requires typed latitude and longitude node selectors.");
        }
        var algorithmSupportsWeights = !capabilities.AlgorithmDetails.TryGetValue(query.Algorithm, out var details) ||
            details.SupportsWeights;
        if (query.RelationshipWeightProperty is not null &&
            (!capabilities.SupportsWeightedRelationships || !algorithmSupportsWeights))
        {
            throw new NotSupportedException(
                $"Graph provider '{provider.GetType().Name}' does not support weights for path algorithm '{query.Algorithm}'.");
        }
        var command = analyticsProvider.AnalyticsCompiler.Compile(query);
        var result = await provider.CommandExecutor.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
        var metadata = modelAccessor().GetNode<TNode>();
        var relationMetadata = modelAccessor().GetRelation<TNode, TRelation, TNode>();
        return result.RouteRecords.Select(route =>
        {
            var nodes = route.Nodes.Select(record =>
            {
                var value = provider.ResultMaterializer.Materialize<TNode>(new GraphQueryResult([record])).Single();
                return query.Nodes.TrackingBehavior == GraphTrackingBehavior.NoTracking
                    ? value
                    : stateManager.TrackFromQuery(value, metadata).Node;
            }).ToArray();
            var relations = route.Relations.Select((record, index) =>
            {
                var normalized = new GraphPathRecord(route.Nodes[index], record, route.Nodes[index + 1]);
                var value = provider.ResultMaterializer
                    .MaterializePaths<TNode, TRelation, TNode>(new GraphQueryResult([], Paths: [normalized]))
                    .Single().Relation;
                return query.Nodes.TrackingBehavior == GraphTrackingBehavior.NoTracking
                    ? value
                    : stateManager.TrackRelationFromQuery(
                        nodes[index], value, nodes[index + 1], record.Id,
                        metadata, metadata, relationMetadata).Relation;
            }).ToArray();
            return new Model.GraphRoute<TNode, TRelation>(nodes, relations, route.TotalCost);
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

    /// <summary>Executes a normalized subgraph projection.</summary>
    ValueTask<GraphQueryResult> ExecuteSubgraphAsync(
        GraphQueryModel query,
        CancellationToken cancellationToken = default);

    /// <summary>Executes a provider-side scalar or aggregate row projection.</summary>
    async ValueTask<IReadOnlyList<GraphResultRow>> ExecuteRowsAsync(
        GraphQueryModel query,
        CancellationToken cancellationToken = default)
    {
        var result = await ExecuteSubgraphAsync(query, cancellationToken).ConfigureAwait(false);
        return result.ResultRows;
    }

    /// <summary>Executes a server-side count aggregate.</summary>
    ValueTask<int> ExecuteCountAsync(GraphQueryModel query, CancellationToken cancellationToken = default);

    /// <summary>Executes a provider-native analytics operation.</summary>
    ValueTask<IReadOnlyList<GraphAnalyticsRecord<TNode>>> ExecuteAnalyticsAsync<TNode>(
        GraphAnalyticsQueryModel query,
        CancellationToken cancellationToken = default);

    /// <summary>Executes a provider-native path-finding operation.</summary>
    ValueTask<IReadOnlyList<Model.GraphRoute<TNode, TRelation>>> ExecuteRoutesAsync<TNode, TRelation>(
        GraphAnalyticsQueryModel query,
        CancellationToken cancellationToken = default)
        where TRelation : notnull;
}
