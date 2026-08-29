using Neo4j.Driver;
using Nodal.Core.Analytics;
using Nodal.Core.Migrations;
using Nodal.Core.Query;
using NSubstitute;

namespace Nodal.Neo4j.Tests;

public sealed class Neo4jAnalyticsRuntimeTests
{
    [Fact]
    public async Task DiscoveryReadsVersionProceduresAndProjectionsAndCachesSnapshot()
    {
        var (driver, session, runner) = RuntimeHarness();
        runner.RunAsync(Arg.Any<string>(), Arg.Any<IDictionary<string, object>>())
            .Returns(call => Cursor(call.ArgAt<string>(0) switch
            {
                var text when text.Contains("gds.version", StringComparison.Ordinal) => "2.13.0",
                var text when text.Contains("gds.list", StringComparison.Ordinal) => "gds.pageRank.stream",
                _ => "social",
            }));
        using var runtime = new Neo4jAnalyticsRuntime(
            driver, "neo4j", new HashSet<GraphAnalyticsAlgorithm> { GraphAnalyticsAlgorithm.PageRank },
            TimeSpan.FromMinutes(5));

        var first = await runtime.DiscoverAsync();
        var cached = await runtime.DiscoverAsync();

        Assert.Same(first, cached);
        Assert.Equal("2.13.0", first.ProviderVersion);
        Assert.Contains("gds.pageRank.stream", first.Procedures);
        Assert.Contains("social", first.Projections);
        Assert.True(first.IsLiveDiscovery);
        await runner.Received(3).RunAsync(Arg.Any<string>(), Arg.Any<IDictionary<string, object>>());
        await session.Received(3).DisposeAsync();

        await runtime.DiscoverAsync(forceRefresh: true);
        await runner.Received(6).RunAsync(Arg.Any<string>(), Arg.Any<IDictionary<string, object>>());
    }

