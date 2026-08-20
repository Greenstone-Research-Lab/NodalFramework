using Nodal.Core.Providers;
using Nodal.Core.Query;

namespace Nodal.TigerGraph.Tests;

public sealed class TigerGraphQueryCompilerTests
{
    [Fact]
    public void CompileProducesParameterizedInterpretedGsql()
    {
        const string personId = "person-42";
        var model = new GraphSet<Person>()
            .Match(person => person.Id == personId && person.Age >= 18)
            .Take(5)
            .ToQueryModel();

        var command = new TigerGraphQueryCompiler("SocialGraph").Compile(model);

        Assert.Equal(
            "INTERPRET QUERY (STRING p0, INT p1) FOR GRAPH SocialGraph { result = SELECT node FROM Person:node WHERE (node.Id == p0 AND node.Age >= p1) LIMIT 5; PRINT result; }",
            command.Text);
        Assert.Equal(personId, command.Parameters["p0"]);
        Assert.Equal(18, command.Parameters["p1"]);
    }

    [Fact]
    public void ConstructorRejectsUnsafeGraphNames()
    {
        Assert.Throws<ArgumentException>(() => new TigerGraphQueryCompiler("graph; DROP ALL"));
    }

    [Fact]
    public void CompileProducesDirectedTraversalAndReturnsTargetNode()
    {
        var model = new GraphQueryModel(
            "Person",
            "node",
            new GraphComparisonPredicate("Id", GraphComparisonOperator.Equal, "p0"),
            [
                new GraphQueryParameter("p0", "person-42", typeof(string)),
                new GraphQueryParameter("p1", "TR", typeof(string)),
            ],
            5,
            [
                new GraphTraversalStep(
                    "WORKS_AT",
                    "Company",
                    "node",
                    "relation1",
                    "node1",
                    GraphTraversalDirection.Outgoing,
                    new GraphComparisonPredicate("Country", GraphComparisonOperator.Equal, "p1")),
            ]);

        var command = new TigerGraphQueryCompiler("SocialGraph").Compile(model);

        Assert.Equal(
            "INTERPRET QUERY (STRING p0, STRING p1) FOR GRAPH SocialGraph { result = SELECT node1 FROM Person:node -(WORKS_AT:relation1)-> Company:node1 WHERE node.Id == p0 AND node1.Country == p1 LIMIT 5; PRINT result; }",
            command.Text);
    }

