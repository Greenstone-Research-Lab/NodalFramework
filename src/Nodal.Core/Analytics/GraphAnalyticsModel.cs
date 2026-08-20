using Nodal.Core.Query;

namespace Nodal.Core.Analytics;

/// <summary>Identifies a provider-neutral graph analytics operation.</summary>
public enum GraphAnalyticsAlgorithm
{
    /// <summary>ArticleRank centrality.</summary>
    ArticleRank,
    /// <summary>Articulation-point discovery.</summary>
    ArticulationPoints,
    /// <summary>Betweenness centrality.</summary>
    BetweennessCentrality,
    /// <summary>Bridge discovery.</summary>
    Bridges,
    /// <summary>Cost-effective lazy-forward influence maximization.</summary>
    CelfInfluenceMaximization,
    /// <summary>Closeness centrality.</summary>
    ClosenessCentrality,
    /// <summary>Degree centrality.</summary>
    DegreeCentrality,
    /// <summary>Eigenvector centrality.</summary>
    EigenvectorCentrality,
    /// <summary>Harmonic centrality.</summary>
    HarmonicCentrality,
    /// <summary>Hyperlink-induced topic search.</summary>
    Hits,
    /// <summary>PageRank centrality.</summary>
    PageRank,

    /// <summary>Clique counting.</summary>
    CliqueCounting,
    /// <summary>Community conductance measurement.</summary>
    Conductance,
    /// <summary>Density-based HDBSCAN clustering.</summary>
    Hdbscan,
    /// <summary>K-core decomposition.</summary>
    KCoreDecomposition,
    /// <summary>K-1 graph coloring.</summary>
    K1Coloring,
    /// <summary>K-means clustering.</summary>
    KMeans,
    /// <summary>Label-propagation community detection.</summary>
    LabelPropagation,
    /// <summary>Leiden community detection.</summary>
    Leiden,
    /// <summary>Local clustering coefficient.</summary>
    LocalClusteringCoefficient,
    /// <summary>Louvain community detection.</summary>
    Louvain,
    /// <summary>Modularity measurement.</summary>
    Modularity,
    /// <summary>Modularity optimization.</summary>
    ModularityOptimization,
    /// <summary>Strongly connected components.</summary>
    StronglyConnectedComponents,
    /// <summary>Triangle counting.</summary>
    TriangleCount,
    /// <summary>Weakly connected components.</summary>
    WeaklyConnectedComponents,
    /// <summary>Approximate maximum k-cut partitioning.</summary>
    ApproximateMaximumKCut,
    /// <summary>Speaker-listener label propagation.</summary>
    SpeakerListenerLabelPropagation,

    /// <summary>One unweighted shortest path.</summary>
    ShortestPath,
    /// <summary>All equally short unweighted paths.</summary>
    AllShortestPaths,
    /// <summary>Dijkstra weighted shortest path.</summary>
    Dijkstra,
    /// <summary>A-star weighted shortest path.</summary>
    AStar,
    /// <summary>Yen k-shortest paths.</summary>
    YenKShortestPaths,
}

/// <summary>Classifies graph algorithms by their principal result semantics.</summary>
public enum GraphAnalyticsFamily
{
    /// <summary>Produces node importance, influence, or structural criticality measurements.</summary>
    Centrality,

    /// <summary>Produces memberships, clusters, components, or cohesion measurements.</summary>
    CommunityDetection,

    /// <summary>Produces one or more routes between selected vertices.</summary>
    PathFinding,
}

/// <summary>Describes how an analytics algorithm is supplied by a database platform.</summary>
public enum GraphAnalyticsAvailability
{
    /// <summary>The database engine exposes the operation without an additional analytics component.</summary>
    Native,

    /// <summary>The operation requires an optional database extension or plugin.</summary>
    Extension,

    /// <summary>The operation requires an explicitly installed provider query.</summary>
    InstalledQuery,
}

/// <summary>Identifies the strongest verification completed for a capability.</summary>
public enum GraphCapabilityVerification
{
    /// <summary>The portable contract and validation behavior are covered by tests.</summary>
    Contract,

    /// <summary>The provider compiler and normalized response contract are covered by tests.</summary>
    Compiler,

    /// <summary>The operation is exercised against the stated live database baseline.</summary>
    Integration,
}

/// <summary>Describes one provider's support boundary for an analytics algorithm.</summary>
/// <param name="Algorithm">The provider-neutral algorithm.</param>
/// <param name="Availability">How the database supplies the algorithm.</param>
/// <param name="Verification">The strongest completed Nodal verification level.</param>
/// <param name="Requirement">The extension, installed query, or platform condition required for execution.</param>
/// <param name="SupportsWeights">Whether this configured implementation accepts a relationship weight.</param>
public sealed record GraphAlgorithmCapability(
    GraphAnalyticsAlgorithm Algorithm,
    GraphAnalyticsAvailability Availability,
    GraphCapabilityVerification Verification,
    string Requirement,
    bool SupportsWeights);

