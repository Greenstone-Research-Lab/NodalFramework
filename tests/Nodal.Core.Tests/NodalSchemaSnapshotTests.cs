using Nodal.Core.Execution;
using Nodal.Core.Metadata;
using Nodal.Core.Migrations;
using Nodal.Core.Providers;
using Nodal.Core.Query;

namespace Nodal.Core.Tests;

public sealed class NodalSchemaSnapshotTests
{
    [Fact]
    public void SnapshotFactoryCapturesModelAndProducesStableHash()
    {
        var model = new SnapshotContext(new EmptyProvider()).Model;

        var snapshot = NodalSchemaSnapshotFactory.FromModel(
            model,
            "Neo4j",
            "5.26");
        var repeat = NodalSchemaSnapshotFactory.FromModel(
            model,
            "Neo4j",
            "5.26");

        Assert.Equal(NodalSchemaSnapshot.CurrentFormatVersion, snapshot.FormatVersion);
        Assert.Equal("Neo4j", snapshot.ProviderName);
        Assert.Equal("5.26", snapshot.ProviderVersion);
        Assert.Single(snapshot.Nodes);
        Assert.Single(snapshot.Relations);
        Assert.Equal("people", snapshot.Nodes[0].Name);
        Assert.Equal("KNOWS", snapshot.Relations[0].Name);
        Assert.Equal("display_name", snapshot.Nodes[0].Properties[1].Name);
        Assert.True(snapshot.Nodes[0].Properties[1].IsNullable);
        Assert.Equal(
            NodalSchemaSnapshotSerializer.Serialize(snapshot),
            NodalSchemaSnapshotSerializer.Serialize(repeat));
        Assert.Equal(
            NodalSchemaSnapshotSerializer.ComputeHash(snapshot),
            NodalSchemaSnapshotSerializer.ComputeHash(repeat));
    }

    [Fact]
    public void SnapshotNormalizationOrdersPropertiesAndValidatesInputs()
    {
        var snapshot = new NodalSchemaSnapshot(
            1,
            [new NodalNodeSnapshot(
                "people", "Person", "id",
                [
                    new NodalPropertySnapshot("z", "Z", "System.String", true, false, []),
                    new NodalPropertySnapshot("a", "A", "System.Int32", false, false, []),
                ])],
            [],
            Indexes:
            [
                new NodalSchemaObjectSnapshot("z-index", "Index", "people", ["name"]),
                new NodalSchemaObjectSnapshot("a-index", "Index", "people", ["id"]),
            ],
            Constraints:
            [
                new NodalSchemaObjectSnapshot("uq-people-id", "Constraint", "people", ["id"], true),
            ]);

        var normalized = snapshot.Normalize();

        Assert.Equal("a", normalized.Nodes[0].Properties[0].Name);
        Assert.Equal("z", normalized.Nodes[0].Properties[1].Name);
        Assert.Equal("a-index", normalized.Indexes![0].Name);
        Assert.True(normalized.Constraints![0].IsUnique);
        Assert.Throws<ArgumentNullException>(
            () => NodalSchemaSnapshotSerializer.Serialize(null!));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new NodalSchemaSnapshot(0, [], []).Normalize());
    }

    [Fact]
    public void DifferReportsSchemaChangesWithoutGuessingRenames()
    {
        var before = new NodalSchemaSnapshot(
            1,
            [new NodalNodeSnapshot(
                "people", "Person", "id",
                [
                    new NodalPropertySnapshot("name", "Name", "System.String", false, false, []),
                    new NodalPropertySnapshot("age", "Age", "System.Int32", false, false, []),
                ])],
            [new NodalRelationSnapshot(
                "KNOWS", "Knows", "people", "people", true,
                [new NodalPropertySnapshot("since", "Since", "System.Int32", false, false, [])])]);
        var after = new NodalSchemaSnapshot(
            1,
            [new NodalNodeSnapshot(
                "people", "Person", "id",
                [
                    new NodalPropertySnapshot("display_name", "Name", "System.String", false, false, []),
                    new NodalPropertySnapshot("age", "Age", "System.Int64", false, false, []),
                ])],
            [new NodalRelationSnapshot(
                "KNOWS", "Knows", "people", "people", false,
                [new NodalPropertySnapshot("since", "Since", "System.Int64", false, false, [])])]);

        var raw = NodalSchemaDiffer.Compare(before, after);
        Assert.Contains(raw.Changes, change =>
            change.Kind is NodalSchemaChangeKind.NodePropertyRemoved &&
            change.PropertyName == "name");
        Assert.Contains(raw.Changes, change =>
            change.Kind is NodalSchemaChangeKind.NodePropertyAdded &&
            change.PropertyName == "display_name");
        Assert.Contains(raw.Changes, change =>
            change.Kind is NodalSchemaChangeKind.NodePropertyTypeChanged &&
            change.PropertyName == "age");
        Assert.Contains(raw.Changes, change =>
            change.Kind is NodalSchemaChangeKind.RelationShapeChanged);
        Assert.Contains(raw.Changes, change =>
            change.Kind is NodalSchemaChangeKind.RelationPropertyTypeChanged);

        var renamed = NodalSchemaDiffer.Compare(
            before,
            after,
            new NodalSchemaDiffOptions(
                new Dictionary<string, string>
                {
                    ["node:people:name"] = "display_name",
                }));
        Assert.Contains(renamed.Changes, change =>
            change.Kind is NodalSchemaChangeKind.NodePropertyRenamed &&
            change.NewPropertyName == "display_name");
        Assert.DoesNotContain(renamed.Changes, change =>
            change.Kind is NodalSchemaChangeKind.NodePropertyAdded &&
            change.PropertyName == "display_name");

        var plan = NodalSchemaMigrationMapper.Map(
            before,
            after,
            new NodalSchemaDiffOptions(
                new Dictionary<string, string>
                {
                    ["node:people:name"] = "display_name",
                }),
            name => name switch
            {
                "System.String" => typeof(string),
                "System.Int32" => typeof(int),
                "System.Int64" => typeof(long),
                _ => null,
            });

        Assert.Contains(plan.Operations, operation =>
            operation is RenameNodePropertyOperation);
        Assert.Contains(plan.Operations, operation =>
            operation is AlterNodePropertyTypeOperation);
        Assert.Contains(plan.Operations, operation =>
            operation is AlterRelationPropertyTypeOperation);
        Assert.True(plan.RequiresManualReview);
    }

    private sealed class SnapshotContext(IGraphProvider provider) : NodalContext(provider)
    {
        public GraphSet<Person> People => Set<Person>();

        public RelationSet<Person, Knows, Person> Knows =>
            Relations<Person, Knows, Person>();
    }

    [GraphNode("people")]
    private sealed record Person(
        [property: GraphKey] string Id,
        [property: GraphProperty("display_name")] string? Name);

    [GraphRelation("KNOWS", Directed = true)]
    private sealed record Knows(
        [property: GraphProperty("since")] DateTime Since);

    private sealed class EmptyProvider : IGraphProvider
    {
        public IGraphQueryCompiler QueryCompiler => throw new NotSupportedException();
        public IGraphCommandExecutor CommandExecutor => throw new NotSupportedException();
        public IGraphResultMaterializer ResultMaterializer => throw new NotSupportedException();
    }
}
