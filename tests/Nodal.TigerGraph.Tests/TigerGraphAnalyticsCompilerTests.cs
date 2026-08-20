using Nodal.Core.Analytics;
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

        Assert.Throws<NotSupportedException>(() => compiler.Compile(model));
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
}
