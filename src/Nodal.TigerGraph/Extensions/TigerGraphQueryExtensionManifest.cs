using System.Text.RegularExpressions;
using Nodal.Core.Migrations;

namespace Nodal.TigerGraph.Extensions;

/// <summary>
/// Identifies an optional TigerGraph installed-query capability that is not
/// available through Nodal's interpreted GSQL route.
/// </summary>
public enum TigerGraphQueryExtensionFeature
{
    /// <summary>Enables correlated <c>WhereExists</c> and <c>WhereNotExists</c> query shapes.</summary>
    CorrelatedExistence,

    /// <summary>Enables additional named graph patterns in a single query.</summary>
    MultiplePatterns,

    /// <summary>Enables compatible node-query union operations.</summary>
    SetOperations,

    /// <summary>Enables optional traversal semantics.</summary>
    OptionalTraversal,
}

/// <summary>
/// Declares the installed GSQL query names and semantic version for an explicit
/// TigerGraph query-extension deployment.
/// </summary>
/// <remarks>
/// Declaring this manifest does not silently enable a portable capability. The
/// provider validates the feature contract before it can advertise or execute
/// an extension-backed query shape.
/// </remarks>
/// <example>
/// <code>
/// var extensions = new TigerGraphQueryExtensionManifest(
///     new Version(1, 0, 0),
///     new Dictionary&lt;TigerGraphQueryExtensionFeature, string&gt;
///     {
///         [TigerGraphQueryExtensionFeature.CorrelatedExistence] = "nodal_exists_v1"
///     });
/// </code>
/// </example>
public sealed partial class TigerGraphQueryExtensionManifest
{
    private readonly Dictionary<TigerGraphQueryExtensionFeature, string> queryNames;

    /// <summary>Initializes a manifest with a semantic version and installed query names.</summary>
    /// <param name="version">The extension contract version deployed to TigerGraph.</param>
    /// <param name="queryNames">The installed query name for each declared feature.</param>
    /// <param name="discoveryQueryName">The installed health query that returns extension version and features.</param>
    public TigerGraphQueryExtensionManifest(
        Version version,
        IReadOnlyDictionary<TigerGraphQueryExtensionFeature, string> queryNames,
        string discoveryQueryName = "nodal_extension_capabilities")
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(queryNames);
        this.queryNames = queryNames.ToDictionary(
            item => item.Key,
            item => ValidateQueryName(item.Value),
            EqualityComparer<TigerGraphQueryExtensionFeature>.Default);
        Version = version;
        DiscoveryQueryName = ValidateQueryName(discoveryQueryName);
    }

    /// <summary>Gets the extension contract version expected by the application.</summary>
    public Version Version { get; }

    /// <summary>Gets the installed query name used to discover the deployed extension contract.</summary>
    public string DiscoveryQueryName { get; }

    /// <summary>Gets the configured installed query names by feature.</summary>
    public IReadOnlyDictionary<TigerGraphQueryExtensionFeature, string> QueryNames => queryNames;

    /// <summary>Determines whether an installed query is explicitly configured for a feature.</summary>
    public bool Supports(TigerGraphQueryExtensionFeature feature) => queryNames.ContainsKey(feature);

    /// <summary>Gets a configured query name or throws a precise capability error before transport.</summary>
    public string GetRequiredQueryName(TigerGraphQueryExtensionFeature feature, string providerVersion)
    {
        if (queryNames.TryGetValue(feature, out var queryName))
        {
            return queryName;
        }

        throw new NodalCapabilityNotSupportedException(
            "TigerGraph",
            "NODAL-TIGERGRAPH-EXTENSION-NOT-CONFIGURED",
            $"TigerGraph extension feature '{feature}' is not configured for provider version '{providerVersion}'.");
    }

    private static string ValidateQueryName(string queryName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queryName);
        return QueryNamePattern().IsMatch(queryName)
            ? queryName
            : throw new ArgumentException("The installed query name is not a valid TigerGraph identifier.", nameof(queryName));
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex QueryNamePattern();
}