    [Theory]
    [InlineData(GraphTraversalDirection.Incoming, "<-(WORKS_AT:relation1)- Company:node1")]
    [InlineData(GraphTraversalDirection.Undirected, "-(WORKS_AT:relation1)- Company:node1")]
    public void CompileHonorsTraversalDirection(GraphTraversalDirection direction, string expectedPattern)
    {
        var model = new GraphQueryModel(
            "Person",
            "node",
            null,
            [],
            null,
            [new GraphTraversalStep("WORKS_AT", "Company", "node", "relation1", "node1", direction, null)]);

        var command = new TigerGraphQueryCompiler("SocialGraph").Compile(model);

        Assert.Contains(expectedPattern, command.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void CompilePathProjectionAccumulatesEndpointsAndRelationship()
    {
        var model = new GraphQueryModel(
            "Person",
            "node",
            null,
            [new GraphQueryParameter("p0", 2020, typeof(int))],
            2,
            [new GraphTraversalStep(
                "KNOWS",
                "Person",
                "node",
                "relation1",
                "node1",
                GraphTraversalDirection.Outgoing,
                null,
                new GraphComparisonPredicate("since_year", GraphComparisonOperator.GreaterThanOrEqual, "p0"))],
            GraphQueryProjection.Path);

        var command = new TigerGraphQueryCompiler("SocialGraph").Compile(model);

        Assert.Equal(
            "INTERPRET QUERY (INT p0) FOR GRAPH SocialGraph { ListAccum<EDGE> @@nodal_relations; nodal_sources = SELECT node FROM Person:node -(KNOWS:relation1)-> Person:node1 WHERE relation1.since_year >= p0 ACCUM @@nodal_relations += relation1 LIMIT 2; nodal_targets = SELECT node1 FROM Person:node -(KNOWS:relation1)-> Person:node1 WHERE relation1.since_year >= p0 LIMIT 2; PRINT nodal_sources, @@nodal_relations AS nodal_relations, nodal_targets; }",
            command.Text);
    }


    [Fact]
    public void CompileSupportsRichPredicatesOrderingAndOffset()
    {
        string[] ids = ["a", "b"];
        var model = new GraphSet<RichPerson>().Query()
            .Where(person => ids.Contains(person.Id) && person.Name.EndsWith("da"))
            .OrderBy(person => person.Name)
            .Skip(2)
            .Take(3)
            .ToQueryModel();

        var command = new TigerGraphQueryCompiler("SocialGraph").Compile(model);

        Assert.Contains("SET<STRING> p0", command.Text, StringComparison.Ordinal);
        Assert.Contains("node.Id IN p0", command.Text, StringComparison.Ordinal);
        Assert.Contains("node.Name LIKE \"%\" + p1", command.Text, StringComparison.Ordinal);
        Assert.Contains("ORDER BY node.Name ASC LIMIT 3 OFFSET 2", command.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileRejectsOptionalTraversalAndOffsetWithoutLimit()
    {
        var optional = new GraphQueryModel("Person", "node", null, [], null,
            [new GraphTraversalStep("KNOWS", "Person", "node", "relation1", "node1",
                GraphTraversalDirection.Outgoing, null, null, 1, 1, true)]);
        var offsetOnly = new GraphSet<Person>().Query().OrderBy(person => person.Id).Skip(2).ToQueryModel();

        Assert.Throws<NotSupportedException>(() => new TigerGraphQueryCompiler("SocialGraph").Compile(optional));
        Assert.Throws<NotSupportedException>(() => new TigerGraphQueryCompiler("SocialGraph").Compile(offsetOnly));
    }

    [Fact]
    public void CompileUsesSyntaxV2ForVariableDepthTraversal()
    {
        var model = new GraphQueryModel("Person", "node", null, [], 10,
            [new GraphTraversalStep("KNOWS", "Person", "node", "relation1", "node1",
                GraphTraversalDirection.Outgoing, null, null, 1, 3)]);

        var command = new TigerGraphQueryCompiler("SocialGraph").Compile(model);

        Assert.Equal(
            "INTERPRET QUERY () FOR GRAPH SocialGraph SYNTAX V2 { result = SELECT node1 FROM (node:Person)-[relation1:KNOWS*1..3]->(node1:Person) LIMIT 10; PRINT result; }",
            command.Text);
    }

    [Theory]
    [InlineData(GraphTraversalDirection.Incoming, "<-[relation1:KNOWS*1..3]-(node1:Person)")]
    [InlineData(GraphTraversalDirection.Undirected, "-[relation1:KNOWS*1..3]-(node1:Person)")]
    public void CompileUsesSyntaxV2ForEveryVariableDepthDirection(
        GraphTraversalDirection direction,
        string expected)
    {
        var model = new GraphQueryModel(
            "Person", "node", null, [], null,
            [new GraphTraversalStep(
                "KNOWS", "Person", "node", "relation1", "node1",
                direction, null, MinDepth: 1, MaxDepth: 3)]);

        var command = new TigerGraphQueryCompiler("SocialGraph").Compile(model);

        Assert.Contains(expected, command.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileSupportsFixedSimplePathAndSubgraphProjection()
    {
        var model = new GraphQueryModel("Person", "node", null, [], 5,
            [new GraphTraversalStep("KNOWS", "Person", "node", "relation1", "node1",
                GraphTraversalDirection.Outgoing, null)],
            GraphQueryProjection.Subgraph,
            Orderings: [new GraphOrdering("Id", "node1", GraphSortDirection.Ascending)],
            CycleBehavior: GraphCycleBehavior.SimplePath);

        var command = new TigerGraphQueryCompiler("SocialGraph").Compile(model);

        Assert.Contains("nodal_nodes_0 = SELECT node", command.Text, StringComparison.Ordinal);
        Assert.Contains("nodal_nodes_1 = SELECT node1", command.Text, StringComparison.Ordinal);
        Assert.Contains("WHERE node != node1", command.Text, StringComparison.Ordinal);
        Assert.Contains("@@nodal_relations += relation1", command.Text, StringComparison.Ordinal);
        Assert.True(
            command.Text.IndexOf("ACCUM", StringComparison.Ordinal) <
            command.Text.IndexOf("ORDER BY", StringComparison.Ordinal));
    }

    [Fact]
    public void CompileSubgraphWithoutTraversalsAvoidsRelationshipAccumulation()
    {
        var model = new GraphQueryModel(
            "Person", "node", null, [], null, [], GraphQueryProjection.Subgraph);

        var command = new TigerGraphQueryCompiler("SocialGraph").Compile(model);

        Assert.DoesNotContain("ACCUM", command.Text, StringComparison.Ordinal);
        Assert.Contains("PRINT nodal_nodes_0", command.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileProducesServerSideCount()
    {
        var model = new GraphQueryModel("Person", "node", null, [], null, [], GraphQueryProjection.Count);

        var command = new TigerGraphQueryCompiler("SocialGraph").Compile(model);

        Assert.Equal(
            "INTERPRET QUERY () FOR GRAPH SocialGraph { result = SELECT node FROM Person:node; PRINT result.size() AS nodal_count; }",
            command.Text);
    }

    [Fact]
    public void CompileRendersCompletePredicateAndParameterTypeSurface()
    {
        GraphPredicate predicate = new GraphLogicalPredicate(
            new GraphNotPredicate(new GraphComparisonPredicate("Age", GraphComparisonOperator.NotEqual, "p0")),
            GraphLogicalOperator.Or,
            new GraphLogicalPredicate(
                new GraphComparisonPredicate("Score", GraphComparisonOperator.GreaterThan, "p1"),
                GraphLogicalOperator.And,
                new GraphLogicalPredicate(
                    new GraphComparisonPredicate("Rank", GraphComparisonOperator.LessThan, "p2"),
                    GraphLogicalOperator.And,
                    new GraphComparisonPredicate("Level", GraphComparisonOperator.LessThanOrEqual, "p3"))));
        var relationPredicate = new GraphLogicalPredicate(
            new GraphStringPredicate("Comment", GraphStringOperator.StartsWith, "p4"),
            GraphLogicalOperator.And,
            new GraphStringPredicate("Code", GraphStringOperator.Contains, "p5"));
        var model = new GraphQueryModel(
            "Person",
            "node",
            predicate,
            [
                new GraphQueryParameter("p0", 18, typeof(short)),
                new GraphQueryParameter("p1", 50.5m, typeof(decimal)),
                new GraphQueryParameter("p2", true, typeof(bool)),
                new GraphQueryParameter("p3", SampleLevel.Admin, typeof(SampleLevel)),
                new GraphQueryParameter("p4", "trusted", typeof(string)),
                new GraphQueryParameter("p5", Guid.Parse("e8ff13d8-91a8-4e73-a94b-03a38dafd929"), typeof(Guid)),
                new GraphQueryParameter("created", new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero), typeof(DateTimeOffset)),
            ],
            5,
            [new GraphTraversalStep(
                "KNOWS", "Person", "node", "relation1", "node1",
                GraphTraversalDirection.Outgoing, null, relationPredicate)]);

        var command = new TigerGraphQueryCompiler("SocialGraph").Compile(model);

        Assert.Contains("NOT (node.Age != p0)", command.Text, StringComparison.Ordinal);
        Assert.Contains("node.Score > p1", command.Text, StringComparison.Ordinal);
        Assert.Contains("node.Rank < p2", command.Text, StringComparison.Ordinal);
        Assert.Contains("node.Level <= p3", command.Text, StringComparison.Ordinal);
        Assert.Contains("relation1.Comment LIKE p4 + \"%\"", command.Text, StringComparison.Ordinal);
        Assert.Contains("relation1.Code LIKE \"%\" + p5 + \"%\"", command.Text, StringComparison.Ordinal);
        Assert.Equal(1L, command.Parameters["p3"]);
        Assert.IsType<DateTime>(command.Parameters["created"]);
    }

    [Fact]
    public void CompileRejectsUnsupportedPathShapesPredicatesTypesAndEnums()
    {
        var variableSimple = ModelWithTraversal(
            minDepth: 1,
            maxDepth: 3,
            cycleBehavior: GraphCycleBehavior.SimplePath);
        var variablePath = ModelWithTraversal(minDepth: 1, maxDepth: 3, projection: GraphQueryProjection.Path);
        var invalidDepth = ModelWithTraversal(minDepth: -1, maxDepth: 1);
        var invalidDirection = new GraphQueryModel(
            "Person", "node", null, [], null,
            [new GraphTraversalStep(
                "KNOWS", "Person", "node", "relation1", "node1", (GraphTraversalDirection)999, null)]);

        Assert.Throws<NotSupportedException>(() => new TigerGraphQueryCompiler("SocialGraph").Compile(variableSimple));
        Assert.Throws<NotSupportedException>(() => new TigerGraphQueryCompiler("SocialGraph").Compile(variablePath));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TigerGraphQueryCompiler("SocialGraph").Compile(invalidDepth));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TigerGraphQueryCompiler("SocialGraph").Compile(invalidDirection));
        Assert.Throws<NotSupportedException>(() => CompilePredicate(new UnsupportedPredicate()));
        Assert.Throws<ArgumentOutOfRangeException>(() => CompilePredicate(
            new GraphComparisonPredicate("Age", (GraphComparisonOperator)999, "p0")));
        Assert.Throws<ArgumentOutOfRangeException>(() => CompilePredicate(
            new GraphLogicalPredicate(
                new GraphNullPredicate("Name", true),
                (GraphLogicalOperator)999,
                new GraphNullPredicate("Name", false))));
        Assert.Throws<ArgumentOutOfRangeException>(() => CompilePredicate(
            new GraphStringPredicate("Name", (GraphStringOperator)999, "p0")));
        Assert.Throws<NotSupportedException>(() => new TigerGraphQueryCompiler("SocialGraph").Compile(
            new GraphQueryModel(
                "Person", "node", null,
                [new GraphQueryParameter("p0", new Version(1, 0), typeof(Version))],
                null, [])));

        static GraphQueryModel ModelWithTraversal(
            int minDepth,
            int maxDepth,
            GraphCycleBehavior cycleBehavior = GraphCycleBehavior.ProviderDefault,
            GraphQueryProjection projection = GraphQueryProjection.Node) => new(
                "Person", "node", null, [], null,
                [new GraphTraversalStep(
                    "KNOWS", "Person", "node", "relation1", "node1",
                    GraphTraversalDirection.Outgoing, null, MinDepth: minDepth, MaxDepth: maxDepth)],
                projection,
                CycleBehavior: cycleBehavior);

        static GraphCommand CompilePredicate(GraphPredicate predicate) => new TigerGraphQueryCompiler("SocialGraph").Compile(
            new GraphQueryModel(
                "Person", "node", predicate,
                [new GraphQueryParameter("p0", "value", typeof(string))],
                null, []));
    }

    private sealed record Person(string Id, int Age);

    private sealed record RichPerson(string Id, string Name);

    private enum SampleLevel
    {
        User,
        Admin,
    }

    private sealed record UnsupportedPredicate : GraphPredicate;
}
