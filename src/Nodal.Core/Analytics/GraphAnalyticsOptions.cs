namespace Nodal.Core.Analytics;

/// <summary>Configures PageRank with provider-neutral, validated settings.</summary>
/// <param name="DampingFactor">Probability of following an outgoing relationship; must be between zero and one.</param>
/// <param name="MaximumIterations">Maximum solver iterations.</param>
/// <param name="Tolerance">Convergence tolerance.</param>
/// <param name="Concurrency">Optional provider worker count.</param>
public sealed record PageRankOptions(
    double DampingFactor = 0.85,
    int MaximumIterations = 20,
    double Tolerance = 0.0000001,
    int? Concurrency = null);

/// <summary>Configures Louvain community detection with provider-neutral, validated settings.</summary>
/// <param name="MaximumLevels">Maximum hierarchy depth.</param>
/// <param name="MaximumIterations">Maximum iterations at each level.</param>
/// <param name="Tolerance">Minimum modularity improvement required to continue.</param>
/// <param name="IncludeIntermediateCommunities">Whether each hierarchy level is returned.</param>
/// <param name="Concurrency">Optional provider worker count.</param>
public sealed record LouvainOptions(
    int MaximumLevels = 10,
    int MaximumIterations = 10,
    double Tolerance = 0.0001,
    bool IncludeIntermediateCommunities = false,
    int? Concurrency = null);
