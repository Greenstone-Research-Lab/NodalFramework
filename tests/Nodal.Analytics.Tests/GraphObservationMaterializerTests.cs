using System.Collections.ObjectModel;
using System.Text.Json;
using Nodal.Analytics.Observations;
using Nodal.Core.Execution;

namespace Nodal.Analytics.Tests;

public sealed class GraphObservationMaterializerTests
{
    [Fact]
    public void MaterializesProjectedImmutableObservationAndPreservesOrder()
    {
        var tags = new List<object?> { "estonian", 2 };
        var nested = new Dictionary<string, object?> { ["score"] = 9 };
        using var document = JsonDocument.Parse("{\"temperature\":12}");
        var firstProperties = Properties(
            ("name", "Kohvik"),
            ("tags", tags),
            ("nested", nested),
            ("weather", document.RootElement),
            ("secret", "excluded"));
        var relationProperties = Properties(("orderedAt", "2026-08-29"), ("payment", "card"));
        var result = Result(
            [Node("Restaurant", "r-1", firstProperties), Node("Food", "f-1", Properties(("name", "Soup")))],
            [Relation("SELLS", "e-1", "r-1", "f-1", relationProperties)]);
        var options = new GraphObservationOptions
        {
            NodeProperties = Set("name", "tags", "nested", "weather"),
            RelationProperties = Set("orderedAt"),
        };

        var observation = GraphObservationMaterializer.Materialize(result, options);
        tags[0] = "changed";
        nested["score"] = 0;
        firstProperties["name"] = "changed";

        Assert.Equal(["Restaurant", "Food"], observation.Nodes.Select(node => node.Identity.Type));
        Assert.Equal("Kohvik", observation.Nodes[0].Properties["name"]);
        Assert.Equal("estonian", Assert.IsAssignableFrom<IReadOnlyList<object?>>(observation.Nodes[0].Properties["tags"])[0]);
        Assert.Equal(9, Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(observation.Nodes[0].Properties["nested"])["score"]);
        Assert.Equal(12, Assert.IsType<JsonElement>(observation.Nodes[0].Properties["weather"]).GetProperty("temperature").GetInt32());
        Assert.DoesNotContain("secret", observation.Nodes[0].Properties);
        Assert.Single(observation.Relations);
        Assert.Equal("Restaurant", observation.Relations[0].Source.Type);
        Assert.Equal("Food", observation.Relations[0].Target.Type);
        Assert.Equal("2026-08-29", observation.Relations[0].Properties["orderedAt"]);
        Assert.DoesNotContain("payment", observation.Relations[0].Properties);
    }

    [Fact]
    public void DefaultsExcludePropertiesAndAcceptEmptyResult()
    {
        var empty = GraphObservationMaterializer.Materialize(Result([], []));
        var nodeOnly = GraphObservationMaterializer.Materialize(
            Result([Node("Food", 1, Properties(("name", "Soup")))], []));

        Assert.Empty(empty.Nodes);
        Assert.Empty(empty.Relations);
        Assert.Empty(nodeOnly.Nodes[0].Properties);
        Assert.Equal(GraphObservationOptions.DefaultMaxNodes, new GraphObservationOptions().MaxNodes);
        Assert.Equal(GraphObservationOptions.DefaultMaxRelations, new GraphObservationOptions().MaxRelations);
        Assert.Equal(
            GraphObservationOptions.DefaultMaxPropertyCollectionItems,
            new GraphObservationOptions().MaxPropertyCollectionItems);
        Assert.Equal(GraphObservationOptions.DefaultMaxPropertyDepth, new GraphObservationOptions().MaxPropertyDepth);
    }

    [Fact]
    public void ParallelRelationshipsWithDistinctIdentitiesArePreserved()
    {
        var result = Result(
            [Node("Person", "p", Properties()), Node("Food", "f", Properties())],
            [
                Relation("ORDERED", 1, "p", "f", Properties()),
                Relation("ORDERED", 2, "p", "f", Properties()),
            ]);

        var observation = GraphObservationMaterializer.Materialize(result);

        Assert.Equal(["1", "2"], observation.Relations.Select(relation => relation.Key.Value));
    }