    [Fact]
    public async Task ProjectionLifecycleCreatesOnlyWhenMissingThenDropsAndValidatesInput()
    {
        var (driver, _, runner) = RuntimeHarness();
        runner.RunAsync(Arg.Any<string>(), Arg.Any<IDictionary<string, object>>())
            .Returns(call => Cursor(call.ArgAt<string>(0).Contains("graph.exists", StringComparison.Ordinal)
                ? false
                : "social"));
        using var runtime = new Neo4jAnalyticsRuntime(
            driver, null, new HashSet<GraphAnalyticsAlgorithm>(), TimeSpan.Zero);

        await runtime.EnsureProjectionAsync(new GraphProjectionDefinition(
            "social", "Person", "KNOWS", Directed: false, WeightProperty: "strength"));
        await runtime.DropProjectionAsync("social");

        await runner.Received().RunAsync(
            Arg.Is<string>(text => text.Contains("gds.graph.project", StringComparison.Ordinal)),
            Arg.Is<IDictionary<string, object>>(parameters =>
                Equals(parameters["name"], "social") && Equals(parameters["nodeType"], "Person")));
        await runner.Received().RunAsync(
            Arg.Is<string>(text => text.Contains("gds.graph.drop", StringComparison.Ordinal)),
            Arg.Any<IDictionary<string, object>>());
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await runtime.EnsureProjectionAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(async () => await runtime.EnsureProjectionAsync(
            new GraphProjectionDefinition(" ", "Person", "KNOWS")));
        await Assert.ThrowsAsync<ArgumentException>(async () => await runtime.EnsureProjectionAsync(
            new GraphProjectionDefinition("p", " ", "KNOWS")));
        await Assert.ThrowsAsync<ArgumentException>(async () => await runtime.EnsureProjectionAsync(
            new GraphProjectionDefinition("p", "Person", " ")));
        await Assert.ThrowsAsync<ArgumentException>(async () => await runtime.EnsureProjectionAsync(
            new GraphProjectionDefinition("p", "Person", "KNOWS", Relationships:
            [
                new GraphProjectionRelationshipDefinition("KNOWS"),
                new GraphProjectionRelationshipDefinition("KNOWS"),
            ])));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await runtime.EnsureProjectionAsync(
            new GraphProjectionDefinition("p", "Person", "KNOWS", Relationships:
            [new GraphProjectionRelationshipDefinition("KNOWS", Coefficient: 0)])));
        await Assert.ThrowsAsync<ArgumentException>(async () => await runtime.DropProjectionAsync(" "));
    }

    [Fact]
    public async Task ProjectionLifecycleTransportsEveryCanonicalRelationship()
    {
        var (driver, _, runner) = RuntimeHarness();
        runner.RunAsync(Arg.Any<string>(), Arg.Any<IDictionary<string, object>>())
            .Returns(call => Cursor(call.ArgAt<string>(0).Contains("graph.exists", StringComparison.Ordinal)
                ? false
                : "author-influence"));
        using var runtime = new Neo4jAnalyticsRuntime(
            driver, null, new HashSet<GraphAnalyticsAlgorithm>(), TimeSpan.Zero);
        var projection = new GraphProjectionDefinition(
            "author-influence",
            "Author",
            "CO_AUTHORED",
            Relationships:
            [
                new GraphProjectionRelationshipDefinition("CO_AUTHORED", false, "paperCount"),
                new GraphProjectionRelationshipDefinition("SHARES_INTEREST", false, "similarity"),
            ]);

        await runtime.EnsureProjectionAsync(projection);

        await runner.Received().RunAsync(
            Arg.Is<string>(text => text.Contains("gds.graph.project", StringComparison.Ordinal)),
            Arg.Is<IDictionary<string, object>>(parameters => HasRelationship(parameters, "CO_AUTHORED", "paperCount") &&
                HasRelationship(parameters, "SHARES_INTEREST", "similarity")));
    }

    [Fact]
    public async Task ProjectionReuseSkipsCreationAndCancellationStopsDiscovery()
    {
        var (driver, _, runner) = RuntimeHarness();
        var existsCursor = Cursor(true);
        runner.RunAsync(Arg.Any<string>(), Arg.Any<IDictionary<string, object>>()).Returns(existsCursor);
        using var runtime = new Neo4jAnalyticsRuntime(
            driver, null, new HashSet<GraphAnalyticsAlgorithm>(), TimeSpan.Zero);

        await runtime.EnsureProjectionAsync(new GraphProjectionDefinition("social", "Person", "KNOWS"));
        await runner.DidNotReceive().RunAsync(
            Arg.Is<string>(text => text.Contains("graph.project", StringComparison.Ordinal)),
            Arg.Any<IDictionary<string, object>>());

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await runtime.DiscoverAsync(cancellationToken: cancellation.Token));
        Assert.Throws<ArgumentNullException>(() => new Neo4jAnalyticsRuntime(
            null!, null, new HashSet<GraphAnalyticsAlgorithm>(), TimeSpan.Zero));
        Assert.Throws<ArgumentNullException>(() => new Neo4jAnalyticsRuntime(
            driver, null, null!, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Neo4jAnalyticsRuntime(
            driver, null, new HashSet<GraphAnalyticsAlgorithm>(), TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void ProviderRejectsScopeSemanticsThatNativeProjectionCannotPreserve()
    {
        var driver = Substitute.For<IDriver>();
        var provider = new Neo4jProvider(driver, graphDataScienceEnabled: true);
        var baseModel = new GraphAnalyticsQueryModel(
            GraphAnalyticsAlgorithm.PageRank,
            GraphAnalyticsFamily.Centrality,
            new GraphQueryModel("Author", "node", null, [], null, []),
            "CO_AUTHORED",
            false,
            "author-influence",
            Relationships:
            [
                new GraphAnalyticsRelationshipDefinition("CO_AUTHORED", false),
                new GraphAnalyticsRelationshipDefinition("SHARES_INTEREST", false),
            ]);

        provider.ValidateAnalyticsScope(baseModel);
        var coefficient = Assert.Throws<NodalCapabilityNotSupportedException>(() => provider.ValidateAnalyticsScope(
            baseModel with
            {
                Relationships =
                [
                    new GraphAnalyticsRelationshipDefinition("CO_AUTHORED", false, Coefficient: 0.7),
                    new GraphAnalyticsRelationshipDefinition("SHARES_INTEREST", false, Coefficient: 0.3),
                ],
            }));
        var weightShape = Assert.Throws<NodalCapabilityNotSupportedException>(() => provider.ValidateAnalyticsScope(
            baseModel with
            {
                Relationships =
                [
                    new GraphAnalyticsRelationshipDefinition("CO_AUTHORED", false, "paperCount"),
                    new GraphAnalyticsRelationshipDefinition("SHARES_INTEREST", false, "similarity"),
                ],
            }));
        var pathShape = Assert.Throws<NodalCapabilityNotSupportedException>(() => provider.ValidateAnalyticsScope(
            baseModel with { Family = GraphAnalyticsFamily.PathFinding }));

        Assert.Equal("NODAL-NEO4J-ANALYTICS-COEFFICIENT", coefficient.CapabilityCode);
        Assert.Equal("NODAL-NEO4J-ANALYTICS-WEIGHT-SHAPE", weightShape.CapabilityCode);
        Assert.Equal("NODAL-ANALYTICS-PATH-MULTI-RELATION", pathShape.CapabilityCode);
        Assert.NotNull(provider.SchemaIntrospector);
    }

    private static (IDriver Driver, IAsyncSession Session, IAsyncQueryRunner Runner) RuntimeHarness()
    {
        var driver = Substitute.For<IDriver>();
        var session = Substitute.For<IAsyncSession>();
        var runner = Substitute.For<IAsyncQueryRunner>();
        driver.AsyncSession(Arg.Any<Action<SessionConfigBuilder>>()).Returns(session);
        session.ExecuteReadAsync(
                Arg.Any<Func<IAsyncQueryRunner, Task<List<IRecord>>>>(),
                Arg.Any<Action<TransactionConfigBuilder>>())
            .Returns(call => call.ArgAt<Func<IAsyncQueryRunner, Task<List<IRecord>>>>(0)(runner));
        session.ExecuteWriteAsync(
                Arg.Any<Func<IAsyncQueryRunner, Task<List<IRecord>>>>(),
                Arg.Any<Action<TransactionConfigBuilder>>())
            .Returns(call => call.ArgAt<Func<IAsyncQueryRunner, Task<List<IRecord>>>>(0)(runner));
        return (driver, session, runner);
    }

    private static IResultCursor Cursor(object value)
    {
        var record = Substitute.For<IRecord>();
        record.Values.Returns(new Dictionary<string, object> { ["value"] = value });
        record["value"].Returns(value);
        var cursor = Substitute.For<IResultCursor>();
        cursor.GetAsyncEnumerator(Arg.Any<CancellationToken>())
            .Returns(_ => new TestAsyncEnumerator<IRecord>([record]));
        return cursor;
    }

    private static bool HasRelationship(
        IDictionary<string, object> parameters,
        string relationshipType,
        string weightProperty)
    {
        var relationships = Assert.IsType<Dictionary<string, object>>(parameters["relationships"]);
        var definition = Assert.IsType<Dictionary<string, object?>>(relationships[relationshipType]);
        return Equals(definition["orientation"], "UNDIRECTED") &&
            Assert.IsType<string[]>(definition["properties"]).SequenceEqual([weightProperty]);
    }

    private sealed class TestAsyncEnumerator<T>(IReadOnlyList<T> items) : IAsyncEnumerator<T>
    {
        private int index = -1;

        public T Current => items[index];

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public ValueTask<bool> MoveNextAsync()
        {
            index++;
            return ValueTask.FromResult(index < items.Count);
        }
    }
}
