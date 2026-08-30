using Nodal.Core.Modeling;
using Nodal.Modeling.CodeGeneration;

namespace Nodal.Modeling.CodeGeneration.Tests;

public sealed class GraphModelCodeGeneratorTests
{
    [Fact]
    public void GeneratesDeterministicOneTypePerFileContextManifestAndAotMetadata()
    {
        var descriptor = Descriptor();

        var first = GraphModelCodeGenerator.Generate(descriptor);
        var second = GraphModelCodeGenerator.Generate(descriptor);

        Assert.Equal(5, first.Count);
        Assert.Equal(first, second);
        Assert.Equal(first.OrderBy(file => file.RelativePath, StringComparer.Ordinal), first);
        var person = File(first, "Nodes/Person.cs");
        Assert.Contains("[GraphNode(\"Person\")]", person, StringComparison.Ordinal);
        Assert.Contains("[GraphKey]", person, StringComparison.Ordinal);
        Assert.Contains("public string Id { get; set; } = string.Empty;", person, StringComparison.Ordinal);
        Assert.Contains("public string[] Tags { get; set; } = [];", person, StringComparison.Ordinal);
        Assert.Contains("public DateTimeOffset? LastSeenAt { get; set; }", person, StringComparison.Ordinal);
        Assert.Contains("[GraphRelation(\"KNOWS\", Directed = false)]", File(first, "Relations/Knows.cs"), StringComparison.Ordinal);
        var context = File(first, "GeneratedGraphContext.cs");
        Assert.Contains("using Nodal.Core.Execution;", context, StringComparison.Ordinal);
        Assert.Contains("using Nodal.Core.Query;", context, StringComparison.Ordinal);
        Assert.Contains("RelationSet<Person, Knows, Person> KnowsSet", context, StringComparison.Ordinal);
        Assert.Contains(GraphModelDescriptorJson.ComputeFingerprint(descriptor), File(first, "NodalGeneratedModelManifest.cs"), StringComparison.Ordinal);
        Assert.Contains("JsonSerializable(typeof(Person))", File(first, "NodalGeneratedJsonContext.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void MapsEveryPortableKindAndCompositeKeysWithoutMultipleGraphKeyAttributes()
    {
        var properties = Enum.GetValues<GraphValueKind>()
            .Where(kind => kind != GraphValueKind.Collection)
            .Select((kind, index) => new GraphPropertyDescriptor(
                $"p{index}",
                $"P{index}",
                kind,
                index % 2 == 0))
            .Append(new GraphPropertyDescriptor(
                "items", "Items", GraphValueKind.Collection, false, true, GraphValueKind.Identifier))
            .ToArray();
        var descriptor = new GraphModelDescriptor(
            GraphModelFormat.CurrentVersion,
            [new NodeTypeDescriptor("all", "All", "AllKinds", new GraphKeyDescriptor(["p0", "p1"]), properties)],
            []);

        var source = File(GraphModelCodeGenerator.Generate(descriptor), "Nodes/AllKinds.cs");

        Assert.Equal(1, Count(source, "[GraphKey]"));
        Assert.Contains("public string NodalCompositeKey", source, StringComparison.Ordinal);
        Assert.Contains("IReadOnlyDictionary<string, GraphValue>", source, StringComparison.Ordinal);
        Assert.Contains("GraphGeoPoint", source, StringComparison.Ordinal);
        Assert.Contains("Guid[] Items", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AppliesSafeCustomNamespaceAndContextNames()
    {
        var files = GraphModelCodeGenerator.Generate(Descriptor(), new GraphModelGeneratorOptions
        {
            RootNamespace = "Greenstone.Northwind",
            ContextName = "NorthwindGraphContext",
        });

        Assert.Contains(files, file => file.RelativePath == "NorthwindGraphContext.cs");
        Assert.Contains("namespace Greenstone.Northwind;", File(files, "NorthwindGraphContext.cs"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("bad-name", "Context")]
    [InlineData("Good.Namespace", "class")]
    [InlineData("Good..Namespace", "Context")]
    public void RejectsUnsafeConfiguration(string rootNamespace, string contextName)
    {
        Assert.Throws<ArgumentException>(() => GraphModelCodeGenerator.Generate(Descriptor(), new GraphModelGeneratorOptions
        {
            RootNamespace = rootNamespace,
            ContextName = contextName,
        }));
    }

    [Fact]
    public void RejectsUnsafeOrCollidingDescriptorClrNames()
    {
        var descriptor = Descriptor();
        var badProperty = descriptor with
        {
            Nodes = [descriptor.Nodes[0] with
            {
                Properties = descriptor.Nodes[0].Properties
                    .Append(new GraphPropertyDescriptor("bad", "bad-name", GraphValueKind.Text, false)).ToArray(),
            }],
        };
        var duplicateType = descriptor with
        {
            Relations = [descriptor.Relations[0] with { ClrName = descriptor.Nodes[0].ClrName }],
        };

        Assert.Throws<ArgumentException>(() => GraphModelCodeGenerator.Generate(badProperty));
        Assert.Throws<ArgumentException>(() => GraphModelCodeGenerator.Generate(duplicateType));
    }

    [Fact]
    public void RejectsCompositeKeyPropertyCollisionAndUnsupportedValueKind()
    {
        var descriptor = Descriptor();
        var compositeCollision = descriptor with
        {
            Nodes = [descriptor.Nodes[0] with
            {
                Key = new GraphKeyDescriptor(["id", "tags"]),
                Properties = descriptor.Nodes[0].Properties
                    .Append(new GraphPropertyDescriptor(
                        "composite", "NodalCompositeKey", GraphValueKind.Text, false)).ToArray(),
            }],
        };

        Assert.Throws<ArgumentException>(() => GraphModelCodeGenerator.Generate(compositeCollision));
    }

    private static GraphModelDescriptor Descriptor() => new(
        GraphModelFormat.CurrentVersion,
        [new NodeTypeDescriptor(
            "person",
            "Person",
            "Person",
            new GraphKeyDescriptor(["id"]),
            [
                new GraphPropertyDescriptor("id", "Id", GraphValueKind.Text, false),
                new GraphPropertyDescriptor("last_seen_at", "LastSeenAt", GraphValueKind.DateTimeOffset, true),
                new GraphPropertyDescriptor("tags", "Tags", GraphValueKind.Collection, false, true, GraphValueKind.Text),
            ])],
        [new RelationTypeDescriptor(
            "knows", "KNOWS", "Knows", "person", "person", false,
            [new GraphPropertyDescriptor("since", "Since", GraphValueKind.Date, true)])]);

    private static string File(IReadOnlyList<GeneratedSourceFile> files, string path) =>
        Assert.Single(files, file => file.RelativePath == path).Content;

    private static int Count(string value, string fragment) =>
        value.Split(fragment, StringSplitOptions.None).Length - 1;
}
