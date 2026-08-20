using Nodal.Core.ChangeTracking;
using Nodal.Core.Execution;
using Nodal.Core.Metadata;
using Nodal.Core.Mutations;
using Nodal.Core.Providers;
using Nodal.Core.Query;

namespace Nodal.Core.Tests;

public sealed class GraphUnitOfWorkTests
{
    [Fact]
    public async Task SaveChangesOrdersNodesBeforeNewRelationsAndAcceptsEntries()
    {
        var provider = new RecordingMutationProvider(new GraphMutationResult(2, 1, true));
        var context = new SocialContext(provider);
        var ada = new Person("person-1", "Ada");
        var alan = new Person("person-2", "Alan");

        context.People.Add(ada);
        context.People.Add(alan);
        context.Friendships.Connect(ada, new Knows(2020), alan);

        var result = await context.SaveChangesAsync();

        Assert.Collection(
            provider.Executor.Plan!.Operations,
            operation => AssertCreateNode(operation, "person-1", "Ada"),
            operation => AssertCreateNode(operation, "person-2", "Alan"),
            operation =>
            {
                var relation = Assert.IsType<CreateRelationOperation>(operation);
                Assert.Equal("KNOWS", relation.RelationType);
                Assert.Equal("person-1", relation.Source.Value);
                Assert.Equal("person-2", relation.Target.Value);
                Assert.Equal(2020, relation.Properties["since_year"]);
                Assert.True(relation.Directed);
            });
        Assert.Equal(2, result.AffectedNodes);
        Assert.Equal(1, result.AffectedRelations);
        Assert.True(result.IsAtomic);
        Assert.Equal(3, result.Changes.Operations.Count);
        Assert.All(context.ChangeTracker.Entries(), entry => Assert.Equal(GraphEntryState.Unchanged, entry.State));
    }

    [Fact]
    public async Task UpdateCreatesMappedUpdateOperation()
    {
        var provider = new RecordingMutationProvider(new GraphMutationResult(1, 0, true));
        var context = new SocialContext(provider);
        var person = new Person("person-1", "Updated Ada");

        var entry = context.People.Update(person);
        var result = await context.SaveChangesAsync();

        var operation = Assert.IsType<UpdateNodeOperation>(Assert.Single(result.Changes.Operations));
        Assert.Equal("Person", operation.Identity.NodeType);
        Assert.Equal("person_id", operation.Identity.KeyProperty);
        Assert.Equal("Updated Ada", operation.Properties["display_name"]);
        Assert.Equal(GraphEntryState.Unchanged, entry.State);
    }

