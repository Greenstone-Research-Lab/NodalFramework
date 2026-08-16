using Nodal.Core.Query;

namespace Nodal.Neo4j.Tests;

public sealed class Neo4jQueryCompilerTests
{
    [Fact]
    public void CompileProducesParameterizedCypher()
    {
        const string personId = "person-42";
        var model = new GraphSet<Person>()
            .Match(person => person.Id == personId && person.Age >= 18)
            .Take(5)
            .ToQueryModel();

        var command = new Neo4jQueryCompiler().Compile(model);

        Assert.Equal(
            "MATCH (`node`:`Person`) WHERE (`node`.`Id` = $p0 AND `node`.`Age` >= $p1) RETURN `node` LIMIT 5",
            command.Text);
        Assert.Equal(personId, command.Parameters["p0"]);
        Assert.Equal(18, command.Parameters["p1"]);
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

        var command = new Neo4jQueryCompiler().Compile(model);

        Assert.Equal(
            "MATCH (`node`:`Person`)-[`relation1`:`WORKS_AT`]->(`node1`:`Company`) WHERE `node`.`Id` = $p0 AND `node1`.`Country` = $p1 RETURN `node1` LIMIT 5",
            command.Text);
    }

    [Theory]
    [InlineData(GraphTraversalDirection.Incoming, "<-[`relation1`:`WORKS_AT`]-(`node1`:`Company`)")]
    [InlineData(GraphTraversalDirection.Undirected, "-[`relation1`:`WORKS_AT`]-(`node1`:`Company`)")]
    public void CompileHonorsTraversalDirection(GraphTraversalDirection direction, string expectedPattern)
    {
        var model = new GraphQueryModel(
            "Person",
            "node",
            null,
            [],
            null,
            [new GraphTraversalStep("WORKS_AT", "Company", "node", "relation1", "node1", direction, null)]);

        var command = new Neo4jQueryCompiler().Compile(model);

        Assert.Contains(expectedPattern, command.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void CompilePathProjectionReturnsEndpointsAndFiltersRelationship()
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

        var command = new Neo4jQueryCompiler().Compile(model);

        Assert.Equal(
            "MATCH (`node`:`Person`)-[`relation1`:`KNOWS`]->(`node1`:`Person`) WHERE `relation1`.`since_year` >= $p0 RETURN `node`, `relation1`, `node1` LIMIT 2",
            command.Text);
    }

    private sealed record Person(string Id, int Age);
}
