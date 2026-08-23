using Nodal.Core;
using Nodal.Core.Execution;
using Nodal.Core.Migrations;
using Nodal.Core.Providers;
using Nodal.Migrations;

namespace Nodal.Migrations.Tests;

public sealed class MigrationHistoryStateTests
{
    [Fact]
    public async Task SuccessfulMigrationIsRecordedAsApplied()
    {
        var provider = new StatefulRecordingProvider();
        var runner = new MigrationRunner(provider);

        var plan = await runner.MigrateAsync([new StatefulMigration()]);

        Assert.Single(plan.Executions);

        var entry = Assert.Single(provider.History.Entries.Values);

        Assert.Equal("stateful_migration", entry.Id);
        Assert.Equal(MigrationExecutionState.Applied, entry.State);
        Assert.NotNull(entry.StartedAt);
        Assert.NotNull(entry.CompletedAt);
        Assert.Null(entry.Failure);
    }

    [Fact]
    public async Task FailedMigrationIsRecordedAsFailed()
    {
        var provider = new StatefulRecordingProvider(failApply: true);
        var runner = new MigrationRunner(provider);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await runner.MigrateAsync([new StatefulMigration()]));

        var entry = Assert.Single(provider.History.Entries.Values);

        Assert.Equal("stateful_migration", entry.Id);
        Assert.Equal(MigrationExecutionState.Failed, entry.State);
        Assert.NotNull(entry.StartedAt);
        Assert.NotNull(entry.CompletedAt);
        Assert.NotNull(entry.Failure);
        Assert.Equal(
            typeof(InvalidOperationException).FullName,
            entry.Failure.ErrorType);
    }

    private sealed class StatefulRecordingProvider :
        IGraphProvider,
        IGraphMigrationProvider,
        IGraphMigrationHistoryProvider
    {
        public StatefulRecordingProvider(bool failApply = false)
        {
            MigrationDialect = new StatefulRecordingDialect();
            MigrationExecutor = new StatefulRecordingExecutor(failApply);

            History = new StatefulRecordingHistoryStore();
            MigrationHistory = History;
        }

        public bool SupportsMigrationExecution => true;

        public IGraphMigrationDialect MigrationDialect { get; }

        public IGraphMigrationExecutor MigrationExecutor { get; }

        public IGraphMigrationHistoryStore MigrationHistory { get; }

        public string MigrationHistoryScope =>
            "recording://migration-history-tests";

        public StatefulRecordingHistoryStore History { get; }

        public IGraphQueryCompiler QueryCompiler =>
            throw new NotSupportedException();

        public IGraphCommandExecutor CommandExecutor =>
            throw new NotSupportedException();

        public IGraphResultMaterializer ResultMaterializer =>
            throw new NotSupportedException();
    }

    private sealed class StatefulRecordingDialect : IGraphMigrationDialect
    {
        public IReadOnlyList<MigrationCommand> Compile(
            IReadOnlyList<MigrationOperation> operations) =>
            [];
    }

    private sealed class StatefulRecordingExecutor(bool failApply) :
        IGraphMigrationExecutor
    {
        private readonly Dictionary<string, string> applied =
            new(StringComparer.Ordinal);

        public ValueTask<IReadOnlyDictionary<string, string>>
            GetAppliedMigrationsAsync(
                CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyDictionary<string, string>>(applied);

        public ValueTask ApplyAsync(
            MigrationExecution execution,
            CancellationToken cancellationToken = default)
        {
            if (failApply)
            {
                throw new InvalidOperationException(
                    "Recording migration execution failed.");
            }

            applied[execution.Id] = execution.Checksum;
            return ValueTask.CompletedTask;
        }

        public ValueTask RevertAsync(
            MigrationExecution execution,
            CancellationToken cancellationToken = default)
        {
            applied.Remove(execution.Id);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StatefulRecordingHistoryStore :
        IGraphMigrationHistoryStore
    {
        public Dictionary<string, MigrationHistoryEntry> Entries { get; } =
            new(StringComparer.Ordinal);

        public ValueTask<IReadOnlyDictionary<string, MigrationHistoryEntry>>
            GetMigrationHistoryAsync(
                CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<
                IReadOnlyDictionary<string, MigrationHistoryEntry>>(Entries);

        public ValueTask SaveMigrationHistoryAsync(
            MigrationHistoryEntry entry,
            CancellationToken cancellationToken = default)
        {
            Entries[entry.Id] = entry;
            return ValueTask.CompletedTask;
        }

        public ValueTask RemoveMigrationHistoryAsync(
            string migrationId,
            CancellationToken cancellationToken = default)
        {
            Entries.Remove(migrationId);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StatefulMigration : NodalMigration
    {
        public override string Id => "stateful_migration";

        protected override void Up(MigrationBuilder migration)
        {
        }

        protected override void Down(MigrationBuilder migration)
        {
        }
    }
}
