namespace Nodal.Analytics.DerivedNetworks;

/// <summary>Configures deterministic baseline analytics over a bounded canonical observation.</summary>
public sealed record DerivedNetworkAnalysisOptions
{
    /// <summary>Gets relation types included in the derived network; an empty set includes every type.</summary>
    public IReadOnlySet<string> RelationTypes { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Gets whether directed relations are treated as undirected during ranking.</summary>
    public bool TreatAsUndirected { get; init; }

    /// <summary>Gets the PageRank damping factor.</summary>
    public double DampingFactor { get; init; } = 0.85;

    /// <summary>Gets the maximum PageRank iterations.</summary>
    public int MaxIterations { get; init; } = 100;

    /// <summary>Gets the PageRank convergence tolerance.</summary>
    public double Tolerance { get; init; } = 1e-9;
}
