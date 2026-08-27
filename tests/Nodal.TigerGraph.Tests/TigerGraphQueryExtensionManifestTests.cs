using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Nodal.Core.Migrations;
using Nodal.TigerGraph.Extensions;

namespace Nodal.TigerGraph.Tests;

public sealed class TigerGraphQueryExtensionManifestTests
{
    [Fact]
    public void ManifestPreservesValidatedConfiguredFeatures()
    {
        var manifest = new TigerGraphQueryExtensionManifest(
            new Version(1, 0, 0),
            new Dictionary<TigerGraphQueryExtensionFeature, string>
            {
                [TigerGraphQueryExtensionFeature.CorrelatedExistence] = "nodal_exists_v1",
            });

        Assert.True(manifest.Supports(TigerGraphQueryExtensionFeature.CorrelatedExistence));
        Assert.Equal(
            "nodal_exists_v1",
            manifest.GetRequiredQueryName(TigerGraphQueryExtensionFeature.CorrelatedExistence, "4.2.4 Community"));
    }

    [Fact]
    public void ManifestRejectsUnsafeNamesAndFailsFastForMissingFeatures()
    {
        Assert.Throws<ArgumentException>(() => new TigerGraphQueryExtensionManifest(
            new Version(1, 0, 0),
            new Dictionary<TigerGraphQueryExtensionFeature, string>
            {
                [TigerGraphQueryExtensionFeature.CorrelatedExistence] = "unsafe; query",
            }));

        var manifest = new TigerGraphQueryExtensionManifest(
            new Version(1, 0, 0),
            new Dictionary<TigerGraphQueryExtensionFeature, string>());
        var exception = Assert.Throws<NodalCapabilityNotSupportedException>(() =>
            manifest.GetRequiredQueryName(TigerGraphQueryExtensionFeature.CorrelatedExistence, "4.2.4 Community"));

        Assert.Equal("NODAL-TIGERGRAPH-EXTENSION-NOT-CONFIGURED", exception.CapabilityCode);
    }

    [Fact]
    public async Task DiscoveryValidatesVersionFeaturesRouteAndAuthentication()
    {
        var manifest = Manifest();
        var handler = new StubHandler("""
        {"error":false,"results":[{"nodal_extension_version":"1.0.0","nodal_extension_features":["CorrelatedExistence"]}]}
        """);
        using var client = new HttpClient(handler);
        var snapshot = await new TigerGraphQueryExtensionDiscovery(client, Options(), "Social", manifest).DiscoverAsync();

        Assert.Equal(new Version(1, 0, 0), snapshot.Version);
        Assert.Contains(TigerGraphQueryExtensionFeature.CorrelatedExistence, snapshot.Features);
        Assert.Equal("https://tigergraph.example/restpp/query/Social/nodal_extension_capabilities", handler.RequestUri?.ToString());
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "token"), handler.Authorization);
    }

    [Fact]
    public async Task DiscoveryFailsFastForUnavailableOrIncompatibleContracts()
    {
        using var unavailableClient = new HttpClient(new StubHandler("denied", HttpStatusCode.NotFound));
        var unavailable = await Assert.ThrowsAsync<NodalCapabilityNotSupportedException>(async () =>
            await new TigerGraphQueryExtensionDiscovery(unavailableClient, Options(), "Social", Manifest()).DiscoverAsync());
        Assert.Equal("NODAL-TIGERGRAPH-EXTENSION-DISCOVERY-FAILED", unavailable.CapabilityCode);

        using var incompatibleClient = new HttpClient(new StubHandler("""
        {"error":false,"results":[{"nodal_extension_version":"2.0.0","nodal_extension_features":[]}]}
        """));
        var incompatible = await Assert.ThrowsAsync<NodalCapabilityNotSupportedException>(async () =>
            await new TigerGraphQueryExtensionDiscovery(incompatibleClient, Options(), "Social", Manifest()).DiscoverAsync());
        Assert.Equal("NODAL-TIGERGRAPH-EXTENSION-CONTRACT-MISMATCH", incompatible.CapabilityCode);
    }

    [Fact]
    public async Task FactoryValidatesConfiguredManifestBeforeReturningProvider()
    {
        var manifest = Manifest();
        var handler = new StubHandler("""
        {"error":false,"results":[{"nodal_extension_version":"1.0.0","nodal_extension_features":["CorrelatedExistence"]}]}
        """);
        using var client = new HttpClient(handler);
        var provider = await TigerGraphProviderFactory.CreateAsync(
            client,
            Options(manifest),
            "Social");

        Assert.Same(manifest, provider.QueryExtensions);
        var verified = Assert.IsType<TigerGraphQueryExtensionSnapshot>(provider.VerifiedQueryExtensions);
        Assert.Equal(new Version(1, 0, 0), verified.Version);
        Assert.Contains(
            TigerGraphQueryExtensionFeature.CorrelatedExistence,
            verified.Features);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task FactorySkipsDiscoveryWithoutManifestAndSupportsAdministrativeComposition()
    {
        var handler = new StubHandler("not used", HttpStatusCode.InternalServerError);
        using var client = new HttpClient(handler);
        var provider = await TigerGraphProviderFactory.CreateAsync(
            client,
            Options(),
            "Social",
            new StubAdministrativeTransport());

        Assert.Null(provider.QueryExtensions);
        Assert.Null(provider.VerifiedQueryExtensions);
        Assert.Equal(0, handler.RequestCount);
    }

    private static TigerGraphQueryExtensionManifest Manifest() => new(
        new Version(1, 0, 0),
        new Dictionary<TigerGraphQueryExtensionFeature, string>
        {
            [TigerGraphQueryExtensionFeature.CorrelatedExistence] = "nodal_exists_v1",
        });

    private static TigerGraphOptions Options(TigerGraphQueryExtensionManifest? manifest = null) => new()
    {
        Endpoint = new Uri("https://tigergraph.example/"),
        AccessToken = "token",
        QueryExtensions = manifest,
    };

    private sealed class StubHandler(string payload, HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public AuthenticationHeaderValue? Authorization { get; private set; }
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class StubAdministrativeTransport : ITigerGraphAdministrativeTransport
    {
        public ValueTask ExecuteAsync(
            MigrationCommand command,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
