using Neo4j.Driver;
using Nodal.Core;
using Nodal.Core.Metadata;
using Nodal.Core.Query;
using Nodal.Neo4j;

namespace Nodal.IntegrationTests;

public sealed class Neo4jConnectionTests
{
    [Neo4jIntegrationFact]
    [Trait("Category", "Integration")]
    [Trait("Provider", "Neo4j")]
    public async Task UnitOfWorkCommitsReadsAndDeletesThroughRealBoltConnection()
    {
        var settings = Settings.FromEnvironment();
        await ResetDatabaseAsync(settings);
        await using var provider = CreateProvider(settings);
        var context = new SocialContext(provider);
        var ada = new Person("person-1", "Ada");
        var alan = new Person("person-2", "Alan");
        var relation = new Knows(2020);

        context.People.Add(ada);
        context.People.Add(alan);
        context.Friendships.Connect(ada, relation, alan);
        var created = await context.SaveChangesAsync();

        var people = await context.People.Query().ToListAsync();
        var acquaintances = await context.People
            .Match(person => person.Id == ada.Id)
            .Traverse(context.Friendships)
            .ToListAsync();
        var friendshipPath = Assert.Single(await context.People
            .Match(person => person.Id == ada.Id)
            .TraversePath(context.Friendships)
            .WhereRelation(edge => edge.SinceYear >= 2020)
            .ToListAsync());
        Assert.Equal(2, created.AffectedNodes);
        Assert.Equal(1, created.AffectedRelations);
        Assert.True(created.IsAtomic);
        Assert.Equal(["Ada", "Alan"], people.Select(person => person.Name).OrderBy(name => name));
        Assert.Equal(["Alan"], acquaintances.Select(person => person.Name));
        Assert.Equal(2020, friendshipPath.Relation.SinceYear);

        friendshipPath.Relation.SinceYear = 2025;
        context.Friendships.Update(
            friendshipPath.Source,
            friendshipPath.Relation,
            friendshipPath.Target);
        var updated = await context.SaveChangesAsync();
        Assert.Equal(1, updated.AffectedRelations);

        context.Friendships.Disconnect(ada, relation, alan);
        context.People.Remove(ada);
        context.People.Remove(alan);
        await context.SaveChangesAsync();

        Assert.Empty(await context.People.Query().ToListAsync());
    }

    [Neo4jIntegrationFact]
    [Trait("Category", "Integration")]
    [Trait("Provider", "Neo4j")]
    public async Task SerializationFailureRollsBackEarlierCommandsInTransaction()
    {
        var settings = Settings.FromEnvironment();
        await ResetDatabaseAsync(settings);
        await using var provider = CreateProvider(settings);
        var context = new FailureContext(provider);
        context.People.Add(new Person("person-rollback", "Must Roll Back"));
        context.BadNodes.Add(new BadNode("bad-1", Stream.Null));

        await Assert.ThrowsAnyAsync<Exception>(async () => await context.SaveChangesAsync());

        Assert.Empty(await context.People.Query().ToListAsync());
    }

    private static Neo4jProvider CreateProvider(Settings settings) => new(new Neo4jOptions
    {
        Endpoint = settings.Endpoint,
        Username = settings.Username,
        Password = settings.Password,
        Database = settings.Database,
    });

    private static async Task ResetDatabaseAsync(Settings settings)
    {
        await using var driver = GraphDatabase.Driver(
            settings.Endpoint,
            AuthTokens.Basic(settings.Username, settings.Password));
        await using var session = driver.AsyncSession(builder => builder.WithDatabase(settings.Database));
        var cursor = await session.RunAsync("MATCH (`node`) DETACH DELETE `node`");
        await cursor.ConsumeAsync();
    }

    private sealed class SocialContext(Neo4jProvider provider) : NodalContext(provider)
    {
        public GraphSet<Person> People => Set<Person>();

        public RelationSet<Person, Knows, Person> Friendships => Relations<Person, Knows, Person>();
    }

    private sealed class FailureContext(Neo4jProvider provider) : NodalContext(provider)
    {
        public GraphSet<Person> People => Set<Person>();

        public GraphSet<BadNode> BadNodes => Set<BadNode>();
    }

    [GraphNode("Person")]
    private sealed record Person([property: GraphKey] string Id, string Name);

    [GraphRelation("KNOWS")]
    private sealed class Knows(int sinceYear)
    {
        public int SinceYear { get; set; } = sinceYear;
    }

    [GraphNode("BadNode")]
    private sealed record BadNode([property: GraphKey] string Id, Stream Payload);

    private sealed record Settings(
        Uri Endpoint,
        string Username,
        string Password,
        string Database)
    {
        public static Settings FromEnvironment() => new(
            new Uri(Environment.GetEnvironmentVariable("NODAL_NEO4J_ENDPOINT")!, UriKind.Absolute),
            Environment.GetEnvironmentVariable("NODAL_NEO4J_USERNAME")!,
            Environment.GetEnvironmentVariable("NODAL_NEO4J_PASSWORD")!,
            Environment.GetEnvironmentVariable("NODAL_NEO4J_DATABASE") ?? "neo4j");
    }
}
