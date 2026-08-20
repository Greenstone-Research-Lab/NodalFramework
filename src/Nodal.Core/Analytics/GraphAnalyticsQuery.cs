using System.Linq.Expressions;
using Nodal.Core.Execution;
using Nodal.Core.Metadata;
using Nodal.Core.Query;

namespace Nodal.Core.Analytics;

/// <summary>Builds analytics operations over a strongly typed node and relationship selection.</summary>
public sealed class GraphAnalyticsBuilder<TNode, TRelation>
    where TRelation : notnull
{
    private readonly GraphQueryModel nodes;
    private readonly IGraphQueryExecutor? executor;
    private readonly GraphRelationMetadata relation;

    internal GraphAnalyticsBuilder(
        GraphQueryModel nodes,
        IGraphQueryExecutor? executor,
        GraphRelationMetadata relation)
    {
        this.nodes = nodes;
        this.executor = executor;
        this.relation = relation;
    }

    /// <summary>Creates an operation for any provider-neutral analytics algorithm.</summary>
    /// <remarks>Prefer named methods such as <see cref="PageRank()"/> and <see cref="Louvain()"/> in application code.</remarks>
    public GraphAnalyticsQuery<TNode, TRelation> Using(GraphAnalyticsAlgorithm algorithm)
    {
        if (GetFamily(algorithm) == GraphAnalyticsFamily.PathFinding)
        {
            throw new ArgumentException(
                "Path algorithms require ShortestPathTo so their typed target and route result are explicit.",
                nameof(algorithm));
        }
        return new GraphAnalyticsQuery<TNode, TRelation>(CreateModel(algorithm), executor, relation);
    }

    /// <summary>Creates a PageRank centrality operation.</summary>
    public GraphAnalyticsQuery<TNode, TRelation> PageRank() => Using(GraphAnalyticsAlgorithm.PageRank);

    /// <summary>Creates a PageRank operation with validated, provider-neutral settings.</summary>
    public GraphAnalyticsQuery<TNode, TRelation> PageRank(PageRankOptions options) =>
        PageRank().Configure(options);
    /// <summary>Creates an ArticleRank centrality operation.</summary>
    public GraphAnalyticsQuery<TNode, TRelation> ArticleRank() => Using(GraphAnalyticsAlgorithm.ArticleRank);
    /// <summary>Creates a betweenness-centrality operation.</summary>
    public GraphAnalyticsQuery<TNode, TRelation> Betweenness() => Using(GraphAnalyticsAlgorithm.BetweennessCentrality);
    /// <summary>Creates a closeness-centrality operation.</summary>
    public GraphAnalyticsQuery<TNode, TRelation> Closeness() => Using(GraphAnalyticsAlgorithm.ClosenessCentrality);
    /// <summary>Creates a degree-centrality operation.</summary>
    public GraphAnalyticsQuery<TNode, TRelation> Degree() => Using(GraphAnalyticsAlgorithm.DegreeCentrality);
    /// <summary>Creates an eigenvector-centrality operation.</summary>
    public GraphAnalyticsQuery<TNode, TRelation> Eigenvector() => Using(GraphAnalyticsAlgorithm.EigenvectorCentrality);
    /// <summary>Creates a harmonic-centrality operation.</summary>
    public GraphAnalyticsQuery<TNode, TRelation> Harmonic() => Using(GraphAnalyticsAlgorithm.HarmonicCentrality);
    /// <summary>Creates a HITS operation.</summary>
    public GraphAnalyticsQuery<TNode, TRelation> Hits() => Using(GraphAnalyticsAlgorithm.Hits);
    /// <summary>Creates an articulation-points operation.</summary>
    public GraphAnalyticsQuery<TNode, TRelation> ArticulationPoints() => Using(GraphAnalyticsAlgorithm.ArticulationPoints);
    /// <summary>Creates a bridges operation.</summary>
    public GraphAnalyticsQuery<TNode, TRelation> Bridges() => Using(GraphAnalyticsAlgorithm.Bridges);
    /// <summary>Creates a CELF influence-maximization operation.</summary>
    public GraphAnalyticsQuery<TNode, TRelation> Celf() => Using(GraphAnalyticsAlgorithm.CelfInfluenceMaximization);

    /// <summary>Creates a Louvain community-detection operation.</summary>
    public GraphAnalyticsQuery<TNode, TRelation> Louvain() => Using(GraphAnalyticsAlgorithm.Louvain);

    /// <summary>Creates a Louvain operation with validated, provider-neutral settings.</summary>
    public GraphAnalyticsQuery<TNode, TRelation> Louvain(LouvainOptions options) =>
        Louvain().Configure(options);
    /// <summary>Creates a Leiden community-detection operation.</summary>
    public GraphAnalyticsQuery<TNode, TRelation> Leiden() => Using(GraphAnalyticsAlgorithm.Leiden);
    /// <summary>Creates a label-propagation operation.</summary>
    public GraphAnalyticsQuery<TNode, TRelation> LabelPropagation() => Using(GraphAnalyticsAlgorithm.LabelPropagation);
    /// <summary>Creates a weakly-connected-components operation.</summary>
    public GraphAnalyticsQuery<TNode, TRelation> WeaklyConnectedComponents() => Using(GraphAnalyticsAlgorithm.WeaklyConnectedComponents);
    /// <summary>Creates a strongly-connected-components operation.</summary>
    public GraphAnalyticsQuery<TNode, TRelation> StronglyConnectedComponents() => Using(GraphAnalyticsAlgorithm.StronglyConnectedComponents);
    /// <summary>Creates a triangle-count operation.</summary>
    public GraphAnalyticsQuery<TNode, TRelation> TriangleCount() => Using(GraphAnalyticsAlgorithm.TriangleCount);
    /// <summary>Creates a local-clustering-coefficient operation.</summary>
    public GraphAnalyticsQuery<TNode, TRelation> LocalClusteringCoefficient() => Using(GraphAnalyticsAlgorithm.LocalClusteringCoefficient);
    /// <summary>Creates a K-core decomposition operation.</summary>
    public GraphAnalyticsQuery<TNode, TRelation> KCore() => Using(GraphAnalyticsAlgorithm.KCoreDecomposition);
    /// <summary>Creates a K-1 coloring operation.</summary>
    public GraphAnalyticsQuery<TNode, TRelation> K1Coloring() => Using(GraphAnalyticsAlgorithm.K1Coloring);
    /// <summary>Creates a K-means clustering operation.</summary>
    public GraphAnalyticsQuery<TNode, TRelation> KMeans() => Using(GraphAnalyticsAlgorithm.KMeans);
    /// <summary>Creates an HDBSCAN clustering operation.</summary>
    public GraphAnalyticsQuery<TNode, TRelation> Hdbscan() => Using(GraphAnalyticsAlgorithm.Hdbscan);
    /// <summary>Creates a clique-counting operation.</summary>
    public GraphAnalyticsQuery<TNode, TRelation> CliqueCounting() => Using(GraphAnalyticsAlgorithm.CliqueCounting);
    /// <summary>Creates a conductance operation.</summary>
    public GraphAnalyticsQuery<TNode, TRelation> Conductance() => Using(GraphAnalyticsAlgorithm.Conductance);
    /// <summary>Creates a modularity measurement operation.</summary>
    public GraphAnalyticsQuery<TNode, TRelation> Modularity() => Using(GraphAnalyticsAlgorithm.Modularity);
    /// <summary>Creates a modularity-optimization operation.</summary>
    public GraphAnalyticsQuery<TNode, TRelation> ModularityOptimization() => Using(GraphAnalyticsAlgorithm.ModularityOptimization);
    /// <summary>Creates an approximate maximum-k-cut operation.</summary>
    public GraphAnalyticsQuery<TNode, TRelation> ApproximateMaximumKCut() => Using(GraphAnalyticsAlgorithm.ApproximateMaximumKCut);
    /// <summary>Creates a speaker-listener label-propagation operation.</summary>
    public GraphAnalyticsQuery<TNode, TRelation> SpeakerListenerLabelPropagation() => Using(GraphAnalyticsAlgorithm.SpeakerListenerLabelPropagation);

    private GraphAnalyticsQueryModel CreateModel(GraphAnalyticsAlgorithm algorithm) => new(
        algorithm,
        GetFamily(algorithm),
        nodes,
        relation.Name,
        relation.Directed,
        "nodal");

    private static GraphAnalyticsFamily GetFamily(GraphAnalyticsAlgorithm algorithm) => algorithm switch
    {
        >= GraphAnalyticsAlgorithm.ArticleRank and <= GraphAnalyticsAlgorithm.PageRank => GraphAnalyticsFamily.Centrality,
        >= GraphAnalyticsAlgorithm.CliqueCounting and <= GraphAnalyticsAlgorithm.SpeakerListenerLabelPropagation => GraphAnalyticsFamily.CommunityDetection,
        _ => GraphAnalyticsFamily.PathFinding,
    };
}

