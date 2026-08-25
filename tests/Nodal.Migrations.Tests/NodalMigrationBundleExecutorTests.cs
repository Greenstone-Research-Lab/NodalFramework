using Nodal.Core.Migrations;

namespace Nodal.Migrations.Tests;

public sealed class NodalMigrationBundleExecutorTests
{
    [Fact]
    public async Task ApplyMapsUpCommandsAndIsIdempotent()
    {
        var provider = new RecordingProvider();
        var executor = Executor(provider);
        var bundle = Bundle();

        var applied = await executor.ApplyAsync(bundle);
        provider.History[bundle.MigrationId] = bundle.Checksum;
        var repeated = await executor.ApplyAsync(bundle);

        Assert.Equal(NodalMigrationBundleExecutionOutcome.Applied, applied.Outcome);
        Assert.Equal(NodalMigrationBundleExecutionOutcome.AlreadyApplied, repeated.Outcome);
        Assert.Equal(1, applied.CommandCount);
        var execution = Assert.Single(provider.Applied);
        var command = Assert.Single(execution.Commands);
        Assert.Equal("CREATE QUERY install_people", command.Text);
        Assert.Equal(MigrationCommandKind.QueryDefinition, command.Kind);
    }

    [Fact]
    public async Task ApplyDryRunDoesNotMutateProvider()
    {
        var provider = new RecordingProvider();

        var result = await Executor(provider).ApplyAsync(
            Bundle(),
            new NodalMigrationBundleExecutionOptions { DryRun = true });

        Assert.Equal(NodalMigrationBundleExecutionOutcome.ApplyPlanned, result.Outcome);
        Assert.Empty(provider.Applied);
        Assert.Empty(provider.Reverted);
    }

    [Fact]
    public async Task AppliedChecksumDriftFailsBeforeProviderMutation()
    {
        var provider = new RecordingProvider();
        provider.History[Bundle().MigrationId] = new string('0', 64);

        await Assert.ThrowsAsync<NodalMigrationBundleAppliedChecksumException>(async () =>
            await Executor(provider).ApplyAsync(Bundle()));

        Assert.Empty(provider.Applied);
    }

    [Fact]
    public async Task DestructiveApplyRequiresExplicitApproval()
    {
        var provider = new RecordingProvider();
        var bundle = Bundle(destructiveUp: true);

        await Assert.ThrowsAsync<NodalMigrationBundleApprovalRequiredException>(async () =>
            await Executor(provider).ApplyAsync(bundle));
        var result = await Executor(provider).ApplyAsync(
            bundle,
            new NodalMigrationBundleExecutionOptions { AllowDestructiveOperations = true });

        Assert.Equal(NodalMigrationBundleExecutionOutcome.Applied, result.Outcome);
        Assert.Single(provider.Applied);
    }

    [Fact]
    public async Task RevertUsesDownCommandsAndRequiresApproval()
    {
        var provider = new RecordingProvider();
        var bundle = Bundle();
        provider.History[bundle.MigrationId] = bundle.Checksum;

        await Assert.ThrowsAsync<NodalMigrationBundleApprovalRequiredException>(async () =>
            await Executor(provider).RevertAsync(bundle));
        var result = await Executor(provider).RevertAsync(
            bundle,
            new NodalMigrationBundleExecutionOptions { AllowDestructiveOperations = true });

        Assert.Equal(NodalMigrationBundleExecutionOutcome.Reverted, result.Outcome);
        var command = Assert.Single(Assert.Single(provider.Reverted).Commands);
        Assert.Equal("DROP QUERY install_people", command.Text);
        Assert.Equal(MigrationCommandKind.QueryDefinition, command.Kind);
    }

    [Fact]
    public async Task RevertIsIdempotentAndRejectsIrreversibleBundle()
    {
        var provider = new RecordingProvider();
        var bundle = Bundle(includeDown: false);

        var absent = await Executor(provider).RevertAsync(bundle);
        provider.History[bundle.MigrationId] = bundle.Checksum;

        Assert.Equal(NodalMigrationBundleExecutionOutcome.AlreadyReverted, absent.Outcome);
        await Assert.ThrowsAsync<NodalMigrationBundleIrreversibleException>(async () =>
            await Executor(provider).RevertAsync(
                bundle,
                new NodalMigrationBundleExecutionOptions { AllowDestructiveOperations = true }));
    }

    [Fact]
    public async Task RevertDryRunDoesNotMutateProvider()
    {
        var provider = new RecordingProvider();
        var bundle = Bundle();
        provider.History[bundle.MigrationId] = bundle.Checksum;

        var result = await Executor(provider).RevertAsync(
            bundle,
            new NodalMigrationBundleExecutionOptions
            {
                AllowDestructiveOperations = true,
                DryRun = true,
            });

        Assert.Equal(NodalMigrationBundleExecutionOutcome.RevertPlanned, result.Outcome);
        Assert.Empty(provider.Reverted);
    }

    [Theory]
    [InlineData("TigerGraph", "5.26", "SchemaWrite")]
    [InlineData("Neo4j", "5.27", "SchemaWrite")]
    [InlineData("Neo4j", "5.26", "OtherCapability")]
    public async Task TargetMismatchFailsBeforeProviderTransport(
        string providerName,
        string providerVersion,
        string capability)
    {
        var provider = new RecordingProvider();
        var executor = new NodalMigrationBundleExecutor(
            provider,
            new NodalMigrationBundleTarget(providerName, providerVersion, new HashSet<string> { capability }));

        await Assert.ThrowsAnyAsync<Exception>(async () => await executor.ApplyAsync(Bundle()));

        Assert.Equal(0, provider.HistoryReads);
        Assert.Empty(provider.Applied);
    }

