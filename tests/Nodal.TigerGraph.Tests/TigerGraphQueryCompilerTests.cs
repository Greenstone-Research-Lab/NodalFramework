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
    public void CompileProducesServerSideCount()
    {
        var model = new GraphQueryModel("Person", "node", null, [], null, [], GraphQueryProjection.Count);

        var command = new TigerGraphQueryCompiler("SocialGraph").Compile(model);

        Assert.Equal(
            "INTERPRET QUERY () FOR GRAPH SocialGraph { result = SELECT node FROM Person:node; PRINT result.size() AS nodal_count; }",
            command.Text);
    }

    private sealed record Person(string Id, int Age);

    private sealed record RichPerson(string Id, string Name);
}
