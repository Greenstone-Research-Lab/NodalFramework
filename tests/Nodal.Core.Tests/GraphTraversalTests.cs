using Nodal.Core.Execution;
using Nodal.Core.Metadata;
using Nodal.Core.Providers;
using Nodal.Core.Query;

namespace Nodal.Core.Tests;

public sealed class GraphTraversalTests
{
    [Fact]
    public void OutgoingTraversalFiltersReachedNodeUsingItsMappedProperties()
    {
        var context = new NetworkContext(new UnusedProvider());

        var model = context.People
            .Match(person => person.Id == "person-1")
            .Traverse(context.Employment)
            .Where(company => company.Country == "TR")
            .Take(10)
            .ToQueryModel();

        Assert.Equal("node1", model.ResultAlias);
        Assert.Equal("Company", model.ResultNodeType);
        Assert.Equal(10, model.Limit);
        var root = Assert.IsType<GraphComparisonPredicate>(model.Predicate);
        Assert.Equal("person_id", root.PropertyName);
        var traversal = Assert.Single(model.Traversals);
        Assert.Equal("WORKS_AT", traversal.RelationType);
        Assert.Equal("node", traversal.SourceAlias);
        Assert.Equal("relation1", traversal.RelationAlias);
        Assert.Equal("node1", traversal.TargetAlias);
        Assert.Equal(GraphTraversalDirection.Outgoing, traversal.Direction);
        var target = Assert.IsType<GraphComparisonPredicate>(traversal.Predicate);
        Assert.Equal("country_code", target.PropertyName);
        Assert.Equal(["p0", "p1"], model.Parameters.Select(parameter => parameter.Name));
    }

    [Fact]
    public void IncomingTraversalReturnsDeclaredRelationshipSource()
    {
        var context = new NetworkContext(new UnusedProvider());

        var model = context.Companies.Query()
            .TraverseIncoming(context.Employment)
            .Where(person => person.Name == "Ada")
            .ToQueryModel();

        Assert.Equal("Person", model.ResultNodeType);
        var traversal = Assert.Single(model.Traversals);
        Assert.Equal(GraphTraversalDirection.Incoming, traversal.Direction);
        Assert.Equal("display_name", Assert.IsType<GraphComparisonPredicate>(traversal.Predicate).PropertyName);
    }

    [Fact]
    public void UndirectedMetadataProducesDirectionAgnosticTraversal()
    {
        var context = new NetworkContext(new UnusedProvider());

        var model = context.People.Query().Traverse(context.Friendships).ToQueryModel();

        Assert.Equal(GraphTraversalDirection.Undirected, Assert.Single(model.Traversals).Direction);
    }

    [Fact]
    public void MultipleTraversalsUseStableSequentialAliases()
    {
        var context = new NetworkContext(new UnusedProvider());

        var model = context.People.Query()
            .Traverse(context.Employment)
            .Traverse(context.Locations)
            .Where(city => city.Name == "Istanbul")
            .ToQueryModel();

        Assert.Equal("City", model.ResultNodeType);
        Assert.Equal("node2", model.ResultAlias);
        Assert.Collection(
            model.Traversals,
            step =>
            {
                Assert.Equal("node", step.SourceAlias);
                Assert.Equal("node1", step.TargetAlias);
            },
            step =>
            {
                Assert.Equal("node1", step.SourceAlias);
                Assert.Equal("node2", step.TargetAlias);
                Assert.Equal("relation2", step.RelationAlias);
            });
    }

    [Fact]
    public void QueryWithoutTraversalReturnsRootMetadata()
    {
        var model = new GraphSet<Person>().Query().ToQueryModel();

        Assert.Equal("node", model.ResultAlias);
        Assert.Equal("Person", model.ResultNodeType);
        Assert.Empty(model.Traversals);
    }

    [Fact]
    public void TraversalRejectsNullRelationSet()
    {
        var context = new NetworkContext(new UnusedProvider());

        Assert.Throws<ArgumentNullException>(
            () => context.People.Query().Traverse<WorksAt, Company>(null!));
        Assert.Throws<ArgumentNullException>(
            () => context.Companies.Query().TraverseIncoming<Person, WorksAt>(null!));
        Assert.Throws<ArgumentNullException>(
            () => context.People.Query().TraversePath<WorksAt, Company>(null!));
    }

    [Fact]
    public void PathProjectionFiltersRelationshipAndTargetUsingMappedProperties()
    {
        var context = new NetworkContext(new UnusedProvider());

        var model = context.People
            .Match(person => person.Id == "person-1")
            .TraversePath(context.Employment)
            .WhereRelation(relation => relation.Role == "Engineer")
            .WhereRelation(relation => relation.Since >= new DateTime(2020, 1, 1))
            .WhereTarget(company => company.Country == "TR")
            .Take(3)
            .ToQueryModel();

        Assert.Equal(GraphQueryProjection.Path, model.Projection);
        Assert.Equal(3, model.Limit);
        var traversal = Assert.Single(model.Traversals);
        var relation = Assert.IsType<GraphLogicalPredicate>(traversal.RelationPredicate);
        Assert.Equal("role_name", Assert.IsType<GraphComparisonPredicate>(relation.Left).PropertyName);
        Assert.Equal("country_code", Assert.IsType<GraphComparisonPredicate>(traversal.Predicate).PropertyName);
        Assert.Equal(4, model.Parameters.Count);
    }

    [Fact]
    public void PathQueryRejectsInvalidLimit()
    {
        var context = new NetworkContext(new UnusedProvider());
        var query = context.People.Query().TraversePath(context.Employment);

        Assert.Throws<ArgumentOutOfRangeException>(() => query.Take(0));
    }

    private sealed class NetworkContext(IGraphProvider provider) : NodalContext(provider)
    {
        public GraphSet<Person> People => Set<Person>();

        public GraphSet<Company> Companies => Set<Company>();

        public RelationSet<Person, WorksAt, Company> Employment => Relations<Person, WorksAt, Company>();

        public RelationSet<Person, FriendOf, Person> Friendships => Relations<Person, FriendOf, Person>();

        public RelationSet<Company, LocatedIn, City> Locations => Relations<Company, LocatedIn, City>();
    }

    [GraphNode("Person")]
    private sealed record Person(
        [property: GraphKey]
        [property: GraphProperty("person_id")]
        string Id,
        [property: GraphProperty("display_name")]
        string Name);

    [GraphNode("Company")]
    private sealed record Company(
        [property: GraphKey]
        string Id,
        [property: GraphProperty("country_code")]
        string Country);

    [GraphNode("City")]
    private sealed record City([property: GraphKey] string Id, string Name);

    [GraphRelation("WORKS_AT")]
    private sealed record WorksAt(
        DateTime Since,
        [property: GraphProperty("role_name")] string Role = "");

    [GraphRelation("FRIEND_OF", Directed = false)]
    private sealed record FriendOf;

    [GraphRelation("LOCATED_IN")]
    private sealed record LocatedIn;

    private sealed class UnusedProvider : IGraphProvider
    {
        public IGraphQueryCompiler QueryCompiler => throw new NotSupportedException();

        public IGraphCommandExecutor CommandExecutor => throw new NotSupportedException();

        public IGraphResultMaterializer ResultMaterializer => throw new NotSupportedException();
    }
}
