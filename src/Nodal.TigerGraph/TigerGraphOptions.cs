namespace Nodal.TigerGraph;

using Nodal.Core.Analytics;

/// <summary>
/// Configures TigerGraph HTTP and GSQL access for a Nodal provider.
/// </summary>
public sealed class TigerGraphOptions
{
    /// <summary>Gets or sets the TigerGraph server base address.</summary>
    public required Uri Endpoint { get; init; }

    /// <summary>Gets or sets the optional GSQL user name used for Basic authentication.</summary>
    public string? Username { get; init; }

    /// <summary>Gets or sets the optional GSQL password used for Basic authentication.</summary>
    public string? Password { get; init; }

    /// <summary>Gets or sets the preferred REST++ bearer access token.</summary>
    public string? AccessToken { get; init; }

    /// <summary>
    /// Gets the installed GSQL query name for each available analytics algorithm.
    /// No algorithm is advertised until its query endpoint is explicitly configured.
    /// </summary>
    public IReadOnlyDictionary<GraphAnalyticsAlgorithm, string> AnalyticsQueries { get; init; } =
        new Dictionary<GraphAnalyticsAlgorithm, string>();

    /// <summary>
    /// Gets the configured installed queries whose declared contract accepts a relationship weight property.
    /// Algorithms are unweighted unless explicitly included.
    /// </summary>
    public IReadOnlySet<GraphAnalyticsAlgorithm> WeightedAnalyticsAlgorithms { get; init; } =
        new HashSet<GraphAnalyticsAlgorithm>();
}
