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
            "INTERPRET QUERY (INT p0) FOR GRAPH SocialGraph { SetAccum<VERTEX> @@nodal_sources; ListAccum<EDGE> @@nodal_relations; result = SELECT node1 FROM Person:node -(KNOWS:relation1)-> Person:node1 WHERE relation1.since_year >= p0 ACCUM @@nodal_sources += node, @@nodal_relations += relation1 LIMIT 2; PRINT @@nodal_sources AS nodal_sources, @@nodal_relations AS nodal_relations, result AS nodal_targets; }",
            command.Text);
    }

    private sealed record Person(string Id, int Age);
}