/// <summary>Represents an immutable, executable graph analytics operation.</summary>
public sealed class GraphAnalyticsQuery<TNode, TRelation>
    where TRelation : notnull
{
    private readonly GraphAnalyticsQueryModel model;
    private readonly IGraphQueryExecutor? executor;
    private readonly GraphRelationMetadata relation;

    internal GraphAnalyticsQuery(
        GraphAnalyticsQueryModel model,
        IGraphQueryExecutor? executor,
        GraphRelationMetadata relation)
    {
        this.model = model;
        this.executor = executor;
        this.relation = relation;
    }

    /// <summary>Selects the provider-side graph projection used by server-native algorithms.</summary>
    /// <example><code>var query = context.People.Query().Analyze(context.Friendships).PageRank().OnProjection("social");</code></example>
    public GraphAnalyticsQuery<TNode, TRelation> OnProjection(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Copy(model with { ProjectionName = name });
    }

    /// <summary>Limits returned analytics rows after provider-native ranking.</summary>
    public GraphAnalyticsQuery<TNode, TRelation> Top(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        return Copy(model with { Limit = count });
    }

    /// <summary>Selects a mapped numeric relationship property as the algorithm weight.</summary>
    public GraphAnalyticsQuery<TNode, TRelation> WeightedBy<TNumber>(Expression<Func<TRelation, TNumber>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var mapped = GraphExpressionTranslator.TranslateProperty(
            selector,
            relation.Properties.ToDictionary(property => property.Key, property => property.Value.Name));
        var property = relation.Properties.Values.Single(candidate => candidate.Name == mapped);
        var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
        if (!IsNumeric(type))
        {
            throw new ArgumentException("Analytics weights must use a numeric relationship property.", nameof(selector));
        }

        return Copy(model with { RelationshipWeightProperty = mapped });
    }

    /// <summary>Adds an algorithm-specific configuration value transported separately from command text.</summary>
    public GraphAnalyticsQuery<TNode, TRelation> WithOption(string name, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var configuration = new Dictionary<string, object?>(model.EffectiveConfiguration, StringComparer.Ordinal)
        {
            [name] = value,
        };
        return Copy(model with { Configuration = configuration });
    }

    /// <summary>Applies strongly typed PageRank settings.</summary>
    public GraphAnalyticsQuery<TNode, TRelation> Configure(PageRankOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        EnsureAlgorithm(GraphAnalyticsAlgorithm.PageRank);
        if (options.DampingFactor is <= 0 or >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "DampingFactor must be between zero and one.");
        }
        ValidatePositive(options.MaximumIterations, nameof(options.MaximumIterations));
        ValidatePositive(options.Tolerance, nameof(options.Tolerance));
        ValidateConcurrency(options.Concurrency);
        return WithConfiguration(new Dictionary<string, object?>
        {
            ["dampingFactor"] = options.DampingFactor,
            ["maxIterations"] = options.MaximumIterations,
            ["tolerance"] = options.Tolerance,
            ["concurrency"] = options.Concurrency,
        });
    }

    /// <summary>Applies strongly typed Louvain settings.</summary>
    public GraphAnalyticsQuery<TNode, TRelation> Configure(LouvainOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        EnsureAlgorithm(GraphAnalyticsAlgorithm.Louvain);
        ValidatePositive(options.MaximumLevels, nameof(options.MaximumLevels));
        ValidatePositive(options.MaximumIterations, nameof(options.MaximumIterations));
        ValidatePositive(options.Tolerance, nameof(options.Tolerance));
        ValidateConcurrency(options.Concurrency);
        return WithConfiguration(new Dictionary<string, object?>
        {
            ["maxLevels"] = options.MaximumLevels,
            ["maxIterations"] = options.MaximumIterations,
            ["tolerance"] = options.Tolerance,
            ["includeIntermediateCommunities"] = options.IncludeIntermediateCommunities,
            ["concurrency"] = options.Concurrency,
        });
    }

    /// <summary>Produces the immutable model consumed by analytics providers.</summary>
    public GraphAnalyticsQueryModel ToQueryModel() => model;

    /// <summary>Executes the server-native algorithm and materializes its canonical result rows.</summary>
    public ValueTask<IReadOnlyList<GraphAnalyticsRecord<TNode>>> ToListAsync(
        CancellationToken cancellationToken = default)
    {
        if (executor is null)
        {
            throw new InvalidOperationException(
                "This analytics query is not attached to a NodalContext. Use ToQueryModel for compiler-only scenarios.");
        }

        return executor.ExecuteAnalyticsAsync<TNode>(model, cancellationToken);
    }

    private GraphAnalyticsQuery<TNode, TRelation> Copy(GraphAnalyticsQueryModel next) => new(next, executor, relation);

    private GraphAnalyticsQuery<TNode, TRelation> WithConfiguration(IReadOnlyDictionary<string, object?> configuration) =>
        Copy(model with { Configuration = configuration });

    private void EnsureAlgorithm(GraphAnalyticsAlgorithm expected)
    {
        if (model.Algorithm != expected)
        {
            throw new InvalidOperationException($"Options for '{expected}' cannot configure '{model.Algorithm}'.");
        }
    }

    private static void ValidatePositive(double value, string name)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(name, "The value must be greater than zero.");
        }
    }

    private static void ValidateConcurrency(int? concurrency)
    {
        if (concurrency is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(concurrency), "Concurrency must be greater than zero.");
        }
    }

    private static bool IsNumeric(Type type) => type == typeof(byte) || type == typeof(sbyte) ||
        type == typeof(short) || type == typeof(ushort) || type == typeof(int) || type == typeof(uint) ||
        type == typeof(long) || type == typeof(ulong) || type == typeof(float) || type == typeof(double) ||
        type == typeof(decimal);
}
