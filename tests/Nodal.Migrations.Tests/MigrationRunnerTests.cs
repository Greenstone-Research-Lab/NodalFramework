using Nodal.Core;
using Nodal.Core.Execution;
using Nodal.Core.Metadata;
using Nodal.Core.Migrations;
using Nodal.Core.Providers;

namespace Nodal.Migrations.Tests;

public sealed class MigrationRunnerTests
{
    [Fact]
    public async Task DryRunIsSideEffectFreeAndMigrateIsIdempotent()
    {
        var provider = new RecordingProvider();
        var runner = new MigrationRunner(provider);
        NodalMigration[] migrations = [new FirstMigration(), new SecondMigration()];

        var dryRun = await runner.PlanAsync(migrations);

        Assert.Equal(["001_first", "002_second"], dryRun.Executions.Select(execution => execution.Id));
        Assert.All(dryRun.Executions, execution => Assert.Equal(64, execution.Checksum.Length));
        Assert.Empty(provider.Executor.Applied);

        var applied = await runner.MigrateAsync(migrations);
        var repeated = await runner.MigrateAsync(migrations);

        Assert.Equal(2, applied.Executions.Count);
        Assert.True(repeated.IsEmpty);
        Assert.Equal(["001_first", "002_second"], provider.Executor.Applied);
    }

    [Fact]
    public async Task RevertRequiresAppliedAndReversibleMigration()
    {
        var provider = new RecordingProvider();
        var runner = new MigrationRunner(provider);
        var migration = new FirstMigration();

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await runner.RevertAsync(migration));
        await runner.MigrateAsync([migration]);
        var reverted = await runner.RevertAsync(migration);

