using Nodal.Core.Analytics;
using Nodal.Core.Query;

namespace Nodal.Neo4j.Tests;

public sealed class Neo4jAnalyticsCompilerTests
{
    [Fact]
    public void EveryDeclaredCentralityAndCommunityAlgorithmHasACompilerShape()
    {
        var compiler = new Neo4jAnalyticsCompiler();
        var algorithms = Enum.GetValues<GraphAnalyticsAlgorithm>()
            .Where(algorithm => algorithm < GraphAnalyticsAlgorithm.ShortestPath)
            .ToArray();

        var commands = algorithms.Select(algorithm => compiler.Compile(Model(algorithm))).ToArray();

        Assert.Equal(28, commands.Length);
        Assert.All(commands, command => Assert.StartsWith("CALL gds.", command.Text, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(GraphAnalyticsAlgorithm.PageRank, "gds.pageRank", "score")]
    [InlineData(GraphAnalyticsAlgorithm.ArticleRank, "gds.articleRank", "score")]
    [InlineData(GraphAnalyticsAlgorithm.BetweennessCentrality, "gds.betweenness", "score")]
    [InlineData(GraphAnalyticsAlgorithm.Louvain, "gds.louvain", "communityId")]
    [InlineData(GraphAnalyticsAlgorithm.Leiden, "gds.leiden", "communityId")]
    [InlineData(GraphAnalyticsAlgorithm.WeaklyConnectedComponents, "gds.wcc", "componentId")]
    [InlineData(GraphAnalyticsAlgorithm.StronglyConnectedComponents, "gds.scc", "componentId")]
    [InlineData(GraphAnalyticsAlgorithm.TriangleCount, "gds.triangleCount", "triangleCount")]
    [InlineData(GraphAnalyticsAlgorithm.Hits, "gds.hits", "values")]
    [InlineData(GraphAnalyticsAlgorithm.Hdbscan, "gds.hdbscan", "clusterId")]
    public void CompileMapsAlgorithmsToParameterizedGdsCalls(
        GraphAnalyticsAlgorithm algorithm,
        string procedure,
        string metric)
    {
        var query = Model(algorithm) with
        {
            RelationshipWeightProperty = "strength",
            Limit = 12,
            Configuration = new Dictionary<string, object?> { ["concurrency"] = 4 },
        };

        var command = new Neo4jAnalyticsCompiler().Compile(query);

        Assert.StartsWith($"CALL {procedure}.stream($nodal_projection, $nodal_configuration)", command.Text);
        Assert.Contains($"{metric}: {metric}", command.Text, StringComparison.Ordinal);
        Assert.EndsWith("LIMIT 12", command.Text, StringComparison.Ordinal);
        Assert.Equal("social", command.Parameters["nodal_projection"]);
        var configuration = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(command.Parameters["nodal_configuration"]);
        Assert.Equal("strength", configuration["relationshipWeightProperty"]);
        Assert.Equal(4, configuration["concurrency"]);
    }

    [Fact]
    public void CompilePreservesTypedNodeFilterAndParameters()
    {
        var nodes = new GraphSet<Person>()
            .Match(person => person.Country == "TR" && person.Age >= 18)
            .ToQueryModel();
        var query = Model(GraphAnalyticsAlgorithm.PageRank) with { Nodes = nodes };

        var command = new Neo4jAnalyticsCompiler().Compile(query);

        Assert.Contains(
            "WHERE (nodal_node.`Country` = $p0 AND nodal_node.`Age` >= $p1)",
            command.Text,
            StringComparison.Ordinal);
        Assert.Equal("TR", command.Parameters["p0"]);
        Assert.Equal(18, command.Parameters["p1"]);
        Assert.Contains("ORDER BY nodal_metrics.score DESC", command.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileCoversTheCompleteProviderNeutralPredicateVocabulary()
    {
        GraphPredicate[] predicates =
        [
            new GraphNotPredicate(new GraphNullPredicate("Name", true)),
            new GraphNullPredicate("Name", false),
            new GraphStringPredicate("Name", GraphStringOperator.StartsWith, "p0"),
            new GraphStringPredicate("Name", GraphStringOperator.EndsWith, "p0"),
            new GraphStringPredicate("Name", GraphStringOperator.Contains, "p0"),
            new GraphInPredicate("Id", "p0"),
            new GraphInPredicate("Id", "p0", Negated: true),
            new GraphLogicalPredicate(
                new GraphComparisonPredicate("Age", GraphComparisonOperator.NotEqual, "p0"),
                GraphLogicalOperator.Or,
                new GraphComparisonPredicate("Age", GraphComparisonOperator.GreaterThan, "p0")),
            new GraphComparisonPredicate("Age", GraphComparisonOperator.LessThan, "p0"),
            new GraphComparisonPredicate("Age", GraphComparisonOperator.LessThanOrEqual, "p0"),
        ];

        var commands = predicates.Select(predicate => new Neo4jAnalyticsCompiler().Compile(
            Model(GraphAnalyticsAlgorithm.PageRank) with
            {
                Nodes = new GraphQueryModel(
                    "Person", "node", predicate,
                    [new GraphQueryParameter("p0", 1, typeof(int))], null, []),
            })).ToArray();

        Assert.Contains(commands, command => command.Text.Contains("NOT (", StringComparison.Ordinal));
        Assert.Contains(commands, command => command.Text.Contains("STARTS WITH", StringComparison.Ordinal));
        Assert.Contains(commands, command => command.Text.Contains("NOT IN", StringComparison.Ordinal));
        Assert.Contains(commands, command => command.Text.Contains(" OR ", StringComparison.Ordinal));
    }

    [Fact]
    public void CompileSupportsGraphLevelRowsAndRejectsPathAlgorithms()
    {
        var bridges = new Neo4jAnalyticsCompiler().Compile(Model(GraphAnalyticsAlgorithm.Bridges));

        Assert.Contains("null AS nodal_node", bridges.Text, StringComparison.Ordinal);
        Assert.Contains("remainingSizes: remainingSizes", bridges.Text, StringComparison.Ordinal);
        Assert.Throws<NotSupportedException>(() =>
            new Neo4jAnalyticsCompiler().Compile(Model(GraphAnalyticsAlgorithm.Dijkstra)));
    }

    [Theory]
    [InlineData(GraphAnalyticsAlgorithm.ShortestPath, "shortestPath(")]
    [InlineData(GraphAnalyticsAlgorithm.AllShortestPaths, "allShortestPaths(")]
    [InlineData(GraphAnalyticsAlgorithm.Dijkstra, "gds.shortestPath.dijkstra.stream")]
    [InlineData(GraphAnalyticsAlgorithm.AStar, "gds.shortestPath.astar.stream")]
    [InlineData(GraphAnalyticsAlgorithm.YenKShortestPaths, "gds.shortestPath.yens.stream")]
    public void CompileProducesProviderNativePathCommands(GraphAnalyticsAlgorithm algorithm, string expected)
    {
        var source = new GraphSet<Person>().Match(person => person.Id == "a").ToQueryModel();
        var target = new GraphQueryModel(
            "Person", "node", new GraphComparisonPredicate("Id", GraphComparisonOperator.Equal, "p1"),
            [new GraphQueryParameter("p1", "b", typeof(string))], null, []);
        var model = new GraphAnalyticsQueryModel(
            algorithm, GraphAnalyticsFamily.PathFinding, source, "KNOWS", true, "social",
            RelationshipWeightProperty: algorithm >= GraphAnalyticsAlgorithm.Dijkstra ? "strength" : null,
            Configuration: algorithm == GraphAnalyticsAlgorithm.YenKShortestPaths
                ? new Dictionary<string, object?> { ["k"] = 3 }
                : null,
            TargetNodes: target,
            MaxDepth: 7);

        var command = new Neo4jAnalyticsCompiler().Compile(model);

        Assert.Contains(expected, command.Text, StringComparison.Ordinal);
        Assert.Equal("a", command.Parameters["p0"]);
        Assert.Equal("b", command.Parameters["p1"]);
    }

    [Fact]
    public void CompileRejectsUnknownPathAlgorithmsAndPredicateKinds()
    {
        var compiler = new Neo4jAnalyticsCompiler();
        var path = Model(GraphAnalyticsAlgorithm.PageRank) with
        {
            Family = GraphAnalyticsFamily.PathFinding,
            TargetNodes = new GraphQueryModel("Person", "node", null, [], null, []),
        };
        Assert.Throws<NotSupportedException>(() => compiler.Compile(path));

        AssertInvalidPredicate(new GraphComparisonPredicate("Id", (GraphComparisonOperator)999, "p0"));
        AssertInvalidPredicate(new GraphLogicalPredicate(
            new GraphNullPredicate("Id", true), (GraphLogicalOperator)999, new GraphNullPredicate("Id", false)));
        AssertInvalidPredicate(new GraphStringPredicate("Id", (GraphStringOperator)999, "p0"));
        var unknownNodes = new GraphQueryModel("Person", "node", new UnknownPredicate(), [], null, []);
        Assert.Throws<NotSupportedException>(() => compiler.Compile(
            Model(GraphAnalyticsAlgorithm.PageRank) with { Nodes = unknownNodes }));

        void AssertInvalidPredicate(GraphPredicate predicate)
        {
            var nodes = new GraphQueryModel("Person", "node", predicate,
                [new GraphQueryParameter("p0", "a", typeof(string))], null, []);
            Assert.ThrowsAny<ArgumentException>(() => compiler.Compile(
                Model(GraphAnalyticsAlgorithm.PageRank) with { Nodes = nodes }));
        }
    }

    private static GraphAnalyticsQueryModel Model(GraphAnalyticsAlgorithm algorithm) => new(
        algorithm,
        algorithm <= GraphAnalyticsAlgorithm.PageRank
            ? GraphAnalyticsFamily.Centrality
            : GraphAnalyticsFamily.CommunityDetection,
        new GraphQueryModel("Person", "node", null, [], null, []),
        "KNOWS",
        true,
        "social");

    private sealed record Person(string Id, string Country, int Age);

    private sealed record UnknownPredicate : GraphPredicate;
}
