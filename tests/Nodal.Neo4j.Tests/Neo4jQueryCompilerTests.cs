using Nodal.Core.Providers;
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

    [Fact]
    public void CompileSupportsRichPredicatesOrderingPagingAndDistinct()
    {
        string[] ids = ["a", "b"];
        var model = new GraphSet<RichPerson>().Query()
            .Where(person => ids.Contains(person.Id) && person.Name.StartsWith("Ad") && person.DeletedAt == null)
            .OrderBy(person => person.Name)
            .ThenByDescending(person => person.Id)
            .Skip(2)
            .Take(3)
            .Distinct()
            .ToQueryModel();

        var command = new Neo4jQueryCompiler().Compile(model);

        Assert.Equal(
            "MATCH (`node`:`RichPerson`) WHERE ((`node`.`Id` IN $p0 AND `node`.`Name` STARTS WITH $p1) AND `node`.`DeletedAt` IS NULL) RETURN DISTINCT `node` ORDER BY `node`.`Name` ASC, `node`.`Id` DESC SKIP 2 LIMIT 3",
            command.Text);
    }

    [Fact]
    public void CompileSupportsVariableDepthAndOptionalTraversal()
    {
        var model = new GraphQueryModel("Person", "node", null, [], null,
            [new GraphTraversalStep("KNOWS", "Person", "node", "relation1", "node1",
                GraphTraversalDirection.Outgoing, null, null, 1, 3, true)]);

        var command = new Neo4jQueryCompiler().Compile(model);

        Assert.Equal(
            "MATCH (`node`:`Person`) OPTIONAL MATCH (`node`)-[`relation1`:`KNOWS`*1..3]->(`node1`:`Person`) RETURN `node1`",
            command.Text);
    }

    [Fact]
    public void OptionalTraversalKeepsRootAndOptionalFiltersInTheirOwnMatchScopes()
    {
        var model = new GraphQueryModel(
            "Person", "node",
            new GraphComparisonPredicate("Id", GraphComparisonOperator.Equal, "p0"),
            [new GraphQueryParameter("p0", "person-1", typeof(string)),
             new GraphQueryParameter("p1", "TR", typeof(string))],
            null,
            [new GraphTraversalStep("WORKS_AT", "Company", "node", "relation1", "node1",
                GraphTraversalDirection.Outgoing,
                new GraphComparisonPredicate("Country", GraphComparisonOperator.Equal, "p1"),
                Optional: true)]);

        var command = new Neo4jQueryCompiler().Compile(model);

        Assert.Equal(
            "MATCH (`node`:`Person`) WHERE `node`.`Id` = $p0 OPTIONAL MATCH (`node`)-[`relation1`:`WORKS_AT`]->(`node1`:`Company`) WHERE `node1`.`Country` = $p1 RETURN `node1`",
            command.Text);
    }

    [Fact]
    public void CompileSupportsSimplePathAndSubgraphProjection()
    {
        var model = new GraphQueryModel("Person", "node", null, [], null,
            [new GraphTraversalStep("KNOWS", "Person", "node", "relation1", "node1",
                GraphTraversalDirection.Outgoing, null)],
            GraphQueryProjection.Subgraph,
            CycleBehavior: GraphCycleBehavior.SimplePath);

        var command = new Neo4jQueryCompiler().Compile(model);

        Assert.Contains("MATCH `nodalPath` = (`node`:`Person`)", command.Text, StringComparison.Ordinal);
        Assert.Contains("all(`nodalVertex` IN nodes(`nodalPath`)", command.Text, StringComparison.Ordinal);
        Assert.EndsWith("RETURN `node`, `relation1`, `node1`", command.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileProducesServerSideDistinctCount()
    {
        var model = new GraphQueryModel("Person", "node", null, [], null, [],
            GraphQueryProjection.Count, Distinct: true);

        var command = new Neo4jQueryCompiler().Compile(model);

        Assert.Equal("MATCH (`node`:`Person`) RETURN count(DISTINCT `node`) AS `nodal_count`", command.Text);
    }

    [Fact]
    public void CompileRendersEverySupportedPredicateVariantAcrossNodeAndRelationScopes()
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
            new GraphStringPredicate("Comment", GraphStringOperator.Contains, "p4"),
            GraphLogicalOperator.And,
            new GraphStringPredicate("Code", GraphStringOperator.EndsWith, "p5"));
        var model = new GraphQueryModel(
            "Person",
            "node",
            predicate,
            [
                new GraphQueryParameter("p0", 18, typeof(int)),
                new GraphQueryParameter("p1", 50, typeof(int)),
                new GraphQueryParameter("p2", 10, typeof(int)),
                new GraphQueryParameter("p3", 5, typeof(int)),
                new GraphQueryParameter("p4", "friend", typeof(string)),
                new GraphQueryParameter("p5", "-trusted", typeof(string)),
            ],
            null,
            [new GraphTraversalStep(
                "KNOWS",
                "Person",
                "node",
                "relation1",
                "node1",
                GraphTraversalDirection.Outgoing,
                null,
                relationPredicate)]);

        var command = new Neo4jQueryCompiler().Compile(model);

        Assert.Contains("NOT (`node`.`Age` <> $p0)", command.Text, StringComparison.Ordinal);
        Assert.Contains("`node`.`Score` > $p1", command.Text, StringComparison.Ordinal);
        Assert.Contains("`node`.`Rank` < $p2", command.Text, StringComparison.Ordinal);
        Assert.Contains("`node`.`Level` <= $p3", command.Text, StringComparison.Ordinal);
        Assert.Contains("`relation1`.`Comment` CONTAINS $p4", command.Text, StringComparison.Ordinal);
        Assert.Contains("`relation1`.`Code` ENDS WITH $p5", command.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileRejectsInvalidSimpleOptionalAndDepthCombinations()
    {
        var optionalSimple = new GraphQueryModel(
            "Person", "node", null, [], null,
            [new GraphTraversalStep(
                "KNOWS", "Person", "node", "relation1", "node1",
                GraphTraversalDirection.Outgoing, null, Optional: true)],
            CycleBehavior: GraphCycleBehavior.SimplePath);
        var invalidDepth = new GraphQueryModel(
            "Person", "node", null, [], null,
            [new GraphTraversalStep(
                "KNOWS", "Person", "node", "relation1", "node1",
                GraphTraversalDirection.Outgoing, null, MinDepth: -1, MaxDepth: 1)]);

        Assert.Throws<NotSupportedException>(() => new Neo4jQueryCompiler().Compile(optionalSimple));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Neo4jQueryCompiler().Compile(invalidDepth));
    }

    [Fact]
    public void CompileRejectsUnknownPredicateAndEnumValues()
    {
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

        var invalidDirection = new GraphQueryModel(
            "Person", "node", null, [], null,
            [new GraphTraversalStep(
                "KNOWS", "Person", "node", "relation1", "node1",
                (GraphTraversalDirection)999, null)]);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Neo4jQueryCompiler().Compile(invalidDirection));

        static GraphCommand CompilePredicate(GraphPredicate predicate) => new Neo4jQueryCompiler().Compile(
            new GraphQueryModel(
                "Person",
                "node",
                predicate,
                [new GraphQueryParameter("p0", "value", typeof(string))],
                null,
                []));
    }

    private sealed record Person(string Id, int Age);

    private sealed record RichPerson(string Id, string Name, DateTime? DeletedAt);

    private sealed record UnsupportedPredicate : GraphPredicate;
}