/// <summary>Describes one immutable, provider-neutral analytics request.</summary>
/// <param name="Algorithm">The requested algorithm.</param>
/// <param name="Family">The algorithm family.</param>
/// <param name="Nodes">The typed node selection used to scope returned vertices.</param>
/// <param name="RelationshipType">The mapped relationship type participating in the analysis.</param>
/// <param name="Directed">Whether relationship direction is significant.</param>
/// <param name="ProjectionName">The provider-side analytics projection or graph name.</param>
/// <param name="RelationshipWeightProperty">The optional mapped numeric relationship property.</param>
/// <param name="Limit">The optional maximum number of result rows.</param>
/// <param name="Configuration">Algorithm-specific, separately transported configuration.</param>
/// <param name="TargetNodes">The typed target selector for path-finding operations.</param>
/// <param name="MaxDepth">The optional maximum path length.</param>
public sealed record GraphAnalyticsQueryModel(
    GraphAnalyticsAlgorithm Algorithm,
    GraphAnalyticsFamily Family,
    GraphQueryModel Nodes,
    string RelationshipType,
    bool Directed,
    string ProjectionName,
    string? RelationshipWeightProperty = null,
    int? Limit = null,
    IReadOnlyDictionary<string, object?>? Configuration = null,
    GraphQueryModel? TargetNodes = null,
    int? MaxDepth = null)
{
    /// <summary>Gets normalized algorithm configuration.</summary>
    public IReadOnlyDictionary<string, object?> EffectiveConfiguration => Configuration ??
        new Dictionary<string, object?>();
}

/// <summary>Represents one analytics row while preserving provider-specific measurements.</summary>
/// <typeparam name="TNode">The mapped node type associated with the result, when applicable.</typeparam>
/// <param name="Node">The materialized node, or <see langword="null"/> for edge- or graph-level results.</param>
/// <param name="Metrics">Normalized measurements returned by the algorithm.</param>
public sealed record GraphAnalyticsRecord<TNode>(TNode? Node, IReadOnlyDictionary<string, object?> Metrics)
{
    /// <summary>Gets a numeric score when the algorithm exposes one.</summary>
    public double? Score => TryGetDouble("score");

    /// <summary>Gets a community identifier when the algorithm assigns one.</summary>
    public long? CommunityId => TryGetInt64("communityId");

    private double? TryGetDouble(string name) => Metrics.TryGetValue(name, out var value) && value is not null
        ? Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture)
        : null;

    private long? TryGetInt64(string name) => Metrics.TryGetValue(name, out var value) && value is not null
        ? Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture)
        : null;
}

/// <summary>Declares the analytics algorithms and execution features exposed by a provider.</summary>
public sealed record GraphAnalyticsCapabilities
{
    /// <summary>Gets the database platform represented by this capability set.</summary>
    public string ProviderName { get; init; } = "Unknown";

    /// <summary>Gets the exact database version used by the repository's live QA baseline.</summary>
    public string? TestedProviderVersion { get; init; }

    /// <summary>Gets the client or transport version used by this provider package.</summary>
    public string? ClientVersion { get; init; }

    /// <summary>Gets the supported algorithms.</summary>
    public required IReadOnlySet<GraphAnalyticsAlgorithm> Algorithms { get; init; }

    /// <summary>Gets whether weighted relationship properties are accepted.</summary>
    public bool SupportsWeightedRelationships { get; init; }

    /// <summary>Gets whether analytics projections can be managed by the provider.</summary>
    public bool SupportsProjectionManagement { get; init; }

    /// <summary>Gets algorithm-specific requirements and verification levels.</summary>
    public IReadOnlyDictionary<GraphAnalyticsAlgorithm, GraphAlgorithmCapability> AlgorithmDetails { get; init; } =
        new Dictionary<GraphAnalyticsAlgorithm, GraphAlgorithmCapability>();

    /// <summary>Determines whether an algorithm can be executed by this provider.</summary>
    public bool Supports(GraphAnalyticsAlgorithm algorithm) => Algorithms.Contains(algorithm);

    /// <summary>Gets detailed support metadata for an advertised algorithm.</summary>
    public GraphAlgorithmCapability GetDetails(GraphAnalyticsAlgorithm algorithm) =>
        AlgorithmDetails.TryGetValue(algorithm, out var details)
            ? details
            : throw new NotSupportedException(
                $"Provider '{ProviderName}' does not advertise analytics algorithm '{algorithm}'.");
}