    [Theory]
    [InlineData(0, 1, "MaxNodes")]
    [InlineData(1, 0, "MaxRelations")]
    [InlineData(-1, 1, "MaxNodes")]
    [InlineData(1, -1, "MaxRelations")]
    public void NonPositiveLimitsAreRejected(int maxNodes, int maxRelations, string parameterName)
    {
        var options = new GraphObservationOptions { MaxNodes = maxNodes, MaxRelations = maxRelations };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => GraphObservationMaterializer.Materialize(Result([], []), options));

        Assert.Equal(parameterName, exception.ParamName);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExceededBoundsFailWithoutPartialObservation(bool exceedNodes)
    {
        var result = Result(
            [Node("Food", "f", Properties()), Node("Food", "g", Properties())],
            [Relation("RELATED", "e", "f", "g", Properties())]);
        var options = exceedNodes
            ? new GraphObservationOptions { MaxNodes = 1 }
            : new GraphObservationOptions { MaxRelations = 0 };

        GraphObservationLimitExceededException exception;
        if (exceedNodes)
        {
            exception = Assert.Throws<GraphObservationLimitExceededException>(
                () => GraphObservationMaterializer.Materialize(result, options));
        }
        else
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => GraphObservationMaterializer.Materialize(result, options));
            return;
        }

        Assert.Equal("node", exception.ElementKind);
        Assert.Equal(2, exception.ActualCount);
        Assert.Equal(1, exception.MaximumCount);
        Assert.Contains("count 2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RelationshipLimitFailureReportsRelationshipCounts()
    {
        var result = Result(
            [Node("Food", "f", Properties())],
            [Relation("SELF", "e-1", "f", "f", Properties()), Relation("SELF", "e-2", "f", "f", Properties())]);

        var exception = Assert.Throws<GraphObservationLimitExceededException>(
            () => GraphObservationMaterializer.Materialize(result, new GraphObservationOptions { MaxRelations = 1 }));

        Assert.Equal("relationship", exception.ElementKind);
        Assert.Equal(2, exception.ActualCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NonPositivePropertyBoundsAreRejected(bool collectionLimit)
    {
        var options = collectionLimit
            ? new GraphObservationOptions { MaxPropertyCollectionItems = 0 }
            : new GraphObservationOptions { MaxPropertyDepth = 0 };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => GraphObservationMaterializer.Materialize(Result([], []), options));

        Assert.Equal(collectionLimit ? "MaxPropertyCollectionItems" : "MaxPropertyDepth", exception.ParamName);
    }

    [Fact]
    public void ProjectedCollectionLimitIsEnforcedWhileEnumerating()
    {
        var result = Result([Node("Food", "f", Properties(("items", new List<int> { 1, 2 })))], []);
        var options = new GraphObservationOptions
        {
            NodeProperties = Set("items"),
            MaxPropertyCollectionItems = 1,
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => GraphObservationMaterializer.Materialize(result, options));

        Assert.Contains("collection-item limit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectedDictionaryLimitIsEnforcedBeforeCopying()
    {
        var value = Properties(("first", 1), ("second", 2));
        var result = Result([Node("Food", "f", Properties(("value", value)))], []);
        var options = new GraphObservationOptions
        {
            NodeProperties = Set("value"),
            MaxPropertyCollectionItems = 1,
        };

        Assert.Throws<InvalidOperationException>(() => GraphObservationMaterializer.Materialize(result, options));
    }

    [Fact]
    public void ProjectedNestingDepthRejectsRecursiveValues()
    {
        var recursive = new List<object?>();
        recursive.Add(recursive);
        var result = Result([Node("Food", "f", Properties(("value", recursive)))], []);
        var options = new GraphObservationOptions
        {
            NodeProperties = Set("value"),
            MaxPropertyDepth = 2,
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => GraphObservationMaterializer.Materialize(result, options));

        Assert.Contains("nesting depth", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateNodeIdentityIsRejected()
    {
        var result = Result([Node("Food", "f", Properties()), Node("Food", "f", Properties())], []);

        Assert.Throws<ArgumentException>(() => GraphObservationMaterializer.Materialize(result));
    }

    [Fact]
    public void DuplicateRelationshipIdentityIsRejected()
    {
        var result = Result(
            [Node("Food", "f", Properties())],
            [Relation("SELF", "e", "f", "f", Properties()), Relation("SELF", "e", "f", "f", Properties())]);

        Assert.Throws<ArgumentException>(() => GraphObservationMaterializer.Materialize(result));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MissingRelationshipEndpointIsRejected(bool missingSource)
    {
        var relation = missingSource
            ? Relation("SELLS", "e", "missing", "f", Properties())
            : Relation("SELLS", "e", "f", "missing", Properties());

        var exception = Assert.Throws<ArgumentException>(
            () => GraphObservationMaterializer.Materialize(Result([Node("Food", "f", Properties())], [relation])));

        Assert.Contains(missingSource ? "source" : "target", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AmbiguousEndpointAcrossNodeTypesIsRejected()
    {
        var result = Result(
            [Node("Person", "1", Properties()), Node("Restaurant", "1", Properties())],
            [Relation("SELF", "e", "1", "1", Properties())]);

        Assert.Throws<ArgumentException>(() => GraphObservationMaterializer.Materialize(result));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EmptyRecordTypeIsRejected(bool nodeType)
    {
        var result = nodeType
            ? Result([Node(" ", "n", Properties())], [])
            : Result([Node("Food", "n", Properties())], [Relation("", "e", "n", "n", Properties())]);

        Assert.Throws<ArgumentException>(() => GraphObservationMaterializer.Materialize(result));
    }

    [Fact]
    public void InvalidProjectionIsRejected()
    {
        var nullProjection = new GraphObservationOptions { NodeProperties = null! };
        var emptyName = new GraphObservationOptions { RelationProperties = Set(" ") };

        Assert.Throws<ArgumentNullException>(() => GraphObservationMaterializer.Materialize(Result([], []), nullProjection));
        Assert.Throws<ArgumentException>(() => GraphObservationMaterializer.Materialize(Result([], []), emptyName));
    }

    [Fact]
    public void UnsupportedProjectedReferenceValueIsRejectedWithoutItsValue()
    {
        var value = new SensitiveReference();
        var result = Result([Node("Food", "f", Properties(("payload", value)))], []);

        var exception = Assert.Throws<InvalidOperationException>(
            () => GraphObservationMaterializer.Materialize(
                result,
                new GraphObservationOptions { NodeProperties = Set("payload") }));

        Assert.Contains(nameof(SensitiveReference), exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(value.ToString(), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NullResultAndNullRecordsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => GraphObservationMaterializer.Materialize(null!));
        Assert.Throws<ArgumentException>(() => GraphObservationMaterializer.Materialize(Result([null!], [])));
        Assert.Throws<ArgumentException>(() => GraphObservationMaterializer.Materialize(Result([], [null!])));
    }

    private static GraphQueryResult Result(
        IReadOnlyList<GraphNodeRecord> nodes,
        IReadOnlyList<GraphRelationRecord> relations) => new(nodes, relations);

    private static GraphNodeRecord Node(
        string type,
        object id,
        IReadOnlyDictionary<string, object?> properties) => new(type, id, properties);

    private static GraphRelationRecord Relation(
        string type,
        object id,
        object source,
        object target,
        IReadOnlyDictionary<string, object?> properties) => new(type, id, source, target, properties);

    private static Dictionary<string, object?> Properties(params (string Name, object? Value)[] values) =>
        values.ToDictionary(item => item.Name, item => item.Value, StringComparer.Ordinal);

    private static HashSet<string> Set(params string[] values) =>
        new HashSet<string>(values, StringComparer.Ordinal);

    private sealed class SensitiveReference
    {
        public override string ToString() => "sensitive-value";
    }
}
