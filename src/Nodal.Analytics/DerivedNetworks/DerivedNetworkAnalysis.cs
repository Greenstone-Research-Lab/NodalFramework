using Nodal.Analytics.Observations;

namespace Nodal.Analytics.DerivedNetworks;

/// <summary>Contains baseline metrics for one node in a bounded derived network.</summary>
/// <param name="Node">Canonical node identity.</param>
/// <param name="InDegree">Incoming relation count.</param>
/// <param name="OutDegree">Outgoing relation count.</param>
/// <param name="Degree">Total incident relation count.</param>
/// <param name="PageRank">Normalized PageRank score.</param>
/// <param name="WeakComponentId">Stable weakly connected component identifier.</param>
public sealed record DerivedNodeMetrics(
    GraphObservationNodeIdentity Node,
    int InDegree,
    int OutDegree,
    int Degree,
    double PageRank,
    int WeakComponentId);

/// <summary>Contains deterministic analytics evidence for one bounded observation.</summary>
/// <param name="Nodes">Metrics in source observation order.</param>
/// <param name="RelationCount">Included relation count.</param>
/// <param name="Iterations">PageRank iterations executed.</param>
/// <param name="Converged">Whether PageRank reached the configured tolerance.</param>
public sealed record DerivedNetworkAnalysis(
    IReadOnlyList<DerivedNodeMetrics> Nodes,
    int RelationCount,
    int Iterations,
    bool Converged);