    [Fact]
    public async Task UnsupportedRuntimeAndCancellationFailBeforeTransport()
    {
        var unsupported = new RecordingProvider { SupportsMigrationExecution = false };
        await Assert.ThrowsAsync<NodalCapabilityNotSupportedException>(async () =>
            await Executor(unsupported).ApplyAsync(Bundle()));
        Assert.Equal(0, unsupported.HistoryReads);

        var cancelled = new RecordingProvider();
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await Executor(cancelled).ApplyAsync(Bundle(), cancellationToken: source.Token));
        Assert.Equal(0, cancelled.HistoryReads);
    }

    [Fact]
    public async Task LockingProviderHoldsOneLeaseForCompleteExecution()
    {
        var provider = new LockingProvider();

        await Executor(provider).ApplyAsync(Bundle());

        Assert.Equal(["Neo4j/graph"], provider.Lock.Scopes);
        Assert.Equal(1, provider.Lock.ReleaseCount);
        Assert.False(provider.Lock.IsActive);
        Assert.True(provider.HistoryReadWhileLocked);
        Assert.True(provider.ApplyWhileLocked);
    }

    [Fact]
    public void InvalidTargetsAndManifestsAreRejected()
    {
        var provider = new RecordingProvider();
        Assert.Throws<ArgumentNullException>(() => new NodalMigrationBundleExecutor(null!, Target()));
        Assert.Throws<ArgumentNullException>(() => new NodalMigrationBundleExecutor(provider, null!));
        Assert.Throws<ArgumentException>(() => new NodalMigrationBundleExecutor(
            provider,
            new NodalMigrationBundleTarget("", "5.26", new HashSet<string>())));
        Assert.Throws<ArgumentException>(() => new NodalMigrationBundleExecutor(
            provider,
            new NodalMigrationBundleTarget("Neo4j", "5.26", new HashSet<string> { "" })));
        Assert.Throws<ArgumentException>(() => NodalMigrationBundleSerializer.Create(new(
            "down-only",
            "Neo4j",
            "5.26",
            "0.1.0-alpha.1",
            [],
            [
                new NodalMigrationBundleCommand(
                    "down",
                    "DROP INDEX people",
                    true,
                    true,
                    Direction: NodalMigrationBundleDirection.Down),
            ])));
    }

    private static NodalMigrationBundleExecutor Executor(IGraphMigrationProvider provider) =>
        new(provider, Target());

    private static NodalMigrationBundleTarget Target() =>
        new("Neo4j", "5.26", new HashSet<string>(StringComparer.Ordinal) { "SchemaWrite" });

    private static NodalMigrationBundle Bundle(bool destructiveUp = false, bool includeDown = true)
    {
        var commands = new List<NodalMigrationBundleCommand>
        {
            new(
                "create-query",
                "CREATE QUERY install_people",
                false,
                destructiveUp,
                MigrationCommandKind.QueryDefinition),
        };
        if (includeDown)
        {
            commands.Add(new NodalMigrationBundleCommand(
                "drop-query",
                "DROP QUERY install_people",
                false,
                true,
                MigrationCommandKind.QueryDefinition,
                NodalMigrationBundleDirection.Down));
        }

        return NodalMigrationBundleSerializer.Create(new NodalMigrationBundleManifest(
            "20260825_001_people",
            "Neo4j",
            "5.26",
            "0.1.0-alpha.1",
            ["SchemaWrite"],
            commands));
    }

    private class RecordingProvider : IGraphMigrationProvider, IGraphMigrationExecutor
    {
        public bool SupportsMigrationExecution { get; set; } = true;

        public IGraphMigrationDialect MigrationDialect => throw new NotSupportedException();

        public IGraphMigrationExecutor MigrationExecutor => this;

        public Dictionary<string, string> History { get; } = new(StringComparer.Ordinal);

        public List<MigrationExecution> Applied { get; } = [];

        public List<MigrationExecution> Reverted { get; } = [];

        public int HistoryReads { get; private set; }

        public virtual ValueTask<IReadOnlyDictionary<string, string>> GetAppliedMigrationsAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HistoryReads++;
            OnHistoryRead();
            return ValueTask.FromResult<IReadOnlyDictionary<string, string>>(History);
        }

        public virtual ValueTask ApplyAsync(
            MigrationExecution execution,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OnApply();
            Applied.Add(execution);
            return ValueTask.CompletedTask;
        }

        public ValueTask RevertAsync(
            MigrationExecution execution,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reverted.Add(execution);
            return ValueTask.CompletedTask;
        }

        protected virtual void OnHistoryRead()
        {
        }

        protected virtual void OnApply()
        {
        }
    }

    private sealed class LockingProvider : RecordingProvider, IGraphMigrationLockProvider
    {
        public RecordingLock Lock { get; } = new();

        public string MigrationLockScope => "Neo4j/graph";

        public IGraphMigrationLock MigrationLock => Lock;

        public bool HistoryReadWhileLocked { get; private set; }

        public bool ApplyWhileLocked { get; private set; }

        protected override void OnHistoryRead() => HistoryReadWhileLocked = Lock.IsActive;

        protected override void OnApply() => ApplyWhileLocked = Lock.IsActive;
    }

    private sealed class RecordingLock : IGraphMigrationLock
    {
        public List<string> Scopes { get; } = [];

        public int ReleaseCount { get; private set; }

        public bool IsActive { get; private set; }

        public ValueTask<IAsyncDisposable> AcquireAsync(
            string scope,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Scopes.Add(scope);
            IsActive = true;
            return ValueTask.FromResult<IAsyncDisposable>(new Lease(this));
        }

        private sealed class Lease(RecordingLock owner) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                owner.IsActive = false;
                owner.ReleaseCount++;
                return ValueTask.CompletedTask;
            }
        }
    }
}
