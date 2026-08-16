using Nodal.Core.Execution;
using Nodal.Core.Migrations;
using Nodal.Core.Model;

namespace Nodal.Core.Tests;

public sealed class CoreContractsTests
{
    [Fact]
    public void JsonMaterializerCreatesPocosAndPreservesExplicitIdProperty()
    {
        var result = new GraphQueryResult(
        [
            new GraphNodeRecord(
                "Person",
                "provider-id",
                new Dictionary<string, object?>
                {
                    ["id"] = "domain-id",
                    ["name"] = "Ada",
                }),
        ]);

        var people = new JsonGraphResultMaterializer().Materialize<Person>(result);

        var person = Assert.Single(people);
        Assert.Equal("domain-id", person.Id);
        Assert.Equal("Ada", person.Name);
    }

    [Fact]
    public void JsonMaterializerAddsProviderIdWhenPropertiesDoNotContainOne()
    {
        var result = new GraphQueryResult(
        [
            new GraphNodeRecord(
                "Person",
                "person-42",
                new Dictionary<string, object?> { ["Name"] = "Ada" }),
        ]);

        var person = Assert.Single(new JsonGraphResultMaterializer().Materialize<Person>(result));

        Assert.Equal("person-42", person.Id);
    }

    [Fact]
    public void JsonMaterializerRejectsNullResult()
    {
        Assert.Throws<ArgumentNullException>(
            () => new JsonGraphResultMaterializer().Materialize<Person>(null!));
        Assert.Throws<ArgumentNullException>(() =>
            new JsonGraphResultMaterializer().MaterializePaths<Person, Friendship, Person>(null!));
    }

    [Fact]
    public void JsonMaterializerCreatesStronglyTypedPaths()
    {
        var source = new GraphNodeRecord("Person", "person-1", new Dictionary<string, object?> { ["Name"] = "Ada" });
        var target = new GraphNodeRecord("Person", "person-2", new Dictionary<string, object?> { ["Name"] = "Alan" });
        var relation = new GraphRelationRecord(
            "KNOWS",
            "relation-1",
            "person-1",
            "person-2",
            new Dictionary<string, object?> { ["SinceYear"] = 2020 });
        var result = new GraphQueryResult([source, target], [relation], [new GraphPathRecord(source, relation, target)]);

        var path = Assert.Single(
            new JsonGraphResultMaterializer().MaterializePaths<Person, Friendship, Person>(result));

        Assert.Equal("Ada", path.Source.Name);
        Assert.Equal(2020, path.Relation.SinceYear);
        Assert.Equal("Alan", path.Target.Name);
    }

    [Fact]
    public void GraphReferencesAndRelationsRetainDomainIdentity()
    {
        var source = new GraphRef<Person>("person-1");
        var target = new GraphRef<Person>(42);
        var relation = new GraphRelation<Person, Friendship, Person>(
            source,
            new Friendship(2020),
            target);

        Assert.Equal("person-1", source.ToString());
        Assert.Equal("42", target.ToString());
        Assert.Equal(source, relation.Source);
        Assert.Equal(2020, relation.Properties.SinceYear);
        Assert.Equal(target, relation.Target);
    }

    [Fact]
    public void GraphReferenceRejectsNullIdentity()
    {
        Assert.Throws<ArgumentNullException>(() => new GraphRef<Person>(null!));
    }

    [Fact]
    public void MigrationContractsRetainProviderNeutralSchemaIntent()
    {
        MigrationOperation node = new CreateNodeTypeOperation("Person");
        MigrationOperation relation = new CreateRelationTypeOperation("KNOWS", "Person", "Person", true);
        MigrationOperation constraint = new CreateUniqueConstraintOperation("Person", "person_id");
        var command = new MigrationCommand("CREATE CONSTRAINT", true);

        Assert.Equal("Person", Assert.IsType<CreateNodeTypeOperation>(node).NodeType);
        var relationOperation = Assert.IsType<CreateRelationTypeOperation>(relation);
        Assert.Equal("KNOWS", relationOperation.RelationType);
        Assert.Equal("Person", relationOperation.SourceType);
        Assert.Equal("Person", relationOperation.TargetType);
        Assert.True(relationOperation.Directed);
        Assert.Equal("person_id", Assert.IsType<CreateUniqueConstraintOperation>(constraint).PropertyName);
        Assert.Equal("CREATE CONSTRAINT", command.Text);
        Assert.True(command.IsTransactional);
    }

    [Fact]
    public void ExtendedMigrationContractsRetainDryRunAndSchemaMetadata()
    {
        var properties = new[] { new GraphSchemaProperty("email", typeof(string)) };
        var node = new CreateNodeTypeOperation("Person", "person_id", typeof(Guid), properties);
        var index = new CreateIndexOperation("Person", "email");
        var dropNode = new DropNodeTypeOperation("Person");
        var dropRelation = new DropRelationTypeOperation("KNOWS");
        var dropObject = new DropSchemaObjectOperation("person_email", MigrationSchemaObjectKind.Index);
        var command = new MigrationCommand("CREATE INDEX", false, MigrationCommandKind.QueryDefinition);
        var execution = new MigrationExecution("001_initial", "checksum", [command]);

        Assert.Equal("person_id", node.KeyProperty);
        Assert.Equal(typeof(Guid), node.KeyClrType);
        Assert.Same(properties, node.Properties);
        Assert.Equal("email", index.PropertyName);
        Assert.Equal("Person", dropNode.NodeType);
        Assert.Equal("KNOWS", dropRelation.RelationType);
        Assert.Equal(MigrationSchemaObjectKind.Index, dropObject.Kind);
        Assert.Equal(MigrationCommandKind.QueryDefinition, command.Kind);
        Assert.Equal("001_initial", execution.Id);
        Assert.Equal("checksum", execution.Checksum);
        Assert.Same(command, Assert.Single(execution.Commands));
    }

    private sealed record Person(string Id, string Name = "");

    private sealed record Friendship(int SinceYear);
}
