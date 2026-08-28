using Nodal.TigerGraph.Extensions;

namespace Nodal.TigerGraph;

/// <summary>
/// Creates TigerGraph providers after validating any explicitly configured
/// installed-query extension contract.
/// </summary>
public static class TigerGraphProviderFactory
{
    /// <summary>
    /// Creates a provider and validates its installed-query manifest before returning it.
    /// </summary>
    /// <param name="httpClient">The host-managed HTTP client used for REST++ requests.</param>
    /// <param name="options">TigerGraph connection and extension options.</param>
    /// <param name="graphName">The target TigerGraph graph.</param>
    /// <param name="cancellationToken">Stops extension discovery while the application is starting.</param>
    /// <returns>A provider whose configured extension manifest has been verified.</returns>
    /// <example>
    /// <code>
    /// var provider = await TigerGraphProviderFactory.CreateAsync(
    ///     httpClient,
    ///     options,
    ///     "SocialGraph",
    ///     cancellationToken);
    /// </code>
    /// </example>
    public static async ValueTask<TigerGraphProvider> CreateAsync(
        HttpClient httpClient,
        TigerGraphOptions options,
        string graphName,
        CancellationToken cancellationToken = default)
    {
        var provider = new TigerGraphProvider(httpClient, options, graphName);
        await ValidateExtensionsAsync(provider, httpClient, options, graphName, cancellationToken)
            .ConfigureAwait(false);
        return provider;
    }

    /// <summary>
    /// Creates an administratively enabled provider and validates its installed-query manifest
    /// before returning it.
    /// </summary>
    /// <param name="httpClient">The host-managed HTTP client used for REST++ requests.</param>
    /// <param name="options">TigerGraph connection and extension options.</param>
    /// <param name="graphName">The target TigerGraph graph.</param>
    /// <param name="administrativeTransport">The explicit privileged GSQL transport.</param>
    /// <param name="cancellationToken">Stops extension discovery while the application is starting.</param>
    /// <returns>A provider whose configured extension manifest has been verified.</returns>
    public static async ValueTask<TigerGraphProvider> CreateAsync(
        HttpClient httpClient,
        TigerGraphOptions options,
        string graphName,
        ITigerGraphAdministrativeTransport administrativeTransport,
        CancellationToken cancellationToken = default)
    {
        var provider = new TigerGraphProvider(httpClient, options, graphName, administrativeTransport);
        await ValidateExtensionsAsync(provider, httpClient, options, graphName, cancellationToken)
            .ConfigureAwait(false);
        return provider;
    }

    private static async ValueTask ValidateExtensionsAsync(
        TigerGraphProvider provider,
        HttpClient httpClient,
        TigerGraphOptions options,
        string graphName,
        CancellationToken cancellationToken)
    {
        if (options.QueryExtensions is null)
        {
            return;
        }

        var snapshot = await new TigerGraphQueryExtensionDiscovery(
            httpClient,
            options,
            graphName,
            options.QueryExtensions)
            .DiscoverAsync(cancellationToken)
            .ConfigureAwait(false);
        provider.SetVerifiedQueryExtensions(snapshot);
    }
}
