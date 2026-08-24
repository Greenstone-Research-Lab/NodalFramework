using Nodal.Core.Metadata;
using Nodal.Core.Migrations;
using Nodal.Core.Providers;
using Nodal.Core.Execution;
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
            []);

        var normalized = snapshot.Normalize();

        Assert.Equal("a", normalized.Nodes[0].Properties[0].Name);
        Assert.Equal("z", normalized.Nodes[0].Properties[1].Name);
        Assert.Throws<ArgumentNullException>(
            () => NodalSchemaSnapshotSerializer.Serialize(null!));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new NodalSchemaSnapshot(0, [], []).Normalize());
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
