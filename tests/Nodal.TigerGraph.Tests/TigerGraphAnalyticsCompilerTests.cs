using Nodal.Core.Analytics;
using Nodal.Core.Migrations;
using Nodal.Core.Query;

namespace Nodal.TigerGraph.Tests;

public sealed class TigerGraphAnalyticsCompilerTests
{
    [Fact]
    public void CompileTargetsOnlyExplicitlyConfiguredInstalledQueries()
    {
        var compiler = new TigerGraphAnalyticsCompiler(
            "SocialGraph",
            new Dictionary<GraphAnalyticsAlgorithm, string>
            {
                [GraphAnalyticsAlgorithm.PageRank] = "nodal_pagerank",
            });
        var model = new GraphAnalyticsQueryModel(
            GraphAnalyticsAlgorithm.PageRank,
            GraphAnalyticsFamily.Centrality,
            new GraphQueryModel(
                "Person", "node", null,
                [new GraphQueryParameter("p0", "TR", typeof(string))], null, []),
            "KNOWS", true, "ignored", "strength", 20,
            new Dictionary<string, object?> { ["maxChange"] = 0.001 });

        var command = compiler.Compile(model);

        Assert.Equal("restpp/query/SocialGraph/nodal_pagerank", command.Route);
        Assert.Equal(string.Empty, command.Text);
        Assert.Equal("Person", command.Parameters["nodal_vertex_type"]);
        Assert.Equal("KNOWS", command.Parameters["nodal_edge_type"]);
        Assert.Equal("strength", command.Parameters["nodal_weight_property"]);
        Assert.Equal(20, command.Parameters["nodal_limit"]);
        Assert.Equal(0.001, command.Parameters["nodal_maxChange"]);
    }

    [Fact]
    public void CompileRejectsUnavailableAlgorithmsAndUnsafeIdentifiers()
    {
        var compiler = new TigerGraphAnalyticsCompiler("SocialGraph", new Dictionary<GraphAnalyticsAlgorithm, string>());
        var model = new GraphAnalyticsQueryModel(
            GraphAnalyticsAlgorithm.Louvain,
            GraphAnalyticsFamily.CommunityDetection,
            new GraphQueryModel("Person", "node", null, [], null, []),
            "KNOWS", true, "social");

        Assert.Throws<NodalCapabilityNotSupportedException>(() => compiler.Compile(model));
        Assert.Throws<ArgumentException>(() => new TigerGraphAnalyticsCompiler(
            "bad/name", new Dictionary<GraphAnalyticsAlgorithm, string>()));
        Assert.Throws<ArgumentException>(() => new TigerGraphAnalyticsCompiler(
            "SocialGraph",
            new Dictionary<GraphAnalyticsAlgorithm, string>
            {
                [GraphAnalyticsAlgorithm.PageRank] = "../query",
            }));
    }

    [Fact]
    public void CompileTransportsBothPathEndpointsAndDepthToInstalledQuery()
    {
        var compiler = new TigerGraphAnalyticsCompiler("SocialGraph", new Dictionary<GraphAnalyticsAlgorithm, string>
        {
            [GraphAnalyticsAlgorithm.ShortestPath] = "nodal_shortest_path",
        });
        var source = new GraphQueryModel("Person", "node", null,
            [new GraphQueryParameter("p0", "a", typeof(string))], null, []);
        var target = new GraphQueryModel("Person", "node", null,
            [new GraphQueryParameter("p1", "b", typeof(string))], null, []);

        var command = compiler.Compile(new GraphAnalyticsQueryModel(
            GraphAnalyticsAlgorithm.ShortestPath, GraphAnalyticsFamily.PathFinding,
            source, "KNOWS", true, "SocialGraph", TargetNodes: target, MaxDepth: 6));

        Assert.Equal("a", command.Parameters["p0"]);
        Assert.Equal("b", command.Parameters["p1"]);
        Assert.Equal("Person", command.Parameters["nodal_target_vertex_type"]);
        Assert.Equal(6, command.Parameters["nodal_max_depth"]);
    }

