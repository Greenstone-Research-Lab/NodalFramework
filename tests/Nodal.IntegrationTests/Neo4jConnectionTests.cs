using Neo4j.Driver;
using Nodal.Core;
using Nodal.Core.Metadata;
using Nodal.Core.Migrations;
using Nodal.Core.Query;
using Nodal.Migrations;
using Nodal.Neo4j;

namespace Nodal.IntegrationTests;

public sealed class Neo4jConnectionTests
{
    [Neo4jIntegrationFact]
    [Trait("Category", "Integration")]
    [Trait("Provider", "Neo4j")]
    public async Task MigrationLifecycleAppliesNoOpsDetectsDriftAndReverts()
    {
        var settings = Settings.FromEnvironment();
        await ResetMigrationDatabaseAsync(settings);
        await using var provider = CreateProvider(settings);
        var runner = new MigrationRunner(provider);
        var migration = new LifecycleMigration();

        var applied = await runner.MigrateAsync([migration]);
        var noOp = await runner.PlanAsync([migration]);
        var history = await provider.MigrationHistory.GetMigrationHistoryAsync();
        var schema = await provider.SchemaIntrospector.CaptureAsync();

        Assert.Single(applied.Executions);
        Assert.True(noOp.IsEmpty);
        Assert.Equal(MigrationExecutionState.Applied, history[migration.Id].State);
        Assert.Contains(schema.Constraints ?? [], item =>
            item.Name == "nodal_uq_M3Person_email");
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await runner.PlanAsync([new DriftedLifecycleMigration()]));

        await runner.RevertAsync(migration);

