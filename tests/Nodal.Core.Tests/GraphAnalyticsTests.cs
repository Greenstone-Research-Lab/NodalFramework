using Nodal.Core.Analytics;
using Nodal.Core.Execution;
using Nodal.Core.Metadata;
using Nodal.Core.Providers;
using Nodal.Core.Query;

namespace Nodal.Core.Tests;

public sealed class GraphAnalyticsTests
{
    [Fact]
    public void FluentApiBuildsImmutableCentralityAndCommunityModels()
    {
        var context = new SocialContext(new QueryOnlyProvider());

        var original = context.People.Query().Analyze(context.Knows).PageRank();
        var configured = original
            .OnProjection("production-social")
            .WeightedBy(edge => edge.Strength)
            .WithOption("dampingFactor", 0.85)
            .Top(25);
        var community = context.People.Query().Analyze(context.Knows).Louvain().ToQueryModel();

        Assert.Equal("nodal", original.ToQueryModel().ProjectionName);
        Assert.Equal("production-social", configured.ToQueryModel().ProjectionName);
        Assert.Equal("strength", configured.ToQueryModel().RelationshipWeightProperty);
        Assert.Equal(0.85, configured.ToQueryModel().EffectiveConfiguration["dampingFactor"]);
        Assert.Equal(25, configured.ToQueryModel().Limit);
        Assert.Equal(GraphAnalyticsFamily.CommunityDetection, community.Family);
    }

    [Fact]
    public void BuilderExposesEveryDeclaredCentralityAndCommunityAlgorithm()
    {
        var context = new SocialContext(new QueryOnlyProvider());
        var builder = context.People.Query().Analyze(context.Knows);
        var requested = Enum.GetValues<GraphAnalyticsAlgorithm>()
            .Where(algorithm => algorithm < GraphAnalyticsAlgorithm.ShortestPath)
            .Select(algorithm => builder.Using(algorithm).ToQueryModel().Algorithm)
            .ToArray();

        Assert.Equal(28, requested.Length);
        Assert.Equal(requested.Length, requested.Distinct().Count());

        var named = new[]
        {
            builder.PageRank(), builder.ArticleRank(), builder.Betweenness(), builder.Closeness(),
            builder.Degree(), builder.Eigenvector(), builder.Harmonic(), builder.Hits(),
            builder.ArticulationPoints(), builder.Bridges(), builder.Celf(), builder.Louvain(),
            builder.Leiden(), builder.LabelPropagation(), builder.WeaklyConnectedComponents(),
            builder.StronglyConnectedComponents(), builder.TriangleCount(),
            builder.LocalClusteringCoefficient(), builder.KCore(), builder.K1Coloring(),
            builder.KMeans(), builder.Hdbscan(), builder.CliqueCounting(), builder.Conductance(),
            builder.Modularity(), builder.ModularityOptimization(), builder.ApproximateMaximumKCut(),
            builder.SpeakerListenerLabelPropagation(),
        };
        Assert.Equal(28, named.Length);
        Assert.Throws<ArgumentException>(() => builder.Using(GraphAnalyticsAlgorithm.ShortestPath));
    }

