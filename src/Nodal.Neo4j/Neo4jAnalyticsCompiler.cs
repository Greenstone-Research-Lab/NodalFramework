using System.Text;
using Nodal.Core.Analytics;
using Nodal.Core.Providers;
using Nodal.Core.Query;

namespace Nodal.Neo4j;

/// <summary>Compiles graph analytics operations into parameterized Neo4j GDS procedure calls.</summary>
public sealed class Neo4jAnalyticsCompiler : IGraphAnalyticsCompiler
{
    /// <inheritdoc />
    public GraphCommand Compile(GraphAnalyticsQueryModel query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Family == GraphAnalyticsFamily.PathFinding)
        {
            return CompilePath(query);
        }
        var shape = GetShape(query.Algorithm);
        var configuration = new Dictionary<string, object?>(query.EffectiveConfiguration, StringComparer.Ordinal);
        if (query.RelationshipWeightProperty is not null)
        {
            configuration["relationshipWeightProperty"] = query.RelationshipWeightProperty;
        }

        var parameters = query.Nodes.Parameters.ToDictionary(item => item.Name, item => item.Value);
        parameters["nodal_projection"] = query.ProjectionName;
        parameters["nodal_configuration"] = configuration;

        var builder = new StringBuilder("CALL ")
            .Append(shape.Procedure)
            .Append(".stream($nodal_projection, $nodal_configuration) YIELD ")
            .Append(string.Join(", ", shape.Yields));

        if (shape.NodeId is not null)
        {
            builder.Append(" WITH gds.util.asNode(")
                .Append(shape.NodeId)
                .Append(") AS nodal_node, ")
                .Append(RenderMetrics(shape.Metrics))
                .Append(" AS nodal_metrics");
            var predicate = query.Nodes.Predicate;
            if (predicate is not null)
            {
                builder.Append(" WHERE ").Append(RenderPredicate(predicate, "nodal_node"));
            }
            builder.Append(" RETURN nodal_node, nodal_metrics");
        }
        else
        {
            builder.Append(" RETURN null AS nodal_node, ")
                .Append(RenderMetrics(shape.Metrics))
                .Append(" AS nodal_metrics");
        }

        if (shape.Metrics.Contains("score", StringComparer.Ordinal))
        {
            builder.Append(" ORDER BY nodal_metrics.score DESC");
        }
        if (query.Limit is not null)
        {
            builder.Append(" LIMIT ").Append(query.Limit.Value);
        }

