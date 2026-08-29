namespace Nodal.TigerGraph;

using Nodal.Core.Analytics;
using Nodal.TigerGraph.Extensions;

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

    /// <summary>Gets verified scope-level installed analytics query bindings.</summary>
    public TigerGraphAnalyticsBindingManifest? AnalyticsBindingManifest { get; init; }

    /// <summary>Gets the explicit policy for missing Nodal-managed analytics bindings.</summary>
    public TigerGraphAnalyticsProvisioningMode AnalyticsProvisioningMode { get; init; } =
        TigerGraphAnalyticsProvisioningMode.ValidateOnly;

    /// <summary>Gets the canonical Nodal analytics response-contract version.</summary>
    public string AnalyticsContractVersion { get; init; } = "1";

    /// <summary>
    /// Gets the optional installed-query extension contract for advanced TigerGraph query shapes.
    /// Configuring a manifest alone does not advertise a capability; Nodal requires a verified
    /// execution contract before the corresponding fluent query shape is enabled.
    /// </summary>
    public TigerGraphQueryExtensionManifest? QueryExtensions { get; init; }

    /// <summary>
    /// Gets query shapes that Nodal may generate and install at runtime through the explicitly
    /// supplied administrative transport. No generated extension is enabled by default.
    /// </summary>
    public IReadOnlySet<TigerGraphQueryExtensionFeature> GeneratedQueryExtensions { get; init; } =
        new HashSet<TigerGraphQueryExtensionFeature>();
}
