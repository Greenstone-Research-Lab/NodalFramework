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
    public void SnapshotSerializationRoundTripsAndRejectsUnknownVersions()
    {
        var snapshot = NodalSchemaSnapshotFactory.FromModel(
            new SnapshotContext(new EmptyProvider()).Model,
            "TigerGraph",
            "4.2");
        var json = NodalSchemaSnapshotSerializer.Serialize(snapshot);

        var loaded = NodalSchemaSnapshotSerializer.Deserialize(json);

        Assert.Equal(json, NodalSchemaSnapshotSerializer.Serialize(loaded));
        var exception = Assert.Throws<NodalSchemaSnapshotVersionException>(() =>
            NodalSchemaSnapshotSerializer.Deserialize(
                "{\"formatVersion\":2,\"nodes\":[],\"relations\":[]}"));
        Assert.Equal(2, exception.ActualVersion);
        Assert.Equal(NodalSchemaSnapshot.CurrentFormatVersion, exception.SupportedVersion);
        Assert.Throws<ArgumentException>(() => NodalSchemaSnapshotSerializer.Deserialize(" "));
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

    [Fact]
    public void MapperCreatesAndRemovesNodeAndRelationOperations()
    {
        var before = new NodalSchemaSnapshot(
            1,
            [new NodalNodeSnapshot("obsolete", "System.Object", "id", [])],
            [new NodalRelationSnapshot("OLD", "System.Object", "obsolete", "obsolete", true, [])]);
        var after = new NodalSchemaSnapshot(
            1,
            [new NodalNodeSnapshot("current", "System.Object", "id", [
                new NodalPropertySnapshot("name", "Name", "System.String", true, false, [])])],
            [new NodalRelationSnapshot("NEW", "System.Object", "current", "current", true, [])]);

        var plan = NodalSchemaMigrationMapper.Map(
            before,
            after,
            typeResolver: name => name switch
            {
                "System.Object" => typeof(object),
                "System.String" => typeof(string),
                _ => null,
            });

        Assert.Contains(plan.Operations, operation => operation is CreateNodeTypeOperation);
        Assert.Contains(plan.Operations, operation => operation is DropNodeTypeOperation);
        Assert.Contains(plan.Operations, operation => operation is CreateRelationTypeOperation);
        Assert.Contains(plan.Operations, operation => operation is DropRelationTypeOperation);
        Assert.Throws<InvalidOperationException>(() => NodalSchemaMigrationMapper.Map(
            new NodalSchemaSnapshot(1, [], []),
            after,
            typeResolver: _ => null));
    }

    [Fact]
    public void DifferMapsIndexesAndConstraintsAndRendersReviewablePlans()
    {
        var before = new NodalSchemaSnapshot(
            1,
            [],
            [],
            Indexes:
            [
                new NodalSchemaObjectSnapshot("ix-old", "Index", "people", ["old"]),
                new NodalSchemaObjectSnapshot("ix-changed", "Index", "people", ["name"]),
            ],
            Constraints:
            [
                new NodalSchemaObjectSnapshot("uq-old", "Constraint", "people", ["id"], true),
            ]);
        var after = new NodalSchemaSnapshot(
            1,
            [],
            [],
            Indexes:
            [
                new NodalSchemaObjectSnapshot("ix-new", "Index", "people", ["email"]),
                new NodalSchemaObjectSnapshot("ix-composite", "Index", "people", ["tenant", "email"]),
                new NodalSchemaObjectSnapshot("ix-changed", "Index", "people", ["display_name"]),
            ],
            Constraints:
            [
                new NodalSchemaObjectSnapshot("uq-new", "Constraint", "people", ["external_id"], true),
            ]);

        var diff = NodalSchemaDiffer.Compare(before, after);
        var plan = NodalSchemaMigrationMapper.Map(before, after);

        Assert.Contains(diff.Changes, change => change.Kind is NodalSchemaChangeKind.IndexAdded);
        Assert.Contains(diff.Changes, change => change.Kind is NodalSchemaChangeKind.IndexRemoved);
        Assert.Contains(diff.Changes, change => change.Kind is NodalSchemaChangeKind.IndexChanged);
        Assert.Contains(diff.Changes, change => change.Kind is NodalSchemaChangeKind.ConstraintAdded);
        Assert.Contains(diff.Changes, change => change.Kind is NodalSchemaChangeKind.ConstraintRemoved);
        Assert.Contains(plan.Operations, operation => operation is CreateIndexOperation);
        Assert.Contains(plan.Operations, operation => operation is CreateUniqueConstraintOperation);
        Assert.Equal(2, plan.Operations.Count(operation => operation is DropSchemaObjectOperation));
        Assert.True(plan.RequiresManualReview);

        var machine = NodalSchemaMigrationPlanSerializer.Serialize(plan);
        var markdown = NodalSchemaMigrationPlanSerializer.ToMarkdown(plan);
        Assert.Contains("Create index people.email", machine, StringComparison.Ordinal);
        Assert.Contains("# Nodal schema migration plan", markdown, StringComparison.Ordinal);
        Assert.Contains("IndexChanged: ix-changed", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyPlanRendersExplicitEmptySections()
    {
        var plan = new NodalSchemaMigrationPlan([], []);

        var markdown = NodalSchemaMigrationPlanSerializer.ToMarkdown(plan);

        Assert.Equal(2, markdown.Split("- None", StringSplitOptions.None).Length - 1);
        Assert.Throws<ArgumentNullException>(() => NodalSchemaMigrationPlanSerializer.Serialize(null!));
        Assert.Throws<ArgumentNullException>(() => NodalSchemaMigrationPlanSerializer.ToMarkdown(null!));
    }

    [Fact]
    public void PlanRendererDescribesEveryProviderNeutralOperation()
    {
        MigrationOperation[] operations =
        [
            new CreateNodeTypeOperation("people"),
            new CreateRelationTypeOperation("KNOWS", "people", "people", true),
            new CreateUniqueConstraintOperation("people", "id"),
            new CreateIndexOperation("people", "email"),
            new DropIndexOperation("people", "old_email"),
            new DropUniqueConstraintOperation("people", "old_id"),
            new DropNodeTypeOperation("obsolete"),
            new DropRelationTypeOperation("OLD_RELATION"),
            new DropSchemaObjectOperation("ix_named", MigrationSchemaObjectKind.Index),
            new AddNodePropertyOperation("people", new GraphSchemaProperty("age", typeof(int))),
            new AddRelationPropertyOperation("KNOWS", new GraphSchemaProperty("since", typeof(int))),
            new DropNodePropertyOperation("people", "legacy"),
            new DropRelationPropertyOperation("KNOWS", "legacy"),
            new RenameNodePropertyOperation("people", "name", "display_name"),
            new RenameRelationPropertyOperation("KNOWS", "date", "since"),
            new AlterNodePropertyTypeOperation(
                "people", "age", typeof(int), typeof(long), MigrationPropertyTypeCompatibility.RequiresRewrite),
            new AlterRelationPropertyTypeOperation(
                "KNOWS", "since", typeof(int), typeof(long), MigrationPropertyTypeCompatibility.RequiresRewrite),
        ];

        var markdown = NodalSchemaMigrationPlanSerializer.ToMarkdown(
            new NodalSchemaMigrationPlan(operations, []));

        Assert.Contains("Create node people", markdown, StringComparison.Ordinal);
        Assert.Contains("Alter relation property KNOWS.since", markdown, StringComparison.Ordinal);
        Assert.Contains("Rename node property people.name to display_name", markdown, StringComparison.Ordinal);
        Assert.Contains("Drop Index ix_named", markdown, StringComparison.Ordinal);
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