    [Fact]
    public async Task ExecutionChecksCapabilitiesAndPreservesNodeMetricAssociation()
    {
        var provider = new AnalyticsProvider(
            [GraphAnalyticsAlgorithm.PageRank],
            supportsWeights: true);
        var context = new SocialContext(provider);

        var first = await context.People.Query().Analyze(context.Knows).PageRank().ToListAsync();
        var second = await context.People.Query().Analyze(context.Knows).PageRank().ToListAsync();

        var result = Assert.Single(first);
        Assert.Equal("person-1", result.Node?.Id);
        Assert.Equal(0.9, result.Score);
        Assert.Same(result.Node, Assert.Single(second).Node);
        Assert.Equal(GraphAnalyticsAlgorithm.PageRank, provider.Compiler.Query?.Algorithm);
        Assert.Equal("analytics command", provider.Executor.Command?.Text);

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await context.People.Query().Analyze(context.Knows).Louvain().ToListAsync());
        Assert.Equal(2, provider.Executor.CallCount);
    }

    [Fact]
    public async Task ExecutionRejectsMissingProviderAndUnsupportedWeightsBeforeTransport()
    {
        var queryOnly = new SocialContext(new QueryOnlyProvider());
        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await queryOnly.People.Query().Analyze(queryOnly.Knows).PageRank().ToListAsync());

        var provider = new AnalyticsProvider([GraphAnalyticsAlgorithm.PageRank], supportsWeights: false);
        var context = new SocialContext(provider);
        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await context.People.Query().Analyze(context.Knows).PageRank()
                .WeightedBy(edge => edge.Strength)
                .ToListAsync());
        Assert.Equal(0, provider.Executor.CallCount);

        var algorithmRestricted = new AnalyticsProvider(
            [GraphAnalyticsAlgorithm.PageRank],
            supportsWeights: true,
            detailSupportsWeights: false);
        var restrictedContext = new SocialContext(algorithmRestricted);
        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await restrictedContext.People.Query().Analyze(restrictedContext.Knows).PageRank()
                .WeightedBy(edge => edge.Strength)
                .ToListAsync());
        Assert.Equal(0, algorithmRestricted.Executor.CallCount);

        var pathContext = new SocialContext(new AnalyticsProvider(
            [GraphAnalyticsAlgorithm.Dijkstra], supportsWeights: false));
        await Assert.ThrowsAsync<NotSupportedException>(async () => await pathContext.People
            .Match(person => person.Id == "person-1")
            .ShortestPathTo(pathContext.People.Match(person => person.Id == "person-2"), pathContext.Knows)
            .Dijkstra().WeightedBy(edge => edge.Strength).ToListAsync());

        var weightedProvider = new AnalyticsProvider(
            [GraphAnalyticsAlgorithm.Dijkstra, GraphAnalyticsAlgorithm.AStar], supportsWeights: true);
        var weightedContext = new SocialContext(weightedProvider);
        var endpoint = weightedContext.People.Match(person => person.Id == "person-1")
            .ShortestPathTo(
                weightedContext.People.Match(person => person.Id == "person-2"), weightedContext.Knows);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await endpoint.Dijkstra().ToListAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await endpoint
            .AStar(person => person.Latitude, person => person.Longitude).ToListAsync());
        Assert.Equal(0, weightedProvider.Executor.CallCount);
    }

    [Fact]
    public void CapabilityMetadataExposesVersionRequirementAndVerification()
    {
        var details = new GraphAlgorithmCapability(
            GraphAnalyticsAlgorithm.PageRank,
            GraphAnalyticsAvailability.Extension,
            GraphCapabilityVerification.Integration,
            "GDS",
            SupportsWeights: true);
        var capabilities = new GraphAnalyticsCapabilities
        {
            ProviderName = "SampleGraph",
            TestedProviderVersion = "1.2.3",
            ClientVersion = "Driver 4.5.6",
            Algorithms = new HashSet<GraphAnalyticsAlgorithm> { GraphAnalyticsAlgorithm.PageRank },
            AlgorithmDetails = new Dictionary<GraphAnalyticsAlgorithm, GraphAlgorithmCapability>
            {
                [GraphAnalyticsAlgorithm.PageRank] = details,
            },
            SupportsProjectionManagement = true,
        };

        Assert.Equal("SampleGraph", capabilities.ProviderName);
        Assert.Equal("1.2.3", capabilities.TestedProviderVersion);
        Assert.Equal("Driver 4.5.6", capabilities.ClientVersion);
        Assert.True(capabilities.SupportsProjectionManagement);
        Assert.Equal("GDS", capabilities.GetDetails(GraphAnalyticsAlgorithm.PageRank).Requirement);
        Assert.Equal(GraphAnalyticsAvailability.Extension, details.Availability);
        Assert.Equal(GraphCapabilityVerification.Integration, details.Verification);
        Assert.Throws<NotSupportedException>(() => capabilities.GetDetails(GraphAnalyticsAlgorithm.Louvain));
    }

    [Fact]
    public async Task ShortestPathRebasesEndpointParametersAndMaterializesTrackedRoute()
    {
        var provider = new AnalyticsProvider([GraphAnalyticsAlgorithm.ShortestPath], supportsWeights: false);
        var context = new SocialContext(provider);
        var query = context.People.Match(person => person.Id == "person-1")
            .ShortestPathTo(
                context.People.Match(person => person.Id == "person-2"),
                context.Knows)
            .MaxDepth(6);

        var model = query.ToQueryModel();
        var route = await query.SingleAsync();
        var repeated = await query.SingleAsync();

        Assert.Equal(["p0"], model.Nodes.Parameters.Select(item => item.Name));
        Assert.Equal(["p1"], model.TargetNodes?.Parameters.Select(item => item.Name));
        Assert.Equal(6, model.MaxDepth);
        Assert.Equal(1, route.HopCount);
        Assert.Equal(2.5, route.TotalCost);
        Assert.Same(route.Nodes[0], repeated.Nodes[0]);
        Assert.Same(route.Relations[0], repeated.Relations[0]);
    }

    [Fact]
    public void WeightedPathBuildersValidateOptionsAndAlgorithmSelection()
    {
        var context = new SocialContext(new QueryOnlyProvider());
        var path = context.People.Match(person => person.Id == "a")
            .ShortestPathTo(context.People.Match(person => person.Id == "b"), context.Knows);

        Assert.Equal(GraphAnalyticsAlgorithm.Dijkstra,
            path.Dijkstra().WeightedBy(edge => edge.Strength).ToQueryModel().Algorithm);
        var aStar = path.AStar(person => person.Latitude, person => person.Longitude).ToQueryModel();
        Assert.Equal(GraphAnalyticsAlgorithm.AStar, aStar.Algorithm);
        Assert.Equal("Latitude", aStar.EffectiveConfiguration["latitudeProperty"]);
        Assert.Equal("Longitude", aStar.EffectiveConfiguration["longitudeProperty"]);
        Assert.Equal(3, path.Yen(3).ToQueryModel().EffectiveConfiguration["k"]);
        Assert.Throws<ArgumentOutOfRangeException>(() => path.MaxDepth(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => path.Yen(0));
        Assert.Throws<ArgumentException>(() => path.WeightedBy(edge => edge.Note));
        Assert.Throws<ArgumentNullException>(() => path.AStar<double, double>(null!, person => person.Longitude));
        Assert.Throws<ArgumentNullException>(() => path.AStar<double, double>(person => person.Latitude, null!));
    }

    [Fact]
    public void QueryAndResultValidationRejectInvalidValuesAndNonNumericWeights()
    {
        var context = new SocialContext(new QueryOnlyProvider());
        var query = context.People.Query().Analyze(context.Knows).PageRank();
        var nonNumeric = context.People.Query().Analyze(context.Knows).Louvain();
        var record = new GraphAnalyticsRecord<Person>(
            new Person("person-1", "Ada"),
            new Dictionary<string, object?> { ["score"] = 0.75, ["communityId"] = 42L });

        Assert.Throws<ArgumentException>(() => query.OnProjection(" "));
        Assert.Throws<ArgumentOutOfRangeException>(() => query.Top(0));
        Assert.Throws<ArgumentException>(() => nonNumeric.WeightedBy(edge => edge.Note));
        Assert.Equal(0.75, record.Score);
        Assert.Equal(42, record.CommunityId);
    }

    [Fact]
    public void TypedOptionsValidateAndProduceProviderConfiguration()
    {
        var context = new SocialContext(new QueryOnlyProvider());
        var pageRank = context.People.Query().Analyze(context.Knows)
            .PageRank(new PageRankOptions(0.9, 30, 0.001, 4))
            .ToQueryModel();
        var louvain = context.People.Query().Analyze(context.Knows)
            .Louvain(new LouvainOptions(5, 8, 0.01, true, 2))
            .ToQueryModel();

        Assert.Equal(0.9, pageRank.EffectiveConfiguration["dampingFactor"]);
        Assert.Equal(30, pageRank.EffectiveConfiguration["maxIterations"]);
        Assert.Equal(5, louvain.EffectiveConfiguration["maxLevels"]);
        Assert.Equal(true, louvain.EffectiveConfiguration["includeIntermediateCommunities"]);

        var builder = context.People.Query().Analyze(context.Knows);
        Assert.Throws<ArgumentNullException>(() => builder.PageRank(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.PageRank(new PageRankOptions(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.PageRank(new PageRankOptions(MaximumIterations: 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.PageRank(new PageRankOptions(Tolerance: 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.PageRank(new PageRankOptions(Concurrency: 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Louvain(new LouvainOptions(MaximumLevels: 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Louvain(new LouvainOptions(MaximumIterations: 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Louvain(new LouvainOptions(Tolerance: 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Louvain(new LouvainOptions(Concurrency: -1)));
        Assert.Throws<InvalidOperationException>(() => builder.Louvain().Configure(new PageRankOptions()));
        Assert.Throws<InvalidOperationException>(() => builder.PageRank().Configure(new LouvainOptions()));
    }

    [Fact]
    public void CompiledAnalyticsFactoriesReuseShapeWithRuntimeValuesAndStableKeys()
    {
        System.Linq.Expressions.Expression<Func<SocialContext, int, GraphAnalyticsQuery<Person, Knows>>> expression =
            (database, count) => database.People.Query().Analyze(database.Knows).PageRank().Top(count);
        var compiled = NodalCompiledAnalyticsQuery.Compile(expression);

        var context = new SocialContext(new QueryOnlyProvider());
        Assert.Equal(12, compiled(context, 12).ToQueryModel().Limit);
        Assert.Equal(
            NodalCompiledAnalyticsQuery.CreateCacheKey(expression),
            NodalCompiledAnalyticsQuery.CreateCacheKey(expression));
        Assert.Equal(64, NodalCompiledAnalyticsQuery.CreateCacheKey(expression).Length);
        Assert.Throws<ArgumentNullException>(() => NodalCompiledAnalyticsQuery.CreateCacheKey(null!));
        Assert.Throws<ArgumentNullException>(() =>
            NodalCompiledAnalyticsQuery.Compile<SocialContext, Person, Knows>(null!));

        var parameterless = NodalCompiledAnalyticsQuery.Compile(
            (SocialContext database) => database.People.Query().Analyze(database.Knows).PageRank());
        Assert.Equal(GraphAnalyticsAlgorithm.PageRank, parameterless(context).ToQueryModel().Algorithm);
    }

    [Fact]
    public void AnalyticsRuntimeContractsExposeCompleteImmutableMetadata()
    {
        var projection = new GraphProjectionDefinition("social", "Person", "KNOWS", false, "strength");
        var snapshot = new GraphAnalyticsRuntimeSnapshot(
            "2.13.0",
            new HashSet<string> { "gds.pageRank.stream" },
            new HashSet<string> { projection.Name },
            new HashSet<GraphAnalyticsAlgorithm> { GraphAnalyticsAlgorithm.PageRank },
            true);

        Assert.Equal("social", projection.Name);
        Assert.Equal("Person", projection.NodeType);
        Assert.Equal("KNOWS", projection.RelationshipType);
        Assert.False(projection.Directed);
        Assert.Equal("strength", projection.WeightProperty);
        Assert.Equal("2.13.0", snapshot.ProviderVersion);
        Assert.Contains("gds.pageRank.stream", snapshot.Procedures);
        Assert.Contains("social", snapshot.Projections);
        Assert.Contains(GraphAnalyticsAlgorithm.PageRank, snapshot.Algorithms);
        Assert.True(snapshot.IsLiveDiscovery);
    }

    [Fact]
    public void LegacyAnalyticsRecordConstructorsAndDeconstructorsRemainCompatible()
    {
        var nodes = new GraphQueryModel("Person", "node", null, [], null, []);
        var legacy = new GraphAnalyticsQueryModel(
            GraphAnalyticsAlgorithm.PageRank,
            GraphAnalyticsFamily.Centrality,
            nodes,
            "KNOWS",
            true,
            "social",
            "strength",
            20,
            new Dictionary<string, object?> { ["maxIterations"] = 20 },
            null,
            null);
        legacy.Deconstruct(
            out var algorithm,
            out var family,
            out var deconstructedNodes,
            out var relationshipType,
            out var directed,
            out var projectionName,
            out var weightProperty,
            out var limit,
            out var configuration,
            out var targetNodes,
            out var maxDepth);
        var projection = new GraphProjectionDefinition("social", "Person", "KNOWS", true, "strength");
        projection.Deconstruct(
            out var projectionNameValue,
            out var nodeType,
            out var projectionRelationship,
            out var projectionDirected,
            out var projectionWeight);

        Assert.Equal(GraphAnalyticsAlgorithm.PageRank, algorithm);
        Assert.Equal(GraphAnalyticsFamily.Centrality, family);
        Assert.Same(nodes, deconstructedNodes);
        Assert.Equal("KNOWS", relationshipType);
        Assert.True(directed);
        Assert.Equal("social", projectionName);
        Assert.Equal("strength", weightProperty);
        Assert.Equal(20, limit);
        Assert.NotNull(configuration);
        Assert.Null(targetNodes);
        Assert.Null(maxDepth);
        Assert.Equal("social", projectionNameValue);
        Assert.Equal("Person", nodeType);
        Assert.Equal("KNOWS", projectionRelationship);
        Assert.True(projectionDirected);
        Assert.Equal("strength", projectionWeight);
    }

    [Fact]
    public async Task MultiRelationScopeIsCanonicalWeightedAndExecutable()
    {
        var provider = new AnalyticsProvider([GraphAnalyticsAlgorithm.PageRank], supportsWeights: true);
        var context = new SocialContext(provider);
        var scope = GraphAnalyticsScope.For<Person>("author-influence")
            .Include(context.Likes, edge => edge.Similarity, 0.30)
            .Include(context.Knows, edge => edge.Strength, 0.70);

        var query = context.People.Query().Analyze(scope)
            .PageRank(new PageRankOptions(0.85, 20, 0.001, 2))
            .Top(10);
        var result = await query.ToListAsync();
        var model = query.ToQueryModel();

        Assert.Single(result);
        Assert.StartsWith("author-influence-", model.ProjectionName, StringComparison.Ordinal);
        Assert.Equal(["KNOWS", "LIKES"], model.EffectiveRelationships.Select(item => item.RelationshipType));
        Assert.Equal([0.70, 0.30], model.EffectiveRelationships.Select(item => item.Coefficient));
        Assert.Equal(10, model.Limit);
        Assert.Equal(20, model.EffectiveConfiguration["maxIterations"]);
        Assert.Equal(model, provider.Compiler.Query);
    }

    [Fact]
    public async Task MultiRelationExecutionEnsuresProviderProjectionBeforeCompilation()
    {
        var provider = new AnalyticsProvider(
            [GraphAnalyticsAlgorithm.PageRank],
            supportsWeights: false,
            supportsProjectionManagement: true);
        var context = new SocialContext(provider);
        var scope = GraphAnalyticsScope.For<Person>("author-influence")
            .Include(context.Likes)
            .Include(context.Knows);

        await context.People.Query().Analyze(scope).PageRank().ToListAsync();

        Assert.NotNull(provider.Runtime.Projection);
        Assert.StartsWith("author-influence-", provider.Runtime.Projection.Name, StringComparison.Ordinal);
        Assert.Equal(["KNOWS", "LIKES"],
            provider.Runtime.Projection.EffectiveRelationships.Select(item => item.RelationshipType));
        Assert.Equal("projection", provider.Runtime.Events[0]);
        Assert.NotNull(provider.Compiler.Query);
        Assert.Equal(1, provider.ScopeValidationCount);
    }

    [Fact]
    public void MultiRelationScopeValidatesShapeAndProducesStableBinding()
    {
        var context = new SocialContext(new QueryOnlyProvider());
        var first = GraphAnalyticsScope.For<Person>("influence")
            .Include(context.Likes)
            .Include(context.Knows);
        var second = GraphAnalyticsScope.For<Person>("influence")
            .Include(context.Knows)
            .Include(context.Likes);
        var firstModel = context.People.Query().Analyze(first).PageRank().ToQueryModel();
        var secondModel = context.People.Query().Analyze(second).PageRank().ToQueryModel();

        Assert.Equal(
            GraphAnalyticsBindingKey.Create(firstModel).Fingerprint,
            GraphAnalyticsBindingKey.Create(secondModel).Fingerprint);
        var binding = GraphAnalyticsBindingKey.Create(firstModel);
        Assert.Equal(16, binding.Fingerprint.Length);
        Assert.Equal(GraphAnalyticsAlgorithm.PageRank, binding.Algorithm);
        Assert.Equal("Person", binding.NodeType);
        Assert.Equal("1", binding.ContractVersion);
        Assert.Equal(["KNOWS", "LIKES"], binding.Relationships.Select(item => item.RelationshipType));
        Assert.Throws<InvalidOperationException>(() => first.Include(context.Knows));
        Assert.Throws<ArgumentOutOfRangeException>(() => GraphAnalyticsScope.For<Person>("x").Include(context.Knows, 0));
        Assert.Throws<ArgumentException>(() => GraphAnalyticsScope.For<Person>("x").Include(context.Knows, edge => edge.Note));
        Assert.Throws<InvalidOperationException>(() => context.People.Query()
            .Analyze(GraphAnalyticsScope.For<Person>("empty")).PageRank());
        Assert.Throws<ArgumentException>(() => context.People.Query().Analyze(first).Using(GraphAnalyticsAlgorithm.ShortestPath));
        Assert.Throws<ArgumentException>(() => GraphAnalyticsBindingKey.Create(firstModel, " "));
    }

    [Fact]
    public void MultiRelationQueryValidatesExecutionOptions()
    {
        var context = new SocialContext(new QueryOnlyProvider());
        var scope = GraphAnalyticsScope.For<Person>("influence").Include(context.Knows);
        var query = context.People.Query().Analyze(scope).PageRank();

        Assert.Throws<ArgumentOutOfRangeException>(() => query.Top(0));
        Assert.Throws<ArgumentNullException>(() => query.Configure(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => query.Configure(new PageRankOptions(1)));
        Assert.Throws<InvalidOperationException>(() => context.People.Query().Analyze(scope)
            .Using(GraphAnalyticsAlgorithm.Louvain).Configure(new PageRankOptions()));
    }

    [GraphNode("Person")]
    private sealed record Person(
        [property: GraphKey] string Id,
        string Name,
        double Latitude = 0,
        double Longitude = 0);

    [GraphRelation("KNOWS")]
    private sealed record Knows([property: GraphProperty("strength")] double Strength, string Note);

    [GraphRelation("LIKES", Directed = false)]
    private sealed record Likes([property: GraphProperty("similarity")] double Similarity);

    private sealed class SocialContext(IGraphProvider provider) : NodalContext(provider)
    {
        public GraphSet<Person> People => Set<Person>();
        public RelationSet<Person, Knows, Person> Knows => Relations<Person, Knows, Person>();
        public RelationSet<Person, Likes, Person> Likes => Relations<Person, Likes, Person>();
    }

    private sealed class QueryOnlyProvider : IGraphProvider
    {
        public IGraphQueryCompiler QueryCompiler => throw new NotSupportedException();
        public IGraphCommandExecutor CommandExecutor => throw new NotSupportedException();
        public IGraphResultMaterializer ResultMaterializer => throw new NotSupportedException();
    }

    private sealed class AnalyticsProvider : IGraphProvider, IGraphAnalyticsProvider, IGraphAnalyticsRuntimeProvider,
        IGraphAnalyticsScopeCapabilityProvider
    {
        public AnalyticsProvider(
            IEnumerable<GraphAnalyticsAlgorithm> algorithms,
            bool supportsWeights,
            bool? detailSupportsWeights = null,
            bool supportsProjectionManagement = false)
        {
            var supported = algorithms.ToHashSet();
            Compiler = new AnalyticsCompiler();
            Executor = new AnalyticsCommandExecutor();
            AnalyticsCapabilities = new GraphAnalyticsCapabilities
            {
                ProviderName = "TestProvider",
                Algorithms = supported,
                SupportsWeightedRelationships = supportsWeights,
                SupportsProjectionManagement = supportsProjectionManagement,
                AlgorithmDetails = detailSupportsWeights is null
                    ? new Dictionary<GraphAnalyticsAlgorithm, GraphAlgorithmCapability>()
                    : supported.ToDictionary(
                        algorithm => algorithm,
                        algorithm => new GraphAlgorithmCapability(
                            algorithm,
                            GraphAnalyticsAvailability.Native,
                            GraphCapabilityVerification.Contract,
                            "Test requirement",
                            detailSupportsWeights.Value)),
            };
        }

        public AnalyticsCompiler Compiler { get; }
        public AnalyticsCommandExecutor Executor { get; }
        public RecordingAnalyticsRuntime Runtime { get; } = new();
        public int ScopeValidationCount { get; private set; }
        public IGraphQueryCompiler QueryCompiler => throw new NotSupportedException();
        public IGraphCommandExecutor CommandExecutor => Executor;
        public IGraphResultMaterializer ResultMaterializer { get; } = new JsonGraphResultMaterializer();
        public IGraphAnalyticsCompiler AnalyticsCompiler => Compiler;
        public GraphAnalyticsCapabilities AnalyticsCapabilities { get; }
        public IGraphAnalyticsRuntime AnalyticsRuntime => Runtime;

        public void ValidateAnalyticsScope(GraphAnalyticsQueryModel query) => ScopeValidationCount++;
    }

    private sealed class RecordingAnalyticsRuntime : IGraphAnalyticsRuntime
    {
        public GraphProjectionDefinition? Projection { get; private set; }
        public List<string> Events { get; } = [];

        public ValueTask<GraphAnalyticsRuntimeSnapshot> DiscoverAsync(
            bool forceRefresh = false,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask EnsureProjectionAsync(
            GraphProjectionDefinition projection,
            CancellationToken cancellationToken = default)
        {
            Projection = projection;
            Events.Add("projection");
            return ValueTask.CompletedTask;
        }

        public ValueTask DropProjectionAsync(string name, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class AnalyticsCompiler : IGraphAnalyticsCompiler
    {
        public GraphAnalyticsQueryModel? Query { get; private set; }

        public GraphCommand Compile(GraphAnalyticsQueryModel query)
        {
            Query = query;
            return new GraphCommand("analytics command", new Dictionary<string, object?>());
        }
    }

    private sealed class AnalyticsCommandExecutor : IGraphCommandExecutor
    {
        public GraphCommand? Command { get; private set; }
        public int CallCount { get; private set; }

        public ValueTask<GraphQueryResult> ExecuteAsync(
            GraphCommand command,
            CancellationToken cancellationToken = default)
        {
            Command = command;
            CallCount++;
            var node = new GraphNodeRecord(
                "Person", "person-1", new Dictionary<string, object?> { ["Name"] = "Ada" });
            var target = new GraphNodeRecord(
                "Person", "person-2", new Dictionary<string, object?> { ["Name"] = "Alan" });
            var relation = new GraphRelationRecord(
                "KNOWS", "edge-1", "person-1", "person-2",
                new Dictionary<string, object?> { ["Strength"] = 1.0, ["Note"] = "friend" });
            var row = new GraphResultRow(node, new Dictionary<string, object?> { ["score"] = 0.9 });
            var route = new GraphRouteRecord([node, target], [relation], 2.5);
            return ValueTask.FromResult(new GraphQueryResult([node], Rows: [row], Routes: [route]));
        }
    }
}