        return new GraphCommand(builder.ToString(), parameters);
    }

    private static GraphCommand CompilePath(GraphAnalyticsQueryModel query)
    {
        var target = query.TargetNodes ?? throw new InvalidOperationException("Path finding requires a target selector.");
        var parameters = query.Nodes.Parameters.Concat(target.Parameters)
            .ToDictionary(item => item.Name, item => item.Value);
        var filters = new List<string>();
        if (query.Nodes.Predicate is not null)
        {
            filters.Add(RenderPredicate(query.Nodes.Predicate, "nodal_source"));
        }
        if (target.Predicate is not null)
        {
            filters.Add(RenderPredicate(target.Predicate, "nodal_target"));
        }
        var match = $"MATCH (nodal_source:`{Escape(query.Nodes.NodeType)}`), (nodal_target:`{Escape(target.NodeType)}`)" +
            (filters.Count == 0 ? string.Empty : $" WHERE {string.Join(" AND ", filters)}");
        if (query.Algorithm is GraphAnalyticsAlgorithm.ShortestPath or GraphAnalyticsAlgorithm.AllShortestPaths)
        {
            var depth = query.MaxDepth is null ? "*" : $"*1..{query.MaxDepth.Value}";
            var relation = $"[:`{Escape(query.RelationshipType)}`{depth}]";
            var pattern = query.Directed
                ? $"(nodal_source)-{relation}->(nodal_target)"
                : $"(nodal_source)-{relation}-(nodal_target)";
            var function = query.Algorithm == GraphAnalyticsAlgorithm.ShortestPath
                ? "shortestPath"
                : "allShortestPaths";
            return new GraphCommand(
                $"{match} MATCH nodal_path = {function}({pattern}) RETURN nodal_path, toFloat(length(nodal_path)) AS nodal_total_cost",
                parameters);
        }

        parameters["nodal_projection"] = query.ProjectionName;
        var configuration = new Dictionary<string, object?>(query.EffectiveConfiguration)
        {
            ["sourceNode"] = null,
            ["targetNode"] = null,
        };
        if (query.RelationshipWeightProperty is not null)
        {
            configuration["relationshipWeightProperty"] = query.RelationshipWeightProperty;
        }
        parameters["nodal_configuration"] = configuration;
        var procedure = query.Algorithm switch
        {
            GraphAnalyticsAlgorithm.Dijkstra => "gds.shortestPath.dijkstra",
            GraphAnalyticsAlgorithm.AStar => "gds.shortestPath.astar",
            GraphAnalyticsAlgorithm.YenKShortestPaths => "gds.shortestPath.yens",
            _ => throw new NotSupportedException($"Neo4j path algorithm '{query.Algorithm}' is not supported."),
        };
        var configExpression = "$nodal_configuration + {sourceNode: nodal_source, targetNode: nodal_target}";
        return new GraphCommand(
            $"{match} CALL {procedure}.stream($nodal_projection, {configExpression}) " +
            "YIELD totalCost, path RETURN path AS nodal_path, totalCost AS nodal_total_cost",
            parameters);
    }

    private static string RenderMetrics(IReadOnlyList<string> metrics) =>
        $"{{ {string.Join(", ", metrics.Select(metric => $"{metric}: {metric}"))} }}";

    private static string RenderPredicate(GraphPredicate predicate, string alias) => predicate switch
    {
        GraphComparisonPredicate comparison =>
            $"{alias}.`{Escape(comparison.PropertyName)}` {RenderOperator(comparison.Operator)} ${comparison.ParameterName}",
        GraphLogicalPredicate logical =>
            $"({RenderPredicate(logical.Left, alias)} {RenderLogical(logical.Operator)} {RenderPredicate(logical.Right, alias)})",
        GraphNotPredicate not => $"NOT ({RenderPredicate(not.Operand, alias)})",
        GraphNullPredicate nullCheck =>
            $"{alias}.`{Escape(nullCheck.PropertyName)}` IS {(nullCheck.IsNull ? string.Empty : "NOT ")}NULL",
        GraphStringPredicate text => text.Operator switch
        {
            GraphStringOperator.StartsWith => $"{alias}.`{Escape(text.PropertyName)}` STARTS WITH ${text.ParameterName}",
            GraphStringOperator.EndsWith => $"{alias}.`{Escape(text.PropertyName)}` ENDS WITH ${text.ParameterName}",
            GraphStringOperator.Contains => $"{alias}.`{Escape(text.PropertyName)}` CONTAINS ${text.ParameterName}",
            _ => throw new ArgumentOutOfRangeException(nameof(predicate)),
        },
        GraphInPredicate membership =>
            $"{alias}.`{Escape(membership.PropertyName)}` {(membership.Negated ? "NOT " : string.Empty)}IN ${membership.ParameterName}",
        _ => throw new NotSupportedException($"Predicate '{predicate.GetType().Name}' is not supported by Neo4j analytics."),
    };

    private static string RenderOperator(GraphComparisonOperator value) => value switch
    {
        GraphComparisonOperator.Equal => "=",
        GraphComparisonOperator.NotEqual => "<>",
        GraphComparisonOperator.GreaterThan => ">",
        GraphComparisonOperator.GreaterThanOrEqual => ">=",
        GraphComparisonOperator.LessThan => "<",
        GraphComparisonOperator.LessThanOrEqual => "<=",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static string RenderLogical(GraphLogicalOperator value) => value switch
    {
        GraphLogicalOperator.And => "AND",
        GraphLogicalOperator.Or => "OR",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static string Escape(string identifier) => identifier.Replace("`", "``", StringComparison.Ordinal);

    private static AlgorithmShape GetShape(GraphAnalyticsAlgorithm algorithm) => algorithm switch
    {
        GraphAnalyticsAlgorithm.ArticleRank => Score("gds.articleRank"),
        GraphAnalyticsAlgorithm.ArticulationPoints => Node("gds.articulationPoints", "resultingComponents"),
        GraphAnalyticsAlgorithm.BetweennessCentrality => Score("gds.betweenness"),
        GraphAnalyticsAlgorithm.Bridges => NoNode("gds.bridges", "from", "to", "remainingSizes"),
        GraphAnalyticsAlgorithm.CelfInfluenceMaximization => Node("gds.celf", "spread"),
        GraphAnalyticsAlgorithm.ClosenessCentrality => Score("gds.closeness"),
        GraphAnalyticsAlgorithm.DegreeCentrality => Score("gds.degree"),
        GraphAnalyticsAlgorithm.EigenvectorCentrality => Score("gds.eigenvector"),
        GraphAnalyticsAlgorithm.HarmonicCentrality => Score("gds.closeness.harmonic"),
        GraphAnalyticsAlgorithm.Hits => Node("gds.hits", "values"),
        GraphAnalyticsAlgorithm.PageRank => Score("gds.pageRank"),
        GraphAnalyticsAlgorithm.CliqueCounting => Node("gds.cliqueCount", "cliqueCount", "maxCliqueSize"),
        GraphAnalyticsAlgorithm.Conductance => NoNode("gds.conductance", "community", "conductance"),
        GraphAnalyticsAlgorithm.Hdbscan => Node("gds.hdbscan", "clusterId", "probability", "outlierScore"),
        GraphAnalyticsAlgorithm.KCoreDecomposition => Node("gds.kcore", "coreValue"),
        GraphAnalyticsAlgorithm.K1Coloring => Node("gds.k1coloring", "color"),
        GraphAnalyticsAlgorithm.KMeans => Node("gds.kmeans", "communityId", "distanceFromCentroid"),
        GraphAnalyticsAlgorithm.LabelPropagation => Node("gds.labelPropagation", "communityId"),
        GraphAnalyticsAlgorithm.Leiden => Node("gds.leiden", "communityId", "intermediateCommunityIds"),
        GraphAnalyticsAlgorithm.LocalClusteringCoefficient => Node("gds.localClusteringCoefficient", "localClusteringCoefficient"),
        GraphAnalyticsAlgorithm.Louvain => Node("gds.louvain", "communityId", "intermediateCommunityIds"),
        GraphAnalyticsAlgorithm.Modularity => NoNode("gds.modularity", "communityId", "modularity"),
        GraphAnalyticsAlgorithm.ModularityOptimization => Node("gds.modularityOptimization", "communityId"),
        GraphAnalyticsAlgorithm.StronglyConnectedComponents => Node("gds.scc", "componentId"),
        GraphAnalyticsAlgorithm.TriangleCount => Node("gds.triangleCount", "triangleCount"),
        GraphAnalyticsAlgorithm.WeaklyConnectedComponents => Node("gds.wcc", "componentId"),
        GraphAnalyticsAlgorithm.ApproximateMaximumKCut => Node("gds.maxkcut", "communityId"),
        GraphAnalyticsAlgorithm.SpeakerListenerLabelPropagation => Node("gds.sllpa", "values"),
        _ => throw new NotSupportedException(
            $"Algorithm '{algorithm}' is not a projection-wide Neo4j analytics operation."),
    };

    private static AlgorithmShape Score(string procedure) => Node(procedure, "score");
    private static AlgorithmShape Node(string procedure, params string[] metrics) =>
        new(procedure, ["nodeId", .. metrics], "nodeId", metrics);
    private static AlgorithmShape NoNode(string procedure, params string[] metrics) =>
        new(procedure, metrics, null, metrics);

    private sealed record AlgorithmShape(
        string Procedure,
        IReadOnlyList<string> Yields,
        string? NodeId,
        IReadOnlyList<string> Metrics);
}