        Assert.Empty(await provider.MigrationExecutor.GetAppliedMigrationsAsync());
        schema = await provider.SchemaIntrospector.CaptureAsync();
        Assert.DoesNotContain(schema.Constraints ?? [], item =>
            item.Name == "nodal_uq_M3Person_email");
    }

    [Neo4jIntegrationFact]
    [Trait("Category", "Integration")]
    [Trait("Provider", "Neo4j")]
    public async Task FailedMigrationIsRetryableAndNeverAppearsApplied()
    {
        var settings = Settings.FromEnvironment();
        await ResetMigrationDatabaseAsync(settings);
        await CreateDuplicateMigrationDataAsync(settings);
        await using var provider = CreateProvider(settings);
        var runner = new MigrationRunner(provider);
        var migration = new FailingConstraintMigration();

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await runner.MigrateAsync([migration]));

        var applied = await provider.MigrationExecutor.GetAppliedMigrationsAsync();
        var history = await provider.MigrationHistory.GetMigrationHistoryAsync();
        var retry = await runner.PlanAsync([migration]);
        var schema = await provider.SchemaIntrospector.CaptureAsync();

        Assert.DoesNotContain(migration.Id, applied.Keys);
        Assert.Equal(MigrationExecutionState.Failed, history[migration.Id].State);
        Assert.NotNull(history[migration.Id].Failure);
        Assert.Single(retry.Executions);
        Assert.DoesNotContain(schema.Constraints ?? [], item =>
            item.Name == "nodal_uq_M3Duplicate_email");
    }

    [Neo4jIntegrationFact]
    [Trait("Category", "Integration")]
    [Trait("Provider", "Neo4j")]
    public async Task CancelledMigrationDoesNotCreateHistoryOrSchemaObjects()
    {
        var settings = Settings.FromEnvironment();
        await ResetMigrationDatabaseAsync(settings);
        await using var provider = CreateProvider(settings);
        var runner = new MigrationRunner(provider);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await runner.MigrateAsync([new LifecycleMigration()], cancellation.Token));

        Assert.Empty(await provider.MigrationHistory.GetMigrationHistoryAsync());
        var schema = await provider.SchemaIntrospector.CaptureAsync();
        Assert.DoesNotContain(schema.Constraints ?? [], item =>
            item.Name == "nodal_uq_M3Person_email");
    }

    [Neo4jIntegrationFact]
    [Trait("Category", "Integration")]
    [Trait("Provider", "Neo4j")]
    public async Task MigrationCheckpointRoundTripsThroughLiveBoltStore()
    {
        var settings = Settings.FromEnvironment();
        await using var driver = GraphDatabase.Driver(
            settings.Endpoint,
            AuthTokens.Basic(settings.Username, settings.Password));
        var store = new Neo4jMigrationCheckpointStore(driver, settings.Database);
        var name = $"integration-{Guid.NewGuid():N}";
        var checkpoint = new MigrationBackfillCheckpoint(
            name, "page-2", 25, DateTimeOffset.UtcNow);

        await store.SaveAsync(checkpoint);
        var loaded = await store.GetAsync(name);

        Assert.NotNull(loaded);
        Assert.Equal(checkpoint.BackfillName, loaded!.BackfillName);
        Assert.Equal(checkpoint.ContinuationToken, loaded.ContinuationToken);
        Assert.Equal(checkpoint.Processed, loaded.Processed);

        await store.RemoveAsync(name);
        Assert.Null(await store.GetAsync(name));
    }

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
        var detachedContext = new SocialContext(provider);
        string[] selectedIds = [ada.Id, alan.Id];
        var paged = await detachedContext.People.Query()
            .Where(person => selectedIds.Contains(person.Id) && person.Name.StartsWith("Ad"))
            .OrderBy(person => person.Name)
            .Skip(0)
            .Take(1)
            .Distinct()
            .AsNoTracking()
            .ToListAsync();
        var raw = await detachedContext.Database.QueryRawAsync<Person>(
            "MATCH (`node`:`Person`) WHERE `node`.`Id` = $id RETURN `node`",
            new Dictionary<string, object?> { ["id"] = alan.Id });
        var subgraph = await detachedContext.People.Match(person => person.Id == ada.Id)
            .Traverse(detachedContext.Friendships)
            .WithoutCycles()
            .ToSubgraphAsync();
        var count = await detachedContext.People.Query().CountAsync();
        Assert.Equal(2, created.AffectedNodes);
        Assert.Equal(1, created.AffectedRelations);
        Assert.True(created.IsAtomic);
        Assert.Equal(["Ada", "Alan"], people.Select(person => person.Name).OrderBy(name => name));
        Assert.Equal(["Alan"], acquaintances.Select(person => person.Name));
        var pathContext = new SocialContext(provider);
        var shortest = await pathContext.People.Match(person => person.Id == ada.Id)
            .ShortestPathTo(
                pathContext.People.Match(person => person.Id == alan.Id),
                pathContext.Friendships)
            .SingleAsync();
        Assert.Equal(1, shortest.HopCount);
        Assert.Equal([ada.Id, alan.Id], shortest.Nodes.Select(person => person.Id));
        Assert.Empty(await pathContext.People.Match(person => person.Id == ada.Id)
            .ShortestPathTo(
                pathContext.People.Match(person => person.Id == "missing"),
                pathContext.Friendships)
            .ToListAsync());
        using var cancelledPath = new CancellationTokenSource();
        cancelledPath.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await pathContext.People.Match(person => person.Id == ada.Id)
                .ShortestPathTo(
                    pathContext.People.Match(person => person.Id == alan.Id),
                    pathContext.Friendships)
                .ToListAsync(cancelledPath.Token));
        Assert.Equal(2020, friendshipPath.Relation.SinceYear);
        Assert.Equal("Ada", Assert.Single(paged).Name);
        Assert.Empty(detachedContext.ChangeTracker.Entries());
        Assert.Equal("Alan", Assert.Single(raw).Name);
        Assert.Equal(2, subgraph.Nodes.Count);
        Assert.Single(subgraph.RelationRecords);
        Assert.Equal(2, count);

        Assert.Equal("Ada", (await detachedContext.People.Query().OrderBy(person => person.Name).FirstAsync()).Name);
        Assert.Equal("Alan", (await detachedContext.People.Match(person => person.Id == alan.Id).SingleAsync()).Name);
        Assert.True(await detachedContext.People.Match(person => person.Name.Contains("da")).AnyAsync());
        var projected = await detachedContext.People.Query().OrderBy(person => person.Name)
            .Select(person => new { person.Id, person.Name })
            .ToListAsync();
        Assert.Equal(2, projected.Count);
        var streamed = new List<string>();
        await foreach (var person in detachedContext.People.Query().OrderBy(candidate => candidate.Name).AsAsyncEnumerable())
        {
            streamed.Add(person.Name);
        }
        Assert.Equal(["Ada", "Alan"], streamed);

        var writer = new SocialContext(provider);
        writer.People.Update(new Person(ada.Id, "Ada Byron"));
        await writer.SaveChangesAsync();
        await context.People.ReloadAsync(ada);
        Assert.Equal("Ada Byron", ada.Name);

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

    private static async Task ResetMigrationDatabaseAsync(Settings settings)
    {
        await ResetDatabaseAsync(settings);
        await using var driver = GraphDatabase.Driver(
            settings.Endpoint,
            AuthTokens.Basic(settings.Username, settings.Password));
        await using var session = driver.AsyncSession(builder => builder.WithDatabase(settings.Database));
        foreach (var command in new[]
        {
            "DROP CONSTRAINT `nodal_uq_M3Person_email` IF EXISTS",
            "DROP INDEX `nodal_ix_M3Person_email` IF EXISTS",
            "DROP CONSTRAINT `nodal_uq_M3Duplicate_email` IF EXISTS",
        })
        {
            var cursor = await session.RunAsync(command);
            await cursor.ConsumeAsync();
        }
    }

    private static async Task CreateDuplicateMigrationDataAsync(Settings settings)
    {
        await using var driver = GraphDatabase.Driver(
            settings.Endpoint,
            AuthTokens.Basic(settings.Username, settings.Password));
        await using var session = driver.AsyncSession(builder => builder.WithDatabase(settings.Database));
        var cursor = await session.RunAsync(
            "CREATE (:`M3Duplicate` {`Id`: 'one', `email`: 'duplicate'}), " +
            "(:`M3Duplicate` {`Id`: 'two', `email`: 'duplicate'})");
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

    [GraphNode("M3Person")]
    private sealed record M3Person(
        [property: GraphKey] string Id,
        [property: GraphProperty("email")] string Email);

    [GraphNode("M3Duplicate")]
    private sealed record M3Duplicate(
        [property: GraphKey] string Id,
        [property: GraphProperty("email")] string Email);

    private sealed class LifecycleMigration : NodalMigration
    {
        public override string Id => "m3_001_lifecycle";

        protected override void Up(MigrationBuilder migration) =>
            migration.CreateUniqueConstraint<M3Person, string>(person => person.Email);

        protected override void Down(MigrationBuilder migration) =>
            migration.DropUniqueConstraint<M3Person, string>(person => person.Email);
    }

    private sealed class DriftedLifecycleMigration : NodalMigration
    {
        public override string Id => "m3_001_lifecycle";

        protected override void Up(MigrationBuilder migration) =>
            migration.CreateIndex<M3Person, string>(person => person.Email);

        protected override void Down(MigrationBuilder migration) =>
            migration.DropIndex<M3Person, string>(person => person.Email);
    }

    private sealed class FailingConstraintMigration : NodalMigration
    {
        public override string Id => "m3_002_failing_constraint";

        protected override void Up(MigrationBuilder migration) =>
            migration.CreateUniqueConstraint<M3Duplicate, string>(person => person.Email);

        protected override void Down(MigrationBuilder migration) =>
            migration.DropUniqueConstraint<M3Duplicate, string>(person => person.Email);
    }

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