    [Fact]
    public async Task RuntimeReportsConfiguredQueriesAndRejectsProjectionOperations()
    {
        var runtime = new TigerGraphAnalyticsRuntime(new Dictionary<GraphAnalyticsAlgorithm, string>
        {
            [GraphAnalyticsAlgorithm.PageRank] = "nodal_pagerank",
        });

        var snapshot = await runtime.DiscoverAsync();

        Assert.False(snapshot.IsLiveDiscovery);
        Assert.Contains("nodal_pagerank", snapshot.Procedures);
        Assert.Contains(GraphAnalyticsAlgorithm.PageRank, snapshot.Algorithms);
        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await runtime.EnsureProjectionAsync(new GraphProjectionDefinition("p", "Person", "KNOWS")));
        await Assert.ThrowsAsync<NotSupportedException>(async () => await runtime.DropProjectionAsync("p"));
        Assert.Throws<ArgumentNullException>(() => new TigerGraphAnalyticsRuntime(null!));
    }

    [Fact]
    public void MultiRelationBindingIsDeterministicAndTransportsCanonicalScope()
    {
        var model = MultiRelationModel();
        var key = GraphAnalyticsBindingKey.Create(model);
        var manifest = new TigerGraphAnalyticsBindingManifest(
        [
            new TigerGraphAnalyticsBinding(
                GraphAnalyticsAlgorithm.PageRank,
                key.Fingerprint,
                "verified_author_pagerank",
                SupportsWeights: true),
        ]);
        var provider = CreateProvider(new TigerGraphOptions
        {
            Endpoint = new Uri("http://localhost:14240"),
            AnalyticsBindingManifest = manifest,
        });

        var command = provider.AnalyticsCompiler.Compile(model);
        provider.ValidateAnalyticsScope(model);

        Assert.Equal("restpp/query/ResearchGraph/verified_author_pagerank", command.Route);
        Assert.Equal(["CO_AUTHORED", "SHARES_INTEREST"],
            Assert.IsType<string[]>(command.Parameters["nodal_edge_types"]));
        Assert.Equal([0.7, 0.3],
            Assert.IsType<double[]>(command.Parameters["nodal_relationship_coefficients"]));
        Assert.Equal(
            TigerGraphAnalyticsNaming.CreateQueryName(key),
            TigerGraphAnalyticsNaming.CreateQueryName(GraphAnalyticsBindingKey.Create(MultiRelationModel(reverse: true))));

        var versionTwoKey = GraphAnalyticsBindingKey.Create(model, "2");
        var incompatibleContract = new TigerGraphProvider(
            new HttpClient(),
            new TigerGraphOptions
            {
                Endpoint = new Uri("http://localhost:14240"),
                AnalyticsContractVersion = "2",
                AnalyticsBindingManifest = new TigerGraphAnalyticsBindingManifest(
                [
                    new TigerGraphAnalyticsBinding(
                        GraphAnalyticsAlgorithm.PageRank,
                        versionTwoKey.Fingerprint,
                        "old_contract_author_pagerank"),
                ]),
            },
            "ResearchGraph");
        Assert.Throws<NodalCapabilityNotSupportedException>(() =>
            incompatibleContract.AnalyticsCompiler.Compile(model));

        var unweightedBinding = new TigerGraphAnalyticsBindingManifest(
        [
            new TigerGraphAnalyticsBinding(
                GraphAnalyticsAlgorithm.PageRank,
                key.Fingerprint,
                "unweighted_author_pagerank"),
        ]);
        Assert.Throws<NodalCapabilityNotSupportedException>(() => CreateProvider(new TigerGraphOptions
        {
            Endpoint = new Uri("http://localhost:14240"),
            AnalyticsBindingManifest = unweightedBinding,
        }).AnalyticsCompiler.Compile(model));
    }

    [Fact]
    public void MissingBindingFailsBeforeTransportWithStableCapabilityCode()
    {
        var compiler = CreateProvider(new TigerGraphOptions
        {
            Endpoint = new Uri("http://localhost:14240"),
        }).AnalyticsCompiler;

        var exception = Assert.Throws<NodalCapabilityNotSupportedException>(() => compiler.Compile(MultiRelationModel()));

        Assert.Equal("NODAL-TIGERGRAPH-ANALYTICS-BINDING", exception.CapabilityCode);
        Assert.Contains(GraphAnalyticsBindingKey.Create(MultiRelationModel()).Fingerprint, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallMissingRegistersAndInstallsManagedPageRankDefinitionOnlyWithCatalog()
    {
        var transport = new RecordingAdministrativeTransport();
        var options = new TigerGraphOptions
        {
            Endpoint = new Uri("http://localhost:14240"),
            AccessToken = "test-token",
            AnalyticsProvisioningMode = TigerGraphAnalyticsProvisioningMode.InstallMissing,
        };
        var provider = new TigerGraphProvider(
            new HttpClient(new SuccessHandler()), options, "ResearchGraph", transport);
        var compiler = provider.AnalyticsCompiler;
        var model = MultiRelationModel(unitWeights: true);

        var command = compiler.Compile(model);
        await provider.CommandExecutor.ExecuteAsync(command);

        Assert.Contains(GraphAnalyticsBindingKey.Create(model).Fingerprint[..8], command.Route, StringComparison.Ordinal);
        Assert.Equal(2, transport.Commands.Count);
        Assert.Contains("CREATE OR REPLACE QUERY", transport.Commands[0].Text, StringComparison.Ordinal);
        Assert.Contains("INSTALL QUERY -FORCE", transport.Commands[1].Text, StringComparison.Ordinal);
        Assert.Throws<NotSupportedException>(() => compiler.Compile(MultiRelationModel()));
        Assert.Throws<NodalCapabilityNotSupportedException>(() => CreateProvider(new TigerGraphOptions
        {
            Endpoint = new Uri("http://localhost:14240"),
            AnalyticsProvisioningMode = TigerGraphAnalyticsProvisioningMode.InstallMissing,
        }).AnalyticsCompiler.Compile(model));
    }

    [Fact]
    public void BindingManifestRejectsUnsafeAndDuplicateEntries()
    {
        var binding = new TigerGraphAnalyticsBinding(GraphAnalyticsAlgorithm.PageRank, "fingerprint", "safe_query");

        Assert.Throws<ArgumentException>(() => new TigerGraphAnalyticsBindingManifest([binding, binding]));
        Assert.Throws<ArgumentException>(() => new TigerGraphAnalyticsBindingManifest(
            [binding with { QueryName = "bad/query" }]));
    }

    private static GraphAnalyticsQueryModel MultiRelationModel(bool reverse = false, bool unitWeights = false)
    {
        GraphAnalyticsRelationshipDefinition[] relationships =
        [
            new("CO_AUTHORED", false, unitWeights ? null : "paperCount", unitWeights ? 1 : 0.7),
            new("SHARES_INTEREST", false, unitWeights ? null : "similarity", unitWeights ? 1 : 0.3),
        ];
        if (reverse)
        {
            Array.Reverse(relationships);
        }
        return new GraphAnalyticsQueryModel(
            GraphAnalyticsAlgorithm.PageRank,
            GraphAnalyticsFamily.Centrality,
            new GraphQueryModel("Author", "node", null, [], null, []),
            relationships[0].RelationshipType,
            false,
            "author-influence",
            Relationships: relationships);
    }

    private static TigerGraphProvider CreateProvider(
        TigerGraphOptions options,
        ITigerGraphAdministrativeTransport? administrativeTransport = null)
    {
        var client = new HttpClient();
        return administrativeTransport is null
            ? new TigerGraphProvider(client, options, "ResearchGraph")
            : new TigerGraphProvider(client, options, "ResearchGraph", administrativeTransport);
    }

    private sealed class StubAdministrativeTransport : ITigerGraphAdministrativeTransport
    {
        public ValueTask ExecuteAsync(MigrationCommand command, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class RecordingAdministrativeTransport : ITigerGraphAdministrativeTransport
    {
        public List<MigrationCommand> Commands { get; } = [];

        public ValueTask ExecuteAsync(MigrationCommand command, CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SuccessHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"results\":[]}"),
            });
    }
}
