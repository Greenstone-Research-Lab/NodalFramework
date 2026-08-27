using Nodal.Core.Migrations;
using Nodal.Core.Query;

namespace Nodal.Core.Tests;

/// <summary>
/// Verifies capability validation for portable graph query features.
/// </summary>
public sealed class GraphQueryPreflightTests
{
    [Fact]
    public void ValidateAllowsVerifiedQueryFeatures()
    {
        var query = CreateQuery(minDepth: 1, maxDepth: 3, cycleBehavior: GraphCycleBehavior.SimplePath);
        var capabilities = CreateCapabilities(
            GraphQueryCapability.VariableLengthTraversal |
            GraphQueryCapability.SimplePath);

        GraphQueryPreflight.Validate(query, capabilities);
    }

    [Fact]
    public void ValidateRejectsOptionalTraversalBeforeExecution()
    {
        var query = CreateQuery(optional: true);

        var exception = Assert.Throws<NodalCapabilityNotSupportedException>(() =>
            GraphQueryPreflight.Validate(query, CreateCapabilities(GraphQueryCapability.None)));

        Assert.Equal("ExampleGraph", exception.ProviderName);
        Assert.Equal("NODAL-QUERY-OPTIONAL-TRAVERSAL", exception.CapabilityCode);
        Assert.Contains("1.0-test", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateRejectsVariableLengthTraversalBeforeExecution()
    {
        var query = CreateQuery(minDepth: 1, maxDepth: 2);

        var exception = Assert.Throws<NodalCapabilityNotSupportedException>(() =>
            GraphQueryPreflight.Validate(query, CreateCapabilities(GraphQueryCapability.None)));

        Assert.Equal("NODAL-QUERY-VARIABLE-LENGTH", exception.CapabilityCode);
    }

    [Fact]
    public void ValidateRejectsSimplePathBeforeExecution()
    {
        var query = CreateQuery(cycleBehavior: GraphCycleBehavior.SimplePath);

        var exception = Assert.Throws<NodalCapabilityNotSupportedException>(() =>
            GraphQueryPreflight.Validate(query, CreateCapabilities(GraphQueryCapability.None)));

        Assert.Equal("NODAL-QUERY-SIMPLE-PATH", exception.CapabilityCode);
    }

    [Fact]
    public void ValidateChecksRequirementsOfEachSetOperationOperand()
    {
        var variableLengthOperand = CreateQuery(minDepth: 1, maxDepth: 2);
        var plainOperand = new GraphQueryModel("Person", "person", null, [], null, []);
        var union = new GraphQueryModel(
            "Person",
            "person",
            null,
            [],
            null,
            [],
            SetOperation: new GraphSetOperation(GraphSetOperationKind.Union, variableLengthOperand, plainOperand));

        var exception = Assert.Throws<NodalCapabilityNotSupportedException>(() =>
            GraphQueryPreflight.Validate(union, CreateCapabilities(GraphQueryCapability.SetOperations)));

        Assert.Equal("NODAL-QUERY-VARIABLE-LENGTH", exception.CapabilityCode);
    }

    [Fact]
    public void ValidateAllowsAdvancedFeaturesWhenTheyAreExplicitlyAdvertised()
    {
        var query = new GraphQueryModel(
            "Person",
            "person",
            null,
            [],
            null,
            [],
            GraphQueryProjection.Row,
            ExistencePatterns:
            [new GraphExistencePattern("KNOWS", "Person", "person", "knows", "friend", GraphTraversalDirection.Outgoing, null, null)],
            MatchPatterns:
            [new GraphTraversalStep("KNOWS", "Person", "person", "knows2", "friend2", GraphTraversalDirection.Outgoing, null)],
            RowProjection: new GraphRowProjection([new GraphRowColumn("count", GraphRowColumnKind.Count, "person")]));
        var capabilities = CreateCapabilities(
            GraphQueryCapability.CorrelatedSubquery |
            GraphQueryCapability.MultiplePatterns |
            GraphQueryCapability.ServerSideProjection |
            GraphQueryCapability.Aggregation);

        GraphQueryPreflight.Validate(query, capabilities);
    }

    [Fact]
    public void ValidateRejectsMissingAdvancedFeatureCapabilities()
    {
        var exists = new GraphQueryModel(
            "Person", "person", null, [], null, [],
            ExistencePatterns:
            [new GraphExistencePattern("KNOWS", "Person", "person", "knows", "friend", GraphTraversalDirection.Outgoing, null, null)]);
        var pattern = new GraphQueryModel(
            "Person", "person", null, [], null, [],
            MatchPatterns:
            [new GraphTraversalStep("KNOWS", "Person", "person", "knows", "friend", GraphTraversalDirection.Outgoing, null)]);
        var row = new GraphQueryModel(
            "Person", "person", null, [], null, [], GraphQueryProjection.Row,
            RowProjection: new GraphRowProjection([new GraphRowColumn("name", GraphRowColumnKind.Property, "person", "Name")]));

        Assert.Equal("NODAL-QUERY-CORRELATED-SUBQUERY", Assert.Throws<NodalCapabilityNotSupportedException>(
            () => GraphQueryPreflight.Validate(exists, CreateCapabilities(GraphQueryCapability.None))).CapabilityCode);
        Assert.Equal("NODAL-QUERY-MULTIPLE-PATTERNS", Assert.Throws<NodalCapabilityNotSupportedException>(
            () => GraphQueryPreflight.Validate(pattern, CreateCapabilities(GraphQueryCapability.None))).CapabilityCode);
        Assert.Equal("NODAL-QUERY-SERVER-SIDE-PROJECTION", Assert.Throws<NodalCapabilityNotSupportedException>(
            () => GraphQueryPreflight.Validate(row, CreateCapabilities(GraphQueryCapability.None))).CapabilityCode);
    }

    private static GraphQueryCapabilities CreateCapabilities(GraphQueryCapability features) => new()
    {
        ProviderName = "ExampleGraph",
        TestedProviderVersion = "1.0-test",
        Features = features,
    };

    private static GraphQueryModel CreateQuery(
        bool optional = false,
        int minDepth = 1,
        int maxDepth = 1,
        GraphCycleBehavior cycleBehavior = GraphCycleBehavior.ProviderDefault) => new(
            NodeType: "Person",
            Alias: "person",
            Predicate: null,
            Parameters: [],
            Limit: null,
            Traversals:
            [
                new GraphTraversalStep(
                    RelationType: "KNOWS",
                    TargetNodeType: "Person",
                    SourceAlias: "person",
                    RelationAlias: "knows",
                    TargetAlias: "friend",
                    Direction: GraphTraversalDirection.Outgoing,
                    Predicate: null,
                    MinDepth: minDepth,
                    MaxDepth: maxDepth,
                    Optional: optional),
            ],
            CycleBehavior: cycleBehavior);
}
