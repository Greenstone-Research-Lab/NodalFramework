using Nodal.Core.ChangeTracking;
using Nodal.Core.Mutations;

namespace Nodal.Neo4j.Tests;

public sealed class Neo4jMutationCompilerTests
{
    [Fact]
    public void CompileProducesOrderedParameterizedCypherForEveryMutationKind()
    {
        var source = Identity("Person", "person_id", "person-1");
        var target = Identity("Person", "person_id", "person-2");
        var plan = new GraphMutationPlan(
        [
            new CreateNodeOperation(source, Properties(("person_id", "person-1"), ("name", "Ada"))),
            new CreateRelationOperation(source, "KNOWS", target, true, Properties(("since", 2020))),
            new UpdateRelationOperation(source, "KNOWS", target, true, Properties(("since", 2025))),
            new UpdateNodeOperation(target, Properties(("person_id", "person-2"), ("name", "Alan"))),
            new DeleteRelationOperation(source, "KNOWS", target, true),
            new DeleteNodeOperation(target),
        ]);

        var commands = Neo4jMutationCompiler.Compile(plan);

        Assert.Collection(
            commands,
            command =>
            {
                Assert.Equal(
                    "MERGE (`node`:`Person` {`person_id`: $key}) SET `node` += $properties",
                    command.Text);
                Assert.Equal("person-1", command.Parameters["key"]);
                var properties = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(command.Parameters["properties"]);
                Assert.Equal("Ada", properties["name"]);
                Assert.DoesNotContain("person_id", properties.Keys);
            },
            command =>
            {
                Assert.Contains("MERGE (`source`)-[`relation`:`KNOWS`]->(`target`)", command.Text, StringComparison.Ordinal);
                Assert.Equal("person-1", command.Parameters["sourceKey"]);
                Assert.Equal("person-2", command.Parameters["targetKey"]);
            },
            command =>
            {
                Assert.Contains("[`relation`:`KNOWS`]->", command.Text, StringComparison.Ordinal);
                Assert.Contains("SET `relation` += $properties", command.Text, StringComparison.Ordinal);
            },
            command => Assert.StartsWith("MATCH (`node`:`Person`", command.Text, StringComparison.Ordinal),
            command => Assert.Contains("DELETE `relation`", command.Text, StringComparison.Ordinal),
            command => Assert.EndsWith("DETACH DELETE `node`", command.Text, StringComparison.Ordinal));
    }

    [Fact]
    public void UndirectedRelationshipDeletionUsesDirectionAgnosticPattern()
    {
        var source = Identity("Person", "Id", 1);
        var target = Identity("Person", "Id", 2);
        var plan = new GraphMutationPlan(
            [new DeleteRelationOperation(source, "KNOWS", target, false)]);

        var command = Assert.Single(Neo4jMutationCompiler.Compile(plan));

        Assert.Contains("-[`relation`:`KNOWS`]-", command.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void QueriedRelationshipUpdateUsesExactProviderIdentity()
    {
        var source = Identity("Person", "Id", 1);
        var target = Identity("Person", "Id", 2);
        var operation = new UpdateRelationOperation(
            source,
            "KNOWS",
            target,
            false,
            Properties(("since", 2025)),
            "edge-42");

        var command = Assert.Single(Neo4jMutationCompiler.Compile(new GraphMutationPlan([operation])));

        Assert.Contains("-[`relation`:`KNOWS`]-", command.Text, StringComparison.Ordinal);
        Assert.Contains("WHERE elementId(`relation`) = $relationId", command.Text, StringComparison.Ordinal);
        Assert.Equal("edge-42", command.Parameters["relationId"]);
    }

    [Fact]
    public void CompilerEscapesSchemaIdentifiers()
    {
        var identity = Identity("Odd`Label", "domain`key", "one");

        var command = Assert.Single(Neo4jMutationCompiler.Compile(
            new GraphMutationPlan([new DeleteNodeOperation(identity)])));

        Assert.Contains("`Odd``Label`", command.Text, StringComparison.Ordinal);
        Assert.Contains("`domain``key`", command.Text, StringComparison.Ordinal);
    }

    private static GraphIdentity Identity(string nodeType, string key, object value) =>
        new(typeof(object), nodeType, key, value);

    private static Dictionary<string, object?> Properties(
        params (string Name, object? Value)[] properties) =>
        properties.ToDictionary(property => property.Name, property => property.Value);
}
