using System.Net;
using Nodal.Core.Migrations;
using Nodal.Core.Query;
using Nodal.TigerGraph.Extensions;

namespace Nodal.TigerGraph.Tests;

public sealed class TigerGraphInstalledQueryExtensionTests
{
    private static readonly int[] SampleStatuses = [1, 2];

    [Fact]
    public void FactoryCreatesDeterministicParameterizedExistenceQuery()
    {
        var first = TigerGraphInstalledQueryDefinitionFactory.CreateCorrelatedExistence("Social", Query());
        var second = TigerGraphInstalledQueryDefinitionFactory.CreateCorrelatedExistence("Social", Query(99));

        Assert.Equal(first.Name, second.Name);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(first.Text, second.Text);
        Assert.StartsWith("nodal_exists_", first.Name, StringComparison.Ordinal);
        Assert.Contains("CREATE OR REPLACE QUERY", first.Text, StringComparison.Ordinal);
        Assert.Contains("INT minimumAge", first.Text, StringComparison.Ordinal);
        Assert.Contains("STRING name", first.Text, StringComparison.Ordinal);
        Assert.Contains("nodal_source.Age >= minimumAge AND nodal_target.Name LIKE name + \"%\"", first.Text, StringComparison.Ordinal);
        Assert.EndsWith($"INSTALL QUERY -FORCE {first.Name}", first.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(GraphTraversalDirection.Outgoing, "-(KNOWS:nodal_relation)->")]
    [InlineData(GraphTraversalDirection.Incoming, "<-(KNOWS:nodal_relation)-")]
    [InlineData(GraphTraversalDirection.Undirected, "-(KNOWS:nodal_relation)-")]
    public void FactoryRendersEveryDirection(GraphTraversalDirection direction, string expected)
    {
        var definition = TigerGraphInstalledQueryDefinitionFactory.CreateCorrelatedExistence(
            "Social",
            Query(direction: direction));

        Assert.Contains(expected, definition.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void FactoryRendersAntiExistenceAndPredicateFamilies()
    {
        var sourcePredicate = new GraphLogicalPredicate(
            new GraphNotPredicate(new GraphNullPredicate("DeletedAt", false)),
            GraphLogicalOperator.Or,
            new GraphInPredicate("Status", "statuses", true));
        var relationPredicate = new GraphComparisonPredicate("Strength", GraphComparisonOperator.NotEqual, "strength");
        var query = Query(negated: true, sourcePredicate: sourcePredicate, relationPredicate: relationPredicate,
            additionalParameters:
            [
                new GraphQueryParameter("statuses", SampleStatuses, typeof(int[])),
                new GraphQueryParameter("strength", 0.5, typeof(double)),
            ]);

        var definition = TigerGraphInstalledQueryDefinitionFactory.CreateCorrelatedExistence("Social", query);

        Assert.Contains("SET<INT> statuses", definition.Text, StringComparison.Ordinal);
        Assert.Contains("DOUBLE strength", definition.Text, StringComparison.Ordinal);
        Assert.Contains("OrAccum<BOOL> @nodal_match", definition.Text, StringComparison.Ordinal);
        Assert.Contains("NOT (nodal_source.DeletedAt IS NOT NULL) OR nodal_source.Status NOT IN statuses", definition.Text, StringComparison.Ordinal);
        Assert.Contains("NOT nodal_source.@nodal_match", definition.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void FactoryMapsSupportedScalarTypesAndRejectsUnsupportedTypes()
    {
        var parameters = new[]
        {
            new GraphQueryParameter("flag", true, typeof(bool)),
            new GraphQueryParameter("id", Guid.Empty, typeof(Guid)),
            new GraphQueryParameter("amount", 1m, typeof(decimal)),
            new GraphQueryParameter("when", DateTimeOffset.UnixEpoch, typeof(DateTimeOffset)),
            new GraphQueryParameter("state", SampleState.Active, typeof(SampleState)),
        };
        var definition = TigerGraphInstalledQueryDefinitionFactory.CreateCorrelatedExistence(
            "Social", Query(additionalParameters: parameters));

        Assert.Contains("BOOL flag", definition.Text, StringComparison.Ordinal);
        Assert.Contains("STRING id", definition.Text, StringComparison.Ordinal);
        Assert.Contains("DOUBLE amount", definition.Text, StringComparison.Ordinal);
        Assert.Contains("DATETIME when", definition.Text, StringComparison.Ordinal);
        Assert.Contains("INT state", definition.Text, StringComparison.Ordinal);
        Assert.Throws<NotSupportedException>(() => TigerGraphInstalledQueryDefinitionFactory.CreateCorrelatedExistence(
            "Social", Query(additionalParameters: [new GraphQueryParameter("uri", new Uri("https://example.test"), typeof(Uri))])));
    }

    [Fact]
    public void FactoryRejectsInvalidIdentifiersAndUnsupportedShapes()
    {
        Assert.Throws<ArgumentException>(() => TigerGraphInstalledQueryDefinitionFactory.CreateCorrelatedExistence("bad graph", Query()));
        Assert.Throws<ArgumentException>(() => TigerGraphInstalledQueryDefinitionFactory.CreateCorrelatedExistence("Social", Query(nodeType: "bad-node")));
        Assert.Throws<NotSupportedException>(() => TigerGraphInstalledQueryDefinitionFactory.CreateCorrelatedExistence(
            "Social", Query() with { ExistencePatterns = [] }));
        Assert.Throws<NotSupportedException>(() => TigerGraphInstalledQueryDefinitionFactory.CreateCorrelatedExistence(
            "Social", Query() with { Projection = GraphQueryProjection.Row }));
    }

    [Fact]
    public void FactoryRejectsUnknownPredicatesAndEnumValues()
    {
        Assert.Throws<NotSupportedException>(() => TigerGraphInstalledQueryDefinitionFactory.CreateCorrelatedExistence(
            "Social", Query(sourcePredicate: new UnknownPredicate())));
        Assert.Throws<ArgumentOutOfRangeException>(() => TigerGraphInstalledQueryDefinitionFactory.CreateCorrelatedExistence(
            "Social", Query(direction: (GraphTraversalDirection)999)));
        Assert.Throws<ArgumentOutOfRangeException>(() => TigerGraphInstalledQueryDefinitionFactory.CreateCorrelatedExistence(
            "Social", Query() with
            {
                ExistencePatterns =
                [
                    Query().EffectiveExistencePatterns[0] with
                    {
                        TargetPredicate = new GraphStringPredicate("Name", (GraphStringOperator)999, "name"),
                    },
                ],
            }));
    }

    [Theory]
    [InlineData(GraphStringOperator.Contains, "\"%\" + name + \"%\"")]
    [InlineData(GraphStringOperator.EndsWith, "\"%\" + name")]
    public void FactoryRendersRemainingStringOperators(GraphStringOperator value, string expected)
    {
        var pattern = Query().EffectiveExistencePatterns[0] with
        {
            TargetPredicate = new GraphStringPredicate("Name", value, "name"),
        };

        var definition = TigerGraphInstalledQueryDefinitionFactory.CreateCorrelatedExistence(
            "Social", Query() with { ExistencePatterns = [pattern] });

        Assert.Contains(expected, definition.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderExposesExtensionManifestAndSeparateSchemaChannel()
    {
        var manifest = new TigerGraphQueryExtensionManifest(
            new Version(1, 0),
            new Dictionary<TigerGraphQueryExtensionFeature, string>());
        var options = new TigerGraphOptions
        {
            Endpoint = new Uri("https://tigergraph.example/"),
            AccessToken = "token",
            QueryExtensions = manifest,
        };
        using var client = new HttpClient();

        var provider = new TigerGraphProvider(
            client, options, "Social", new RecordingTransport(), new SchemaTransport());

        Assert.Same(manifest, provider.QueryExtensions);
        Assert.IsType<TigerGraphSchemaIntrospector>(provider.SchemaIntrospector);
        Assert.NotNull(provider.QueryCompiler);
        Assert.NotNull(provider.AnalyticsCompiler);
        Assert.NotNull(provider.AnalyticsRuntime);
        Assert.Equal("TigerGraph", provider.QueryCapabilities.ProviderName);
    }

    [Fact]
    public void AdministrativeCapabilitiesExposeVersionAndValidateRequiredControlPlane()
    {
        var supported = new TigerGraphAdministrativeCapabilities(
            "4.2.4", true, true, true, true, TigerGraphMigrationLockScope.Distributed);
        var unsupported = supported with { CanCleanupJobs = false };

        Assert.Equal("4.2.4", supported.ServerVersion);
        supported.EnsureMigrationSupport();
        var exception = Assert.Throws<NodalCapabilityNotSupportedException>(unsupported.EnsureMigrationSupport);
        Assert.Equal("NODAL-TIGERGRAPH-MIGRATION-CONTROL-PLANE", exception.CapabilityCode);
    }

    [Fact]
    public async Task ProviderInstallsAndExecutesConfiguredCorrelatedExistenceQueryOnce()
    {
        var graphName = UniqueGraph();
        var handler = new QueryHandler("""
        {"error":false,"results":[{"result":[{"v_id":"person-1","v_type":"Person","attributes":{"Name":"Ada"}}]}]}
        """);
        using var client = new HttpClient(handler);
        var transport = new RecordingTransport();
        var options = new TigerGraphOptions
        {
            Endpoint = new Uri("https://tigergraph.example/"),
            AccessToken = "token",
            GeneratedQueryExtensions = new HashSet<TigerGraphQueryExtensionFeature>
            {
                TigerGraphQueryExtensionFeature.CorrelatedExistence,
            },
        };
        var provider = new TigerGraphProvider(client, options, graphName, transport);
        var command = provider.QueryCompiler.Compile(Query());

        var first = await provider.CommandExecutor.ExecuteAsync(command);
        var second = await provider.CommandExecutor.ExecuteAsync(command);

        Assert.True(provider.QueryCapabilities.Supports(GraphQueryCapability.CorrelatedSubquery));
        Assert.StartsWith($"restpp/query/{graphName}/nodal_exists_", command.Route, StringComparison.Ordinal);
        Assert.Equal(2, transport.Commands.Count);
        Assert.Equal(2, handler.RequestCount);
        Assert.Equal("person-1", Assert.Single(first.Nodes).Id);
        Assert.Equal("person-1", Assert.Single(second.Nodes).Id);
    }

    [Fact]
    public void ProviderDoesNotAdvertiseUnconfiguredCorrelatedExistence()
    {
        using var client = new HttpClient(new QueryHandler("{\"error\":false,\"results\":[]}"));
        var provider = new TigerGraphProvider(
            client,
            new TigerGraphOptions { Endpoint = new Uri("https://tigergraph.example/") },
            UniqueGraph(),
            new RecordingTransport());

        Assert.False(provider.QueryCapabilities.Supports(GraphQueryCapability.CorrelatedSubquery));
        Assert.Throws<NotSupportedException>(() => provider.QueryCompiler.Compile(Query()));
    }

    [Fact]
    public void ProviderRejectsUnimplementedGeneratedExtensionFlagsBeforeTransport()
    {
        using var client = new HttpClient(new QueryHandler("{\"error\":false,\"results\":[]}"));
        var provider = new TigerGraphProvider(
            client,
            new TigerGraphOptions
            {
                Endpoint = new Uri("https://tigergraph.example/"),
                GeneratedQueryExtensions = new HashSet<TigerGraphQueryExtensionFeature>
                {
                    TigerGraphQueryExtensionFeature.MultiplePatterns,
                    TigerGraphQueryExtensionFeature.SetOperations,
                    TigerGraphQueryExtensionFeature.OptionalTraversal,
                },
            },
            UniqueGraph(),
            new RecordingTransport());
        var operand = new GraphQueryModel("Person", "person", null, [], null, []);
        var optional = operand with
        {
            Traversals =
            [
                new GraphTraversalStep(
                    "KNOWS", "Person", "person", "knows", "friend",
                    GraphTraversalDirection.Outgoing, null, Optional: true),
            ],
        };
        var multiple = operand with
        {
            MatchPatterns =
            [
                new GraphTraversalStep(
                    "KNOWS", "Person", "person", "knows", "friend",
                    GraphTraversalDirection.Outgoing, null),
            ],
        };
        var set = operand with
        {
            SetOperation = new GraphSetOperation(GraphSetOperationKind.Union, operand, operand),
        };

        Assert.False(provider.QueryCapabilities.Supports(GraphQueryCapability.OptionalTraversal));
        Assert.False(provider.QueryCapabilities.Supports(GraphQueryCapability.MultiplePatterns));
        Assert.False(provider.QueryCapabilities.Supports(GraphQueryCapability.SetOperations));
        Assert.Equal("NODAL-QUERY-OPTIONAL-TRAVERSAL", Assert.Throws<NodalCapabilityNotSupportedException>(
            () => GraphQueryPreflight.Validate(optional, provider.QueryCapabilities)).CapabilityCode);
        Assert.Equal("NODAL-QUERY-MULTIPLE-PATTERNS", Assert.Throws<NodalCapabilityNotSupportedException>(
            () => GraphQueryPreflight.Validate(multiple, provider.QueryCapabilities)).CapabilityCode);
        Assert.Equal("NODAL-QUERY-SET-OPERATIONS", Assert.Throws<NodalCapabilityNotSupportedException>(
            () => GraphQueryPreflight.Validate(set, provider.QueryCapabilities)).CapabilityCode);
    }

    [Fact]
    public async Task InstallerExecutesDefinitionAndInstallationOnlyOnce()
    {
        var transport = new RecordingTransport();
        var installer = new TigerGraphInstalledQueryInstaller(transport, UniqueGraph());
        var definition = Definition();

        await Task.WhenAll(installer.InstallAsync(definition).AsTask(), installer.InstallAsync(definition).AsTask());

        Assert.Collection(
            transport.Commands,
            command => Assert.Equal(MigrationCommandKind.QueryDefinition, command.Kind),
            command => Assert.Equal(MigrationCommandKind.QueryInstallation, command.Kind));
    }

    [Fact]
    public async Task InstallerInstancesDoNotShareStateAcrossAdministrativeTargets()
    {
        var firstTransport = new RecordingTransport();
        var secondTransport = new RecordingTransport();
        var graphName = UniqueGraph();
        var definition = Definition();

        await new TigerGraphInstalledQueryInstaller(firstTransport, graphName).InstallAsync(definition);
        await new TigerGraphInstalledQueryInstaller(secondTransport, graphName).InstallAsync(definition);

        Assert.Equal(2, firstTransport.Commands.Count);
        Assert.Equal(2, secondTransport.Commands.Count);
    }

    [Fact]
    public async Task InstallerEvictsFailedInstallationSoItCanBeRetried()
    {
        var transport = new RecordingTransport(failures: 1);
        var installer = new TigerGraphInstalledQueryInstaller(transport, UniqueGraph());
        var definition = Definition();

        await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync(definition).AsTask());
        await installer.InstallAsync(definition);

        Assert.Equal(3, transport.Commands.Count);
    }

    [Fact]
    public async Task InstallerValidatesDependenciesAndDefinitionShape()
    {
        Assert.Throws<ArgumentNullException>(() => new TigerGraphInstalledQueryInstaller(null!, "Social"));
        Assert.Throws<ArgumentException>(() => new TigerGraphInstalledQueryInstaller(new RecordingTransport(), " "));
        var installer = new TigerGraphInstalledQueryInstaller(new RecordingTransport(), UniqueGraph());
        await Assert.ThrowsAsync<ArgumentNullException>(() => installer.InstallAsync(null!).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => installer.InstallAsync(
            new TigerGraphInstalledQueryDefinition("bad", Guid.NewGuid().ToString("N"), "CREATE QUERY bad() FOR GRAPH Social {}")).AsTask());
    }

    private static GraphQueryModel Query(
        int minimumAge = 18,
        GraphTraversalDirection direction = GraphTraversalDirection.Outgoing,
        bool negated = false,
        GraphPredicate? sourcePredicate = null,
        GraphPredicate? relationPredicate = null,
        IReadOnlyList<GraphQueryParameter>? additionalParameters = null,
        string nodeType = "Person")
    {
        var parameters = new List<GraphQueryParameter>
        {
            new("minimumAge", minimumAge, typeof(int)),
            new("name", "A", typeof(string)),
        };
        if (additionalParameters is not null) parameters.AddRange(additionalParameters);
        return new GraphQueryModel(
            nodeType,
            "person",
            sourcePredicate ?? new GraphComparisonPredicate("Age", GraphComparisonOperator.GreaterThanOrEqual, "minimumAge"),
            parameters,
            null,
            [],
            ExistencePatterns:
            [
                new GraphExistencePattern(
                    "KNOWS", "Person", "person", "knows", "friend", direction,
                    new GraphStringPredicate("Name", GraphStringOperator.StartsWith, "name"),
                    relationPredicate,
                    negated),
            ]);
    }

    private static TigerGraphInstalledQueryDefinition Definition() => new(
        $"nodal_test_{Guid.NewGuid():N}",
        Guid.NewGuid().ToString("N"),
        "CREATE QUERY nodal_test() FOR GRAPH Social {}\nINSTALL QUERY nodal_test");

    private static string UniqueGraph() => $"Social_{Guid.NewGuid():N}";

    private enum SampleState { Active = 1 }

    private sealed record UnknownPredicate : GraphPredicate;

    private sealed class SchemaTransport : ITigerGraphSchemaIntrospectionTransport
    {
        public ValueTask<NodalSchemaSnapshot> CaptureSchemaAsync(string graphName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingTransport(int failures = 0) : ITigerGraphAdministrativeTransport
    {
        private int failuresRemaining = failures;
        public List<MigrationCommand> Commands { get; } = [];

        public ValueTask ExecuteAsync(MigrationCommand command, CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            if (Interlocked.Decrement(ref failuresRemaining) >= 0)
            {
                throw new InvalidOperationException("Simulated installation failure.");
            }
            return ValueTask.CompletedTask;
        }
    }

    private sealed class QueryHandler(string payload) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload),
            });
        }
    }
}
