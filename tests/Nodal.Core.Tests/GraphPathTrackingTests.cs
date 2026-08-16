using Nodal.Core.ChangeTracking;
using Nodal.Core.Execution;
using Nodal.Core.Metadata;
using Nodal.Core.Mutations;
using Nodal.Core.Providers;
using Nodal.Core.Query;

namespace Nodal.Core.Tests;

public sealed class GraphPathTrackingTests
{
    [Fact]
    public async Task PathMaterializationUsesIdentityMapAndRelationshipCanBeUpdated()
    {
        var provider = new PathProvider();
        var context = new SocialContext(provider);

        var first = Assert.Single(await context.People.Query().TraversePath(context.Friendships).ToListAsync());
        var second = Assert.Single(await context.People.Query().TraversePath(context.Friendships).ToListAsync());
        var single = await context.People.Query().TraversePath(context.Friendships).SingleAsync();
        var relationOnly = Assert.Single(
            await context.People.Query().TraversePath(context.Friendships).ToRelationsAsync());

        Assert.Same(first.Source, second.Source);
        Assert.Same(first.Relation, second.Relation);
        Assert.Same(first.Relation, single.Relation);
        Assert.Same(first.Relation, relationOnly);
        Assert.Same(first.Target, second.Target);
        Assert.Equal(3, context.ChangeTracker.Entries().Count);
        Assert.All(context.ChangeTracker.Entries(), entry => Assert.Equal(GraphEntryState.Unchanged, entry.State));

        first.Relation.SinceYear = 2025;
        context.Friendships.Update(first.Source, first.Relation, first.Target);
        var saved = await context.SaveChangesAsync();

        var update = Assert.IsType<UpdateRelationOperation>(Assert.Single(provider.Mutations.Plan!.Operations));
        Assert.Equal(2025, update.Properties["SinceYear"]);
        Assert.Equal("edge-1", update.ProviderId);
        Assert.Equal(1, saved.AffectedRelations);
        Assert.All(context.ChangeTracker.Entries(), entry => Assert.Equal(GraphEntryState.Unchanged, entry.State));
    }

    private sealed class SocialContext(IGraphProvider provider) : NodalContext(provider)
    {
        public GraphSet<Person> People => Set<Person>();

        public RelationSet<Person, Knows, Person> Friendships => Relations<Person, Knows, Person>();
    }

    [GraphNode("Person")]
    private sealed record Person([property: GraphKey] string Id, string Name);

    [GraphRelation("KNOWS")]
    private sealed class Knows
    {
        public int SinceYear { get; set; }
    }

    private sealed class PathProvider : IGraphProvider, IGraphMutationProvider
    {
        public PathProvider()
        {
            QueryCompiler = new Compiler();
            CommandExecutor = new Executor();
            ResultMaterializer = new JsonGraphResultMaterializer();
            Mutations = new MutationExecutor();
        }

        public IGraphQueryCompiler QueryCompiler { get; }

        public IGraphCommandExecutor CommandExecutor { get; }

        public IGraphResultMaterializer ResultMaterializer { get; }

        public MutationExecutor Mutations { get; }

        IGraphMutationExecutor IGraphMutationProvider.MutationExecutor => Mutations;

        public GraphProviderCapabilities Capabilities { get; } = new()
        {
            SupportsTransactions = true,
            SupportsAtomicBatch = true,
            TransactionScope = GraphTransactionScope.ClientManaged,
        };
    }

    private sealed class Compiler : IGraphQueryCompiler
    {
        public GraphCommand Compile(GraphQueryModel query) => new("path", new Dictionary<string, object?>());
    }

    private sealed class Executor : IGraphCommandExecutor
    {
        public ValueTask<GraphQueryResult> ExecuteAsync(
            GraphCommand command,
            CancellationToken cancellationToken = default)
        {
            var source = new GraphNodeRecord(
                "Person",
                "person-1",
                new Dictionary<string, object?> { ["Id"] = "person-1", ["Name"] = "Ada" });
            var target = new GraphNodeRecord(
                "Person",
                "person-2",
                new Dictionary<string, object?> { ["Id"] = "person-2", ["Name"] = "Alan" });
            var relation = new GraphRelationRecord(
                "KNOWS",
                "edge-1",
                source.Id,
                target.Id,
                new Dictionary<string, object?> { ["SinceYear"] = 2020 });
            return ValueTask.FromResult(new GraphQueryResult(
                [source, target],
                [relation],
                [new GraphPathRecord(source, relation, target)]));
        }
    }

    private sealed class MutationExecutor : IGraphMutationExecutor
    {
        public GraphMutationPlan? Plan { get; private set; }

        public ValueTask<GraphMutationResult> ExecuteAsync(
            GraphMutationPlan plan,
            CancellationToken cancellationToken = default)
        {
            Plan = plan;
            return ValueTask.FromResult(new GraphMutationResult(0, 1, true));
        }
    }
}
