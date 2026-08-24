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
    public async Task UnsupportedMigrationFailsBeforeProviderApply()
    {
        var provider = new RecordingProvider(
            new RejectingDialect());

        var runner = new MigrationRunner(provider);

        var exception = await Assert.ThrowsAsync<
            NodalCapabilityNotSupportedException>(
                async () => await runner.MigrateAsync(
                [
                    new FirstMigration()
                ]));

        Assert.Contains(
            "NODAL-MIGRATION-UNSUPPORTED",
            exception.Message);
        Assert.Equal("RejectingDialect", exception.ProviderName);
        Assert.Equal(
            "NODAL-MIGRATION-UNSUPPORTED",
            exception.CapabilityCode);

        Assert.Empty(provider.Executor.Applied);
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

    [Fact]
    public void BuilderCreatesPropertyEvolutionOperations()
    {
        var builder = new MigrationBuilder()
            .AddNodeProperty<Person, string>(person => person.Email)
            .AddRelationProperty<Knows, DateTime>(relation => relation.Since)
            .DropNodeProperty<Person, string>(person => person.Email)
            .DropRelationProperty<Knows, DateTime>(relation => relation.Since);

        Assert.IsType<AddNodePropertyOperation>(builder.Operations[0]);
        Assert.IsType<AddRelationPropertyOperation>(builder.Operations[1]);
        Assert.IsType<DropNodePropertyOperation>(builder.Operations[2]);
        Assert.IsType<DropRelationPropertyOperation>(builder.Operations[3]);
    }

    [Fact]
    public void BuilderCreatesIndexesConstraintsRenamesAndTypeChanges()
    {
        var builder = new MigrationBuilder()
            .CreateUniqueConstraint<Person, string>(person => person.Email)
            .DropIndex<Person, string>(person => person.Email)
            .DropUniqueConstraint<Person, string>(person => person.Email)
            .RenameNodeProperty<Person, string>(person => person.Email, "email")
            .RenameRelationProperty<Knows, DateTime>(relation => relation.Since, "connected_at")
            .AlterNodePropertyType<Person, string, Uri>(
                person => person.Email,
                MigrationPropertyTypeCompatibility.RequiresRewrite)
            .AlterRelationPropertyType<Knows, DateTime, long>(
                relation => relation.Since,
                MigrationPropertyTypeCompatibility.Destructive);

        Assert.Collection(
            builder.Operations,
            operation => Assert.IsType<CreateUniqueConstraintOperation>(operation),
            operation => Assert.IsType<DropIndexOperation>(operation),
            operation => Assert.IsType<DropUniqueConstraintOperation>(operation),
            operation => Assert.IsType<RenameNodePropertyOperation>(operation),
            operation => Assert.IsType<RenameRelationPropertyOperation>(operation),
            operation => Assert.IsType<AlterNodePropertyTypeOperation>(operation),
            operation => Assert.IsType<AlterRelationPropertyTypeOperation>(operation));
    }

    [Fact]
    public async Task BackfillExecutorHonorsBoundedBatchesAndContinuation()
    {
        var executor = new BoundedMigrationBackfillExecutor();
        var tokens = new List<string?>();

        await executor.ExecuteAsync(
            new MigrationBackfillRequest("email-normalization", 2),
            (context, _) =>
            {
                tokens.Add(context.ContinuationToken);
                return ValueTask.FromResult(
                    tokens.Count == 1
                        ? new MigrationBackfillBatchResult(2, "page-2", false)
                        : new MigrationBackfillBatchResult(1, null, true));
            });

        Assert.Equal([null, "page-2"], tokens);
    }

    [Fact]
    public async Task BackfillExecutorRejectsInvalidBatchResult()
    {
        var executor = new BoundedMigrationBackfillExecutor();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await executor.ExecuteAsync(
                new MigrationBackfillRequest("invalid", 2),
                (_, _) => ValueTask.FromResult(
                new MigrationBackfillBatchResult(3, null, true))));
    }

    [Fact]
    public async Task BackfillExecutorRequiresContinuationForIncompleteBatch()
    {
        var executor = new BoundedMigrationBackfillExecutor();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await executor.ExecuteAsync(
                new MigrationBackfillRequest("invalid-continuation", 2),
                (_, _) => ValueTask.FromResult(
                    new MigrationBackfillBatchResult(1, " ", false))));
    }

    [Fact]
    public async Task BackfillExecutorHonorsCancellationBeforeFirstBatch()
    {
        var executor = new BoundedMigrationBackfillExecutor();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await executor.ExecuteAsync(
                new MigrationBackfillRequest("cancelled", 2),
                (_, _) => ValueTask.FromResult(
                    new MigrationBackfillBatchResult(0, null, true)),
                cancellation.Token));
    }

    [Fact]
    public async Task BackfillExecutorRejectsNullContracts()
    {
        var executor = new BoundedMigrationBackfillExecutor();
        var request = new MigrationBackfillRequest("null-check", 1);
        static ValueTask<MigrationBackfillBatchResult> Batch(
            MigrationBackfillContext _, CancellationToken __) =>
            ValueTask.FromResult(new MigrationBackfillBatchResult(0, null, true));

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await executor.ExecuteAsync(null!, Batch));
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await executor.ExecuteAsync(request, null!));
    }

    [Fact]
    public async Task CleanupOperationCannotPrecedeSchemaEvolution()
    {
        var provider = new RecordingProvider();
        var runner = new MigrationRunner(provider);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await runner.PlanAsync(
            [
                new MisorderedMigration()
            ]));
    }

    [Fact]
    public async Task EmptyAndOutOfOrderMigrationIdentifiersAreRejected()
    {
        var runner = new MigrationRunner(
            new RecordingProvider());

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await runner.PlanAsync(
            [
                new EmptyIdentifierMigration()
            ]));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await runner.PlanAsync(
            [
                new SecondMigration(),
            new FirstMigration()
            ]));
    }

    [Fact]
    public async Task DestructiveMigrationRequiresExplicitApproval()
    {
        var provider = new RecordingProvider();
        var runner = new MigrationRunner(provider);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await runner.MigrateAsync(
            [
                new DestructiveMigration()
            ]));

        Assert.Empty(provider.Executor.Applied);
    }

    [Fact]
    public async Task DestructiveMigrationRunsWhenExplicitlyApproved()
    {
        var provider = new RecordingProvider();
        var runner = new MigrationRunner(provider);

        var result = await runner.MigrateAsync(
            [
                new DestructiveMigration()
            ],
            new MigrationExecutionOptions
            {
                AllowDestructiveOperations = true
            });

        Assert.Single(result.Executions);
        Assert.Equal(
            ["003_destructive"],
            provider.Executor.Applied);
    }

    [Fact]
    public async Task DryRunExposesPreflightFindings()
    {
        var provider = new RecordingProvider();
        var runner = new MigrationRunner(provider);

        var plan = await runner.PlanAsync(
        [
            new DestructiveMigration()
        ]);

        Assert.True(
            plan.Preflight.ContainsKey("003_destructive"));

        var result = plan.Preflight["003_destructive"];

        Assert.True(result.RequiresApproval);
        Assert.Contains(
            result.Issues,
            issue => issue.Kind is MigrationPreflightKind.Destructive);
        Assert.Contains(
            result.Issues,
            issue => issue.Code == "NODAL-MIGRATION-SUPPORTED");
    }

    [Fact]
    public async Task ProviderNoOpPlanReportsNativeSchemaWarning()
    {
        var provider = new RecordingProvider(
            new NoOpDialect());

        var runner = new MigrationRunner(provider);

        var plan = await runner.PlanAsync(
        [
            new FirstMigration()
        ]);

        var result = plan.Preflight["001_first"];

        Assert.True(result.IsValid);
        Assert.True(result.HasWarnings);
        Assert.Contains(
            result.Issues,
            issue => issue.Kind is MigrationPreflightKind.Warning);
        Assert.Contains(
            result.Issues,
            issue => issue.Code == "NODAL-MIGRATION-NATIVE-SCHEMA");
    }

    [Fact]
    public async Task PlanCanonicalizesCompletePropertyEvolution()
    {
        var plan = await new MigrationRunner(new RecordingProvider())
            .PlanAsync([new CompleteEvolutionMigration()]);

        var execution = Assert.Single(plan.Executions);
        Assert.Equal(64, execution.Checksum.Length);
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


    private sealed class EmptyIdentifierMigration : NodalMigration
    {
        public override string Id => string.Empty;

        protected override void Up(MigrationBuilder migration)
        {
        }

        protected override void Down(MigrationBuilder migration)
        {
        }
    }

    private sealed class RejectingDialect : IGraphMigrationDialect
    {
        public IReadOnlyList<MigrationCommand> Compile(
            IReadOnlyList<MigrationOperation> operations)
        {
            throw new NotSupportedException(
                "The provider does not support this migration operation.");
        }
    }

    private sealed class DestructiveMigration : NodalMigration
    {
        public override string Id => "003_destructive";

        protected override void Up(MigrationBuilder migration)
        {
            migration.DropNode<Person>();
        }

        protected override void Down(MigrationBuilder migration)
        {
            migration.CreateNode<Person>();
        }
    }

    private sealed class CompleteEvolutionMigration : NodalMigration
    {
        public override string Id => "005_complete_evolution";

        protected override void Up(MigrationBuilder migration)
        {
            migration
                .CreateUniqueConstraint<Person, string>(person => person.Email)
                .CreateIndex<Person, string>(person => person.Email)
                .AddNodeProperty<Person, string>(person => person.Email)
                .AddRelationProperty<Knows, DateTime>(relation => relation.Since)
                .RenameNodeProperty<Person, string>(person => person.Email, "email_address")
                .RenameRelationProperty<Knows, DateTime>(relation => relation.Since, "connected_at")
                .AlterNodePropertyType<Person, string, Uri>(
                    person => person.Email,
                    MigrationPropertyTypeCompatibility.RequiresRewrite)
                .AlterRelationPropertyType<Knows, DateTime, long>(
                    relation => relation.Since,
                    MigrationPropertyTypeCompatibility.Destructive);
            migration
                .DropIndex<Person, string>(person => person.Email)
                .DropUniqueConstraint<Person, string>(person => person.Email)
                .DropNodeProperty<Person, string>(person => person.Email)
                .DropRelationProperty<Knows, DateTime>(relation => relation.Since);
        }

        protected override void Down(MigrationBuilder migration)
        {
        }
    }

    private sealed class MisorderedMigration : NodalMigration
    {
        public override string Id => "004_misordered";

        protected override void Up(MigrationBuilder migration)
        {
            migration.DropNode<Person>();
            migration.CreateIndex<Person, string>(person => person.Email);
        }

        protected override void Down(MigrationBuilder migration)
        {
        }
    }
}
