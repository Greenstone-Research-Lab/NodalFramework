using System.Linq.Expressions;
using Nodal.Core.Execution;
using Nodal.Core.Metadata;
using Nodal.Core.Model;

namespace Nodal.Core.Analytics;

/// <summary>Builds an immutable strongly typed source-to-target path-finding operation.</summary>
public sealed class GraphShortestPathQuery<TNode, TRelation>
    where TRelation : notnull
{
    private readonly GraphAnalyticsQueryModel model;
    private readonly IGraphQueryExecutor? executor;
    private readonly GraphRelationMetadata relation;
    private readonly IReadOnlyDictionary<string, string> nodeProperties;

    internal GraphShortestPathQuery(
        GraphAnalyticsQueryModel model,
        IGraphQueryExecutor? executor,
        GraphRelationMetadata relation,
        IReadOnlyDictionary<string, string>? nodeProperties = null)
    {
        this.model = model;
        this.executor = executor;
        this.relation = relation;
        this.nodeProperties = nodeProperties ?? new Dictionary<string, string>();
    }

    /// <summary>Limits path expansion to a positive number of relationships.</summary>
    public GraphShortestPathQuery<TNode, TRelation> MaxDepth(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        return Copy(model with { MaxDepth = value });
    }

    /// <summary>Selects Dijkstra weighted shortest-path execution.</summary>
    public GraphShortestPathQuery<TNode, TRelation> Dijkstra() =>
        Copy(model with { Algorithm = GraphAnalyticsAlgorithm.Dijkstra });

    /// <summary>Selects A-star and maps the node coordinates used by the admissible heuristic.</summary>
    public GraphShortestPathQuery<TNode, TRelation> AStar<TLatitude, TLongitude>(
        Expression<Func<TNode, TLatitude>> latitude,
        Expression<Func<TNode, TLongitude>> longitude)
    {
        ArgumentNullException.ThrowIfNull(latitude);
        ArgumentNullException.ThrowIfNull(longitude);
        var latitudeProperty = Query.GraphExpressionTranslator.TranslateProperty(latitude, nodeProperties);
        var longitudeProperty = Query.GraphExpressionTranslator.TranslateProperty(longitude, nodeProperties);
        var configuration = MergeOption("latitudeProperty", latitudeProperty);
        configuration["longitudeProperty"] = longitudeProperty;
        return Copy(model with { Algorithm = GraphAnalyticsAlgorithm.AStar, Configuration = configuration });
    }

    /// <summary>Selects Yen k-shortest-path execution.</summary>
    public GraphShortestPathQuery<TNode, TRelation> Yen(int pathCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pathCount);
        return Copy(model with
        {
            Algorithm = GraphAnalyticsAlgorithm.YenKShortestPaths,
            Configuration = MergeOption("k", pathCount),
        });
    }

    /// <summary>Selects a mapped numeric relationship property as path cost.</summary>
    public GraphShortestPathQuery<TNode, TRelation> WeightedBy<TNumber>(
        Expression<Func<TRelation, TNumber>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var mappings = relation.Properties.ToDictionary(item => item.Key, item => item.Value.Name);
        var propertyName = Query.GraphExpressionTranslator.TranslateProperty(selector, mappings);
        var property = relation.Properties.Values.Single(item => item.Name == propertyName);
        var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
        if (type != typeof(byte) && type != typeof(sbyte) && type != typeof(short) && type != typeof(ushort) &&
            type != typeof(int) && type != typeof(uint) && type != typeof(long) && type != typeof(ulong) &&
            type != typeof(float) && type != typeof(double) && type != typeof(decimal))
        {
            throw new ArgumentException("Path weights must use a numeric relationship property.", nameof(selector));
        }
        return Copy(model with { RelationshipWeightProperty = propertyName });
    }

    /// <summary>Produces the immutable provider-neutral path-finding model.</summary>
    public GraphAnalyticsQueryModel ToQueryModel() => model;

    /// <summary>Executes and returns every route produced by the selected algorithm.</summary>
    public ValueTask<IReadOnlyList<GraphRoute<TNode, TRelation>>> ToListAsync(
        CancellationToken cancellationToken = default) => executor is null
        ? throw new InvalidOperationException("This path query is not attached to a NodalContext.")
        : executor.ExecuteRoutesAsync<TNode, TRelation>(model, cancellationToken);

    /// <summary>Executes the operation and requires exactly one route.</summary>
    public async ValueTask<GraphRoute<TNode, TRelation>> SingleAsync(CancellationToken cancellationToken = default) =>
        (await ToListAsync(cancellationToken).ConfigureAwait(false)).Single();

    private Dictionary<string, object?> MergeOption(string name, object value)
    {
        var options = new Dictionary<string, object?>(model.EffectiveConfiguration) { [name] = value };
        return options;
    }

    private GraphShortestPathQuery<TNode, TRelation> Copy(GraphAnalyticsQueryModel next) =>
        new(next, executor, relation, nodeProperties);
}