        Assert.Equal("001_first", reverted.Id);
        Assert.Empty(provider.Executor.Applied);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await runner.RevertAsync(new IrreversibleMigration()));
    }

    [Fact]
    public async Task DuplicateMigrationIdentifiersAreRejected()
    {
        var runner = new MigrationRunner(new RecordingProvider());

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await runner.PlanAsync([new FirstMigration(), new DuplicateMigration()]));
    }

    [Fact]
    public async Task ChangedAppliedMigrationIsRejectedAsSchemaDrift()
    {
        var provider = new RecordingProvider();
        var runner = new MigrationRunner(provider);
        await runner.MigrateAsync([new FirstMigration()]);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await runner.PlanAsync([new ChangedFirstMigration()]));
    }

    [Fact]
    public async Task ProviderNoOpCommandsStillDetectChangedSchemaOperations()
    {
        var provider = new RecordingProvider(new NoOpDialect());
        var runner = new MigrationRunner(provider);
        await runner.MigrateAsync([new FirstMigration()]);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await runner.PlanAsync([new ChangedFirstMigration()]));
    }

    [Fact]
    public async Task MigrationUsesProviderLockAndReleasesLease()
    {
        var provider = new LockingProvider();
        var runner = new MigrationRunner(provider);

        var plan = await runner.MigrateAsync([new FirstMigration()]);

        Assert.Single(plan.Executions);
        Assert.Equal(
            ["neo4j://localhost:7687/database/neo4j"],
            provider.Lock.AcquiredScopes);
        Assert.Equal(1, provider.Lock.AcquisitionCount);
        Assert.Equal(1, provider.Lock.ReleaseCount);
        Assert.Equal(0, provider.Lock.ActiveLeaseCount);
    }

    [Fact]
    public async Task ContextDatabaseFacadeExposesDryRunAndExecutionExtensions()
    {
        var provider = new RecordingProvider();
        var context = new EmptyContext(provider);

        var dryRun = await context.Database.PlanMigrationsAsync([new FirstMigration()]);
        var applied = await context.Database.MigrateAsync([new FirstMigration()]);
        var reverted = await context.Database.RevertMigrationAsync(new FirstMigration());

        Assert.True(context.Database.SupportsMigrations);
        Assert.Single(dryRun.Executions);
        Assert.Single(applied.Executions);
        Assert.Equal("001_first", reverted.Id);
    }

    [Fact]
    public void BuilderUsesPortableAttributesAndRejectsInvalidPropertyExpressions()
    {
        var builder = new MigrationBuilder()
            .CreateNode<Person>()
            .CreateRelation<Knows, Person, Person>(directed: false)
            .CreateIndex<Person, string>(person => person.Email)
            .DropNode<Person>()
            .DropRelation<Knows>()
            .DropSchemaObject("nodal_ix_Person_email", MigrationSchemaObjectKind.Index);

        var node = Assert.IsType<CreateNodeTypeOperation>(builder.Operations[0]);
        Assert.Equal("people", node.NodeType);
        Assert.Equal("person_id", node.KeyProperty);
        Assert.Contains(node.Properties!, property => property.Name == "email_address");
        var relation = Assert.IsType<CreateRelationTypeOperation>(builder.Operations[1]);
        Assert.Equal("KNOWS", relation.RelationType);
        Assert.False(relation.Directed);
        Assert.Throws<ArgumentException>(() =>
            new MigrationBuilder().CreateIndex<Person, int>(person => person.Email.Length));
    }

    private sealed class EmptyContext(IGraphProvider provider) : NodalContext(provider);

    private sealed class LockingProvider :
    IGraphMigrationProvider,
    IGraphMigrationLockProvider,
    IGraphProvider
    {
        public LockingProvider()
        {
            MigrationExecutor = new RecordingExecutor();
            MigrationDialect = new RecordingDialect();
            Lock = new RecordingMigrationLock();
        }

        public bool SupportsMigrationExecution => true;

        public IGraphMigrationDialect MigrationDialect { get; }

        public IGraphMigrationExecutor MigrationExecutor { get; }

        public IGraphMigrationLock MigrationLock => Lock;

        public string MigrationLockScope =>
            "neo4j://localhost:7687/database/neo4j";

        public RecordingMigrationLock Lock { get; }

        public IGraphQueryCompiler QueryCompiler =>
            throw new NotSupportedException();

        public IGraphCommandExecutor CommandExecutor =>
            throw new NotSupportedException();

        public IGraphResultMaterializer ResultMaterializer =>
            throw new NotSupportedException();
    }

    private sealed class RecordingMigrationLock : IGraphMigrationLock
    {
        public List<string> AcquiredScopes { get; } = [];

        public int AcquisitionCount { get; private set; }

        public int ReleaseCount { get; private set; }

        public int ActiveLeaseCount { get; private set; }

        public ValueTask<IAsyncDisposable> AcquireAsync(
            string scope,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            AcquiredScopes.Add(scope);
            AcquisitionCount++;
            ActiveLeaseCount++;

            return ValueTask.FromResult<IAsyncDisposable>(
                new Lease(this));
        }

        private sealed class Lease(RecordingMigrationLock owner) : IAsyncDisposable
        {
            private int disposed;

            public ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref disposed, 1) == 0)
                {
                    owner.ActiveLeaseCount--;
                    owner.ReleaseCount++;
                }

                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class RecordingProvider : IGraphMigrationProvider, IGraphProvider
    {
        public bool SupportsMigrationExecution => true;

        public IGraphMigrationDialect MigrationDialect { get; }

        public RecordingExecutor Executor { get; } = new();

        public RecordingProvider(IGraphMigrationDialect? dialect = null)
        {
            MigrationDialect = dialect ?? new RecordingDialect();
        }

        IGraphMigrationExecutor IGraphMigrationProvider.MigrationExecutor => Executor;

        public IGraphQueryCompiler QueryCompiler => throw new NotSupportedException();

        public IGraphCommandExecutor CommandExecutor => throw new NotSupportedException();

        public IGraphResultMaterializer ResultMaterializer => throw new NotSupportedException();
    }

    private sealed class RecordingDialect : IGraphMigrationDialect
    {
        public IReadOnlyList<MigrationCommand> Compile(IReadOnlyList<MigrationOperation> operations) =>
            operations.Select(operation => new MigrationCommand(operation.GetType().Name, true)).ToArray();
    }

    private sealed class NoOpDialect : IGraphMigrationDialect
    {
        public IReadOnlyList<MigrationCommand> Compile(IReadOnlyList<MigrationOperation> operations) => [];
    }

    private sealed class RecordingExecutor : IGraphMigrationExecutor
    {
        private readonly Dictionary<string, string> applied = new(StringComparer.Ordinal);

        public IReadOnlyList<string> Applied => applied.Keys.Order(StringComparer.Ordinal).ToArray();

        public ValueTask<IReadOnlyDictionary<string, string>> GetAppliedMigrationsAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyDictionary<string, string>>(applied);

        public ValueTask ApplyAsync(MigrationExecution execution, CancellationToken cancellationToken = default)
        {
            applied.Add(execution.Id, execution.Checksum);
            return ValueTask.CompletedTask;
        }

        public ValueTask RevertAsync(MigrationExecution execution, CancellationToken cancellationToken = default)
        {
            applied.Remove(execution.Id);
            return ValueTask.CompletedTask;
        }
    }

    private class FirstMigration : NodalMigration
    {
        public override string Id => "001_first";

        protected override void Up(MigrationBuilder migration) => migration.CreateNode<Person>();

        protected override void Down(MigrationBuilder migration) => migration.DropNode<Person>();
    }

    private sealed class SecondMigration : NodalMigration
    {
        public override string Id => "002_second";

        protected override void Up(MigrationBuilder migration) =>
            migration.CreateIndex<Person, string>(person => person.Email);

        protected override void Down(MigrationBuilder migration) =>
            migration.DropSchemaObject("nodal_ix_people_email_address", MigrationSchemaObjectKind.Index);
    }

    private sealed class DuplicateMigration : FirstMigration;

    private sealed class ChangedFirstMigration : FirstMigration
    {
        protected override void Up(MigrationBuilder migration) =>
            migration.CreateNode<Person>().CreateIndex<Person, string>(person => person.Email);
    }

    private sealed class IrreversibleMigration : NodalMigration
    {
        public override bool IsReversible => false;

        protected override void Up(MigrationBuilder migration)
        {
        }

        protected override void Down(MigrationBuilder migration)
        {
        }
    }

    [GraphNode("people")]
    private sealed record Person(
        [property: GraphKey, GraphProperty("person_id")] string Id,
        [property: GraphProperty("email_address")] string Email);

    [GraphRelation("KNOWS")]
    private sealed record Knows(DateTime Since);
}
