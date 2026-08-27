using System.Text.Json;
using Nodal.Core.Migrations;

namespace Nodal.TigerGraph.Extensions;

/// <summary>Describes one installed TigerGraph query-extension deployment discovered from REST++.</summary>
public sealed record TigerGraphQueryExtensionSnapshot(
    Version Version,
    IReadOnlySet<TigerGraphQueryExtensionFeature> Features);

/// <summary>
/// Validates an installed TigerGraph query-extension deployment against the application's manifest.
/// </summary>
public sealed class TigerGraphQueryExtensionDiscovery
{
    private readonly HttpClient httpClient;
    private readonly TigerGraphOptions options;
    private readonly string graphName;
    private readonly TigerGraphQueryExtensionManifest manifest;

    /// <summary>Initializes discovery with the host-managed HTTP client and expected extension manifest.</summary>
    public TigerGraphQueryExtensionDiscovery(
        HttpClient httpClient,
        TigerGraphOptions options,
        string graphName,
        TigerGraphQueryExtensionManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(graphName);
        ArgumentNullException.ThrowIfNull(manifest);
        httpClient.BaseAddress ??= options.Endpoint;
        this.httpClient = httpClient;
        this.options = options;
        this.graphName = graphName;
        this.manifest = manifest;
    }

    /// <summary>Discovers and validates the installed extension contract before application queries execute.</summary>
    public async ValueTask<TigerGraphQueryExtensionSnapshot> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var route = $"restpp/query/{Uri.EscapeDataString(graphName)}/{Uri.EscapeDataString(manifest.DiscoveryQueryName)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, route);
        TigerGraphAuthentication.Apply(request, options);
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new NodalCapabilityNotSupportedException("TigerGraph", "NODAL-TIGERGRAPH-EXTENSION-DISCOVERY-FAILED",
                $"TigerGraph extension discovery query '{manifest.DiscoveryQueryName}' is unavailable for graph '{graphName}'.");
        }

        using var document = JsonDocument.Parse(payload);
        var result = document.RootElement.GetProperty("results")[0];
        var version = Version.Parse(result.GetProperty("nodal_extension_version").GetString()!);
        var features = result.GetProperty("nodal_extension_features").EnumerateArray()
            .Select(value => Enum.Parse<TigerGraphQueryExtensionFeature>(value.GetString()!, ignoreCase: false))
            .ToHashSet();
        if (version != manifest.Version || manifest.QueryNames.Keys.Any(feature => !features.Contains(feature)))
        {
            throw new NodalCapabilityNotSupportedException("TigerGraph", "NODAL-TIGERGRAPH-EXTENSION-CONTRACT-MISMATCH",
                $"TigerGraph extension contract does not match manifest version '{manifest.Version}'.");
        }

        return new TigerGraphQueryExtensionSnapshot(version, features);
    }
}