    [Fact]
    public async Task DeletingRelationIsPlannedBeforeDeletingNode()
    {
        var provider = new RecordingMutationProvider(new GraphMutationResult(1, 1, true));
        var context = new SocialContext(provider);
        var ada = new Person("person-1", "Ada");
        var alan = new Person("person-2", "Alan");
        var relation = new Knows(2020);

        context.Friendships.Disconnect(ada, relation, alan);
        var nodeEntry = context.People.Remove(alan);

        await context.SaveChangesAsync();

        Assert.Collection(
            provider.Executor.Plan!.Operations,
            operation => Assert.IsType<DeleteRelationOperation>(operation),
            operation => Assert.IsType<DeleteNodeOperation>(operation));
        Assert.Equal(GraphEntryState.Detached, nodeEntry.State);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task AddingThenRemovingNodeCancelsPendingMutation()
    {
        var provider = new RecordingMutationProvider(new GraphMutationResult(0, 0, true));
        var context = new SocialContext(provider);
        var person = new Person("person-1", "Ada");

        var entry = context.People.Add(person);
        context.People.Remove(person);
        var result = await context.SaveChangesAsync();

        Assert.Equal(GraphEntryState.Detached, entry.State);
        Assert.Empty(result.Changes.Operations);
        Assert.Null(provider.Executor.Plan);
    }

    [Fact]
    public async Task ConnectingThenDisconnectingSameRelationCancelsPendingMutation()
    {
        var provider = new RecordingMutationProvider(new GraphMutationResult(0, 0, true));
        var context = new SocialContext(provider);
        var source = new Person("person-1", "Ada");
        var target = new Person("person-2", "Alan");
        var relation = new Knows(2020);

        var entry = context.Friendships.Connect(source, relation, target);
        context.Friendships.Disconnect(source, relation, target);
        var result = await context.SaveChangesAsync();

        Assert.Equal(GraphEntryState.Detached, entry.State);
        Assert.Empty(result.Changes.Operations);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task ExistingRelationCanBeReusedAndDeletedAfterItIsAccepted()
    {
        var provider = new RecordingMutationProvider(new GraphMutationResult(0, 1, true));
        var context = new SocialContext(provider);
        var source = new Person("person-1", "Ada");
        var target = new Person("person-2", "Alan");
        var relation = new Knows(2020);

        var first = context.Friendships.Connect(source, relation, target);
        var second = context.Friendships.Connect(source, relation, target);
        await context.SaveChangesAsync();
        context.Friendships.Disconnect(source, relation, target);
        await context.SaveChangesAsync();

        Assert.Same(first, second);
        Assert.Equal(GraphEntryState.Detached, first.State);
        Assert.IsType<DeleteRelationOperation>(Assert.Single(provider.Executor.Plan!.Operations));
    }

    [Fact]
    public void DifferentNodeInstancesWithSameIdentityAreRejected()
    {
        var context = new SocialContext(new RecordingMutationProvider(new GraphMutationResult(0, 0, true)));
        context.People.Add(new Person("person-1", "Ada"));

        var exception = Assert.Throws<InvalidOperationException>(
            () => context.People.Update(new Person("person-1", "Another Ada")));

        Assert.Contains("already tracked", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NullNodeKeyIsRejectedBeforeCreatingAnEntry()
    {
        var context = new SocialContext(new RecordingMutationProvider(new GraphMutationResult(0, 0, true)));

        var exception = Assert.Throws<InvalidOperationException>(
            () => context.People.Add(new Person(null!, "Ada")));

        Assert.Contains("cannot be null", exception.Message, StringComparison.Ordinal);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task ProviderFailurePreservesPendingEntryState()
    {
        var expected = new InvalidOperationException("Provider failure");
        var provider = new RecordingMutationProvider(expected);
        var context = new SocialContext(provider);
        var entry = context.People.Add(new Person("person-1", "Ada"));

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await context.SaveChangesAsync());

        Assert.Same(expected, actual);
        Assert.Equal(GraphEntryState.Added, entry.State);
        Assert.Single(context.ChangeTracker.Entries(GraphEntryState.Added));
    }

    [Fact]
    public async Task ProviderWithoutMutationSupportRejectsPendingWork()
    {
        var context = new SocialContext(new ReadOnlyProvider());
        var entry = context.People.Add(new Person("person-1", "Ada"));

        var exception = await Assert.ThrowsAsync<NotSupportedException>(
            async () => await context.SaveChangesAsync());

        Assert.Contains("does not support mutation", exception.Message, StringComparison.Ordinal);
        Assert.Equal(GraphEntryState.Added, entry.State);
    }

    [Fact]
    public async Task EmptyUnitOfWorkDoesNotRequireMutationProvider()
    {
        var context = new SocialContext(new ReadOnlyProvider());

        var result = await context.SaveChangesAsync();

        Assert.Equal(0, result.AffectedNodes);
        Assert.Equal(0, result.AffectedRelations);
        Assert.True(result.IsAtomic);
        Assert.Empty(result.Changes.Operations);
    }

    [Fact]
    public async Task CancellationBeforePlanningLeavesEntriesPending()
    {
        var provider = new RecordingMutationProvider(new GraphMutationResult(1, 0, true));
        var context = new SocialContext(provider);
        var entry = context.People.Add(new Person("person-1", "Ada"));
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await context.SaveChangesAsync(source.Token));

        Assert.Equal(GraphEntryState.Added, entry.State);
        Assert.Null(provider.Executor.Plan);
    }

    [Fact]
    public void StandaloneGraphSetCannotPerformMutations()
    {
        var set = new GraphSet<Person>();

        var exception = Assert.Throws<InvalidOperationException>(
            () => set.Add(new Person("person-1", "Ada")));

        Assert.Contains("NodalContext", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DetectChangesCreatesPropertyLevelUpdateFromSnapshot()
    {
        var provider = new RecordingMutationProvider(new GraphMutationResult(1, 0, true));
        var context = new SocialContext(provider);
        var person = new MutablePerson { Id = "person-1", Name = "Ada", Age = 30 };
        var entry = context.MutablePeople.Attach(person);
        person.Name = "Ada Lovelace";

        var result = await context.SaveChangesAsync();

        var update = Assert.IsType<UpdateNodeOperation>(Assert.Single(result.Changes.Operations));
        Assert.Equal("Ada Lovelace", Assert.Single(update.Properties).Value);
        Assert.Empty(entry.ModifiedProperties);
        Assert.Equal(GraphEntryState.Unchanged, entry.State);
    }

    [Fact]
    public async Task AutoDetectionCanBeDisabledAndEntriesCanBeDetached()
    {
        var context = new SocialContext(new RecordingMutationProvider(new GraphMutationResult(0, 0, true)));
        var person = new MutablePerson { Id = "person-1", Name = "Ada", Age = 30 };
        var entry = context.MutablePeople.Attach(person);
        person.Age = 31;
        context.ChangeTracker.AutoDetectChangesEnabled = false;

        var result = await context.SaveChangesAsync();
        context.ChangeTracker.DetectChanges();

        Assert.Empty(result.Changes.Operations);
        Assert.Equal(GraphEntryState.Modified, entry.State);
        Assert.Contains("Age", entry.ModifiedProperties);
        context.ChangeTracker.Detach(entry);
        Assert.Equal(GraphEntryState.Detached, entry.State);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task EntryCanMarkOneMappedPropertyForUpdate()
    {
        var provider = new RecordingMutationProvider(new GraphMutationResult(1, 0, true));
        var context = new SocialContext(provider);
        var person = new MutablePerson { Id = "person-1", Name = "Ada", Age = 30 };
        context.MutablePeople.Attach(person);
        context.ChangeTracker.AutoDetectChangesEnabled = false;

        var entry = context.Entry(person);
        var property = entry.Property(candidate => candidate.Name);
        Assert.False(property.IsModified);
        property.IsModified = true;
        Assert.True(property.IsModified);
        property.IsModified = false;
        Assert.Equal(GraphEntryState.Unchanged, entry.State);
        property.IsModified = true;
        var result = await context.SaveChangesAsync();

        var update = Assert.IsType<UpdateNodeOperation>(Assert.Single(result.Changes.Operations));
        Assert.Equal("display_name", Assert.Single(update.Properties).Key);
        Assert.Throws<NotSupportedException>(() => entry.Property(candidate => candidate.Name.Length));
        Assert.Throws<InvalidOperationException>(() => context.Entry(
            new MutablePerson { Id = "person-2", Name = "Alan", Age = 31 }));
    }

    [Fact]
    public async Task RelationshipPayloadChangesAreDetectedFromSnapshots()
    {
        var provider = new RecordingMutationProvider(new GraphMutationResult(0, 1, true));
        var context = new SocialContext(provider);
        var source = new Person("person-1", "Ada");
        var target = new Person("person-2", "Alan");
        var relation = new Knows(2020);
        context.Friendships.Connect(source, relation, target);
        await context.SaveChangesAsync();

        relation.SinceYear = 2026;
        var result = await context.SaveChangesAsync();

        var update = Assert.IsType<UpdateRelationOperation>(Assert.Single(result.Changes.Operations));
        Assert.Equal(2026, Assert.Single(update.Properties).Value);
    }

    private static void AssertCreateNode(GraphMutationOperation operation, string id, string name)
    {
        var create = Assert.IsType<CreateNodeOperation>(operation);
        Assert.Equal(id, create.Identity.Value);
        Assert.Equal(name, create.Properties["display_name"]);
    }

    private sealed class SocialContext(IGraphProvider provider) : NodalContext(provider)
    {
        public GraphSet<Person> People => Set<Person>();

        public GraphSet<MutablePerson> MutablePeople => Set<MutablePerson>();

        public RelationSet<Person, Knows, Person> Friendships => Relations<Person, Knows, Person>();
    }

    [GraphNode("Person")]
    private sealed record Person(
        [property: GraphKey]
        [property: GraphProperty("person_id")]
        string Id,
        [property: GraphProperty("display_name")]
        string Name);

    [GraphNode("MutablePerson")]
    private sealed class MutablePerson
    {
        [GraphKey]
        [GraphProperty("person_id")]
        public required string Id { get; init; }

        [GraphProperty("display_name")]
        public required string Name { get; set; }

        public int Age { get; set; }
    }

    [GraphRelation("KNOWS")]
    private sealed class Knows(int sinceYear)
    {
        [GraphProperty("since_year")]
        public int SinceYear { get; set; } = sinceYear;
    }

    private sealed class RecordingMutationProvider : ReadOnlyProvider, IGraphMutationProvider
    {
        public RecordingMutationProvider(GraphMutationResult result) => Executor = new RecordingMutationExecutor(result);

        public RecordingMutationProvider(Exception exception) => Executor = new RecordingMutationExecutor(exception);

        public RecordingMutationExecutor Executor { get; }

        public GraphProviderCapabilities Capabilities { get; } = new()
        {
            SupportsTransactions = true,
            SupportsAtomicBatch = true,
            TransactionScope = GraphTransactionScope.ClientManaged,
        };

        public IGraphMutationExecutor MutationExecutor => Executor;
    }

    private sealed class RecordingMutationExecutor
        : IGraphMutationExecutor
    {
        private readonly GraphMutationResult? result;
        private readonly Exception? exception;

        public RecordingMutationExecutor(GraphMutationResult result) => this.result = result;

        public RecordingMutationExecutor(Exception exception) => this.exception = exception;

        public GraphMutationPlan? Plan { get; private set; }

        public ValueTask<GraphMutationResult> ExecuteAsync(
            GraphMutationPlan plan,
            CancellationToken cancellationToken = default)
        {
            Plan = plan;
            return exception is null
                ? ValueTask.FromResult(result!)
                : ValueTask.FromException<GraphMutationResult>(exception);
        }
    }

    private class ReadOnlyProvider : IGraphProvider
    {
        public IGraphQueryCompiler QueryCompiler => throw new NotSupportedException();

        public IGraphCommandExecutor CommandExecutor => throw new NotSupportedException();

        public IGraphResultMaterializer ResultMaterializer => throw new NotSupportedException();
    }
}
