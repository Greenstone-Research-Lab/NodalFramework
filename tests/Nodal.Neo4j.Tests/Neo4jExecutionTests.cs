using System.Globalization;
using Neo4j.Driver;
using Nodal.Core.Analytics;
using Nodal.Core.ChangeTracking;
using Nodal.Core.Migrations;
using Nodal.Core.Mutations;
using Nodal.Core.Providers;
using NSubstitute;

namespace Nodal.Neo4j.Tests;

public sealed class Neo4jExecutionTests
{
    [Fact]
    public async Task CommandExecutorRunsParameterizedReadAndNormalizesGraphValues()
    {
        var driver = Substitute.For<IDriver>();
        var session = Substitute.For<IAsyncSession>();
        var runner = Substitute.For<IAsyncQueryRunner>();
        var cursor = Substitute.For<IResultCursor>();
        var source = Node("source", "Person", ("Name", "Ada"));
        var target = Node("target", "Person", ("Name", "Alan"));
        var nested = Node("nested", string.Empty, ("Name", "Grace"));
        var relation = Relationship("relation", "KNOWS", "source", "target", ("Since", 2020));
        var record = Substitute.For<IRecord>();
        record.Values.Returns(new Dictionary<string, object>
        {
            ["source"] = source,
            ["relation"] = relation,
            ["target"] = target,
            ["nested"] = new Dictionary<string, object> { ["items"] = new object[] { nested } },
            ["count"] = 2L,
        });
        cursor.GetAsyncEnumerator(Arg.Any<CancellationToken>())
            .Returns(_ => new TestAsyncEnumerator<IRecord>([record]));
        runner.RunAsync(Arg.Any<string>(), Arg.Any<IDictionary<string, object>>()).Returns(cursor);
        driver.AsyncSession(Arg.Any<Action<SessionConfigBuilder>>()).Returns(session);
        session.ExecuteReadAsync(
                Arg.Any<Func<IAsyncQueryRunner, Task<List<IRecord>>>>(),
                Arg.Any<Action<TransactionConfigBuilder>>())
            .Returns(call => call.ArgAt<Func<IAsyncQueryRunner, Task<List<IRecord>>>>(0)(runner));
        var executor = new Neo4jCommandExecutor(driver, "neo4j");
        var command = new GraphCommand(
            "MATCH (n) RETURN n",
            new Dictionary<string, object?> { ["id"] = "source", ["optional"] = null });

        var result = await executor.ExecuteAsync(command);

        Assert.Equal(3, result.Nodes.Count);
        Assert.Single(result.RelationRecords);
        Assert.Single(result.PathRecords);
        Assert.Equal(2L, result.ScalarValues["count"]);
        Assert.Single(result.ResultRows);
        Assert.Equal(2L, result.ResultRows[0].Values["count"]);
        Assert.Equal(string.Empty, result.Nodes.Single(node => Equals(node.Id, "nested")).Type);
        await session.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task CommandExecutorValidatesInputAndCancellation()
    {
        var driver = Substitute.For<IDriver>();
        var executor = new Neo4jCommandExecutor(driver);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await executor.ExecuteAsync(null!));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await executor.ExecuteAsync(
                new GraphCommand("RETURN 1", new Dictionary<string, object?>()),
                cancellation.Token));
        Assert.Throws<ArgumentNullException>(() => new Neo4jCommandExecutor(null!));
    }

    [Fact]
    public async Task MutationExecutorRunsWholePlanInOneWriteTransaction()
    {
        var driver = Substitute.For<IDriver>();
        var session = Substitute.For<IAsyncSession>();
        var runner = Substitute.For<IAsyncQueryRunner>();
        var cursor = Substitute.For<IResultCursor>();
        runner.RunAsync(Arg.Any<string>(), Arg.Any<IDictionary<string, object>>()).Returns(cursor);
        cursor.ConsumeAsync().Returns(Substitute.For<IResultSummary>());
        driver.AsyncSession(Arg.Any<Action<SessionConfigBuilder>>()).Returns(session);
        session.ExecuteWriteAsync(
                Arg.Any<Func<IAsyncQueryRunner, Task>>(),
                Arg.Any<Action<TransactionConfigBuilder>>())
            .Returns(call => call.ArgAt<Func<IAsyncQueryRunner, Task>>(0)(runner));
        var source = Identity("Person", "person-1");
        var target = Identity("Person", "person-2");
        var plan = new GraphMutationPlan(
        [
            new CreateNodeOperation(source, Properties(("Name", "Ada"))),
            new UpdateNodeOperation(target, Properties(("Name", "Alan"))),
            new CreateRelationOperation(source, "KNOWS", target, true, Properties(("Since", 2020))),
            new DeleteRelationOperation(source, "KNOWS", target, true),
            new DeleteNodeOperation(target),
        ]);

        var result = await new Neo4jMutationExecutor(driver, "neo4j").ExecuteAsync(plan);

        Assert.Equal(3, result.AffectedNodes);
        Assert.Equal(2, result.AffectedRelations);
        Assert.True(result.IsAtomic);
        await runner.Received(5).RunAsync(Arg.Any<string>(), Arg.Any<IDictionary<string, object>>());
        await cursor.Received(5).ConsumeAsync();
    }

    [Fact]
    public async Task MutationExecutorValidatesInputAndCancellation()
    {
        var driver = Substitute.For<IDriver>();
        var executor = new Neo4jMutationExecutor(driver);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await executor.ExecuteAsync(null!));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await executor.ExecuteAsync(new GraphMutationPlan([]), cancellation.Token));
        Assert.Throws<ArgumentNullException>(() => new Neo4jMutationExecutor(null!));
    }

    [Fact]
    public async Task MigrationExecutorReadsHistoryAndAppliesAndRevertsTransactionally()
    {
        var driver = Substitute.For<IDriver>();
        var session = Substitute.For<IAsyncSession>();
        var runner = Substitute.For<IAsyncQueryRunner>();
        var cursor = Substitute.For<IResultCursor>();
        var historyRecord = Substitute.For<IRecord>();
        historyRecord["id"].Returns("001_initial");
        historyRecord["checksum"].Returns("abc");
        cursor.GetAsyncEnumerator(Arg.Any<CancellationToken>())
            .Returns(_ => new TestAsyncEnumerator<IRecord>([historyRecord]));
        runner.RunAsync(Arg.Any<string>()).Returns(cursor);
        runner.RunAsync(Arg.Any<string>(), Arg.Any<IDictionary<string, object>>()).Returns(cursor);
        cursor.ConsumeAsync().Returns(Substitute.For<IResultSummary>());
        driver.AsyncSession(Arg.Any<Action<SessionConfigBuilder>>()).Returns(session);
        session.ExecuteReadAsync(
                Arg.Any<Func<IAsyncQueryRunner, Task<Dictionary<string, string>>>>(),
                Arg.Any<Action<TransactionConfigBuilder>>())
            .Returns(call => call.ArgAt<Func<IAsyncQueryRunner, Task<Dictionary<string, string>>>>(0)(runner));
        session.ExecuteWriteAsync(
                Arg.Any<Func<IAsyncQueryRunner, Task>>(),
                Arg.Any<Action<TransactionConfigBuilder>>())
            .Returns(call => call.ArgAt<Func<IAsyncQueryRunner, Task>>(0)(runner));
        var executor = new Neo4jMigrationExecutor(driver, "neo4j");
        var execution = new MigrationExecution(
            "001_initial",
            "abc",
            [new MigrationCommand("CREATE INDEX sample", true)]);

        var applied = await executor.GetAppliedMigrationsAsync();
        await executor.ApplyAsync(execution);
        await executor.RevertAsync(execution);

        Assert.Equal("abc", applied["001_initial"]);
        await runner.Received().RunAsync(
            Arg.Is<string>(text => text.StartsWith("MERGE", StringComparison.Ordinal)),
            Arg.Any<IDictionary<string, object>>());
        await runner.Received().RunAsync(
            Arg.Is<string>(text => text.StartsWith("MATCH", StringComparison.Ordinal) && text.EndsWith("DELETE `migration`", StringComparison.Ordinal)),
            Arg.Any<IDictionary<string, object>>());
    }

    [Fact]
    public async Task StatefulMigrationHistoryStoreReadsWritesAndRemovesEntries()
    {
        var driver = Substitute.For<IDriver>();
        var session = Substitute.For<IAsyncSession>();
        var runner = Substitute.For<IAsyncQueryRunner>();
        var cursor = Substitute.For<IResultCursor>();

        var historyRecord = Substitute.For<IRecord>();
        historyRecord["id"].Returns("001_stateful");
        historyRecord["checksum"].Returns("checksum-001");
        historyRecord["state"].Returns("Applying");
        historyRecord["startedAt"].Returns("2026-08-23T10:00:00.0000000+00:00");
        historyRecord["completedAt"].Returns(string.Empty);
        historyRecord["failureMessage"].Returns(string.Empty);
        historyRecord["failureType"].Returns(string.Empty);
        historyRecord["failureAt"].Returns(string.Empty);

        cursor.GetAsyncEnumerator(Arg.Any<CancellationToken>())
            .Returns(_ => new TestAsyncEnumerator<IRecord>([historyRecord]));

        runner.RunAsync(Arg.Any<string>())
            .Returns(cursor);

        runner.RunAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, object>>())
            .Returns(cursor);

        cursor.ConsumeAsync()
            .Returns(Substitute.For<IResultSummary>());

        driver.AsyncSession(Arg.Any<Action<SessionConfigBuilder>>())
            .Returns(session);

        session.ExecuteReadAsync(
                Arg.Any<Func<IAsyncQueryRunner,
                    Task<Dictionary<string, MigrationHistoryEntry>>>>(),
                Arg.Any<Action<TransactionConfigBuilder>>())
            .Returns(call =>
                call.ArgAt<Func<IAsyncQueryRunner,
                    Task<Dictionary<string, MigrationHistoryEntry>>>>(0)(runner));

        session.ExecuteWriteAsync(
                Arg.Any<Func<IAsyncQueryRunner, Task>>(),
                Arg.Any<Action<TransactionConfigBuilder>>())
            .Returns(call =>
                call.ArgAt<Func<IAsyncQueryRunner, Task>>(0)(runner));

        var store = new Neo4jMigrationHistoryStore(driver, "neo4j");

        var history = await store.GetMigrationHistoryAsync();

        Assert.Single(history);
        Assert.Equal(
            MigrationExecutionState.Applying,
            history["001_stateful"].State);
        Assert.Equal(
            "checksum-001",
            history["001_stateful"].Checksum);

        var entry = new MigrationHistoryEntry(
            "001_stateful",
            "checksum-001",
            MigrationExecutionState.Applied,
            DateTimeOffset.Parse(
                "2026-08-23T10:00:00+00:00",
            CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(
                "2026-08-23T10:01:00+00:00",
                CultureInfo.InvariantCulture));

        await store.SaveMigrationHistoryAsync(entry);
        await store.RemoveMigrationHistoryAsync("001_stateful");

        await runner.Received().RunAsync(
            Arg.Is<string>(text =>
                text.Contains("MERGE", StringComparison.Ordinal)),
            Arg.Any<IDictionary<string, object>>());

        await runner.Received().RunAsync(
            Arg.Is<string>(text =>
                text.Contains("DELETE", StringComparison.Ordinal)),
            Arg.Any<IDictionary<string, object>>());

        await session.Received(3).DisposeAsync();
    }

    [Fact]
    public async Task MigrationExecutorRejectsInvalidExecutions()
    {
        var driver = Substitute.For<IDriver>();
        var executor = new Neo4jMigrationExecutor(driver);
        var nonTransactional = new MigrationExecution(
            "001_initial",
            "abc",
            [new MigrationCommand("CREATE DATABASE", false)]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await executor.ApplyAsync(null!));
        await Assert.ThrowsAsync<NotSupportedException>(async () => await executor.ApplyAsync(nonTransactional));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await executor.GetAppliedMigrationsAsync(cancellation.Token));
    }

    [Fact]
    public async Task ProviderExposesCompletePipelineAndHonorsDriverOwnership()
    {
        var driver = Substitute.For<IDriver>();
        var provider = new Neo4jProvider(driver, "neo4j");

        Assert.IsType<Neo4jQueryCompiler>(provider.QueryCompiler);
        Assert.IsType<Neo4jCommandExecutor>(provider.CommandExecutor);
        Assert.IsType<Neo4jMutationExecutor>(provider.MutationExecutor);
        Assert.IsType<Neo4jMigrationDialect>(provider.MigrationDialect);
        Assert.IsType<Neo4jMigrationExecutor>(provider.MigrationExecutor);
        Assert.IsType<Neo4jMigrationHistoryStore>(
            ((IGraphMigrationHistoryProvider)provider).MigrationHistory);
        Assert.Equal(
            "neo4j:neo4j",
            ((IGraphMigrationHistoryProvider)provider).MigrationHistoryScope);
        Assert.IsType<Neo4jMigrationLock>(
            ((IGraphMigrationLockProvider)provider).MigrationLock);
        Assert.Equal(
            "neo4j:neo4j",
            ((IGraphMigrationLockProvider)provider).MigrationLockScope);
        Assert.True(provider.SupportsMigrationExecution);
        Assert.True(provider.Capabilities.SupportsTransactions);
        Assert.Equal(GraphTransactionScope.ClientManaged, provider.Capabilities.TransactionScope);
        Assert.IsType<Neo4jAnalyticsCompiler>(provider.AnalyticsCompiler);
        Assert.Equal(
            new[] { GraphAnalyticsAlgorithm.ShortestPath, GraphAnalyticsAlgorithm.AllShortestPaths },
            provider.AnalyticsCapabilities.Algorithms.OrderBy(algorithm => algorithm));
        Assert.Equal(
            GraphAnalyticsAvailability.Native,
            provider.AnalyticsCapabilities.GetDetails(GraphAnalyticsAlgorithm.ShortestPath).Availability);

        var analyticsProvider = new Neo4jProvider(driver, "neo4j", graphDataScienceEnabled: true);
        Assert.True(analyticsProvider.AnalyticsCapabilities.Supports(GraphAnalyticsAlgorithm.PageRank));
        Assert.True(analyticsProvider.AnalyticsCapabilities.SupportsWeightedRelationships);
        Assert.Equal("5.26 Community", analyticsProvider.AnalyticsCapabilities.TestedProviderVersion);
        Assert.Equal("Neo4j.Driver 6.3.0", analyticsProvider.AnalyticsCapabilities.ClientVersion);
        var pageRank = analyticsProvider.AnalyticsCapabilities.GetDetails(GraphAnalyticsAlgorithm.PageRank);
        Assert.Equal(GraphAnalyticsAvailability.Extension, pageRank.Availability);
        Assert.Equal(GraphCapabilityVerification.Compiler, pageRank.Verification);
        Assert.True(pageRank.SupportsWeights);
        Assert.False(analyticsProvider.AnalyticsCapabilities
            .GetDetails(GraphAnalyticsAlgorithm.Bridges).SupportsWeights);
        Assert.Throws<NotSupportedException>(() => provider.AnalyticsCapabilities
            .GetDetails(GraphAnalyticsAlgorithm.PageRank));

        var restricted = new Neo4jProvider(
            driver,
            "neo4j",
            graphDataScienceEnabled: true,
            analyticsAlgorithms: new HashSet<GraphAnalyticsAlgorithm> { GraphAnalyticsAlgorithm.Louvain });
        Assert.True(restricted.AnalyticsCapabilities.Supports(GraphAnalyticsAlgorithm.Louvain));
        Assert.False(restricted.AnalyticsCapabilities.Supports(GraphAnalyticsAlgorithm.PageRank));

        await provider.DisposeAsync();

        await driver.DidNotReceive().DisposeAsync();
        Assert.Throws<ArgumentNullException>(() => new Neo4jProvider((IDriver)null!));
        Assert.Throws<ArgumentNullException>(() => new Neo4jProvider((Neo4jOptions)null!));
    }

    [Fact]
    public async Task OptionsProviderCreatesAndDisposesOwnedDriver()
    {
        var options = new Neo4jOptions
        {
            Endpoint = new Uri("neo4j://localhost:7687"),
            Username = "neo4j",
            Password = "secret",
            Database = "neo4j",
        };

        await using var provider = new Neo4jProvider(options);

        Assert.Equal("neo4j://localhost:7687/", options.Endpoint.ToString());
        Assert.Equal("neo4j", options.Username);
        Assert.Equal("secret", options.Password);
        Assert.Equal("neo4j", options.Database);
        Assert.IsType<Neo4jCommandExecutor>(provider.CommandExecutor);
    }


    [Fact]
    public async Task Neo4jMigrationLockAcquiresAndCommitsLease()
    {
        var driver = Substitute.For<IDriver>();

        var bootstrapSession = Substitute.For<IAsyncSession>();
        var leaseSession = Substitute.For<IAsyncSession>();
        var transaction = Substitute.For<IAsyncTransaction>();

        var bootstrapCursor = Substitute.For<IResultCursor>();
        var acquireCursor = Substitute.For<IResultCursor>();
        var releaseCursor = Substitute.For<IResultCursor>();

        bootstrapCursor
            .ConsumeAsync()
            .Returns(Substitute.For<IResultSummary>());

        acquireCursor
            .ConsumeAsync()
            .Returns(Substitute.For<IResultSummary>());

        releaseCursor
            .ConsumeAsync()
            .Returns(Substitute.For<IResultSummary>());

        bootstrapSession
            .RunAsync(Arg.Any<string>())
            .Returns(bootstrapCursor);

        leaseSession
            .BeginTransactionAsync()
            .Returns(Task.FromResult(transaction));

        transaction
            .RunAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, object>>())
            .Returns(acquireCursor, releaseCursor);

        driver
            .AsyncSession(Arg.Any<Action<SessionConfigBuilder>>())
            .Returns(bootstrapSession, leaseSession);

        var migrationLock = new Neo4jMigrationLock(
            driver,
            "neo4j");

        var lease = await migrationLock.AcquireAsync(
            "neo4j:neo4j",
            CancellationToken.None);

        Assert.NotNull(lease);

        await lease.DisposeAsync();

        await bootstrapSession.Received(1).RunAsync(
            Arg.Is<string>(text =>
                text.Contains("CREATE CONSTRAINT", StringComparison.Ordinal)));

        await transaction.Received(1).CommitAsync();

        await transaction.Received(2).RunAsync(
            Arg.Any<string>(),
            Arg.Any<IDictionary<string, object>>());

        await bootstrapSession.Received(1).DisposeAsync();
        await leaseSession.Received(1).DisposeAsync();
    }


    [Fact]
    public async Task Neo4jMigrationLockHonorsCancellationBeforeOpeningSession()
    {
        var driver = Substitute.For<IDriver>();
        var migrationLock = new Neo4jMigrationLock(
            driver,
            "neo4j");

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await migrationLock.AcquireAsync(
                "neo4j:neo4j",
                cancellation.Token));

        driver.DidNotReceive()
            .AsyncSession(Arg.Any<Action<SessionConfigBuilder>>());
    }


    [Fact]
    public async Task Neo4jMigrationLockWrapsBootstrapFailures()
    {
        var driver = Substitute.For<IDriver>();
        var bootstrapSession = Substitute.For<IAsyncSession>();

        bootstrapSession
            .RunAsync(Arg.Any<string>())
            .Returns(Task.FromException<IResultCursor>(
                new InvalidOperationException(
                    "Constraint creation failed.")));

        driver
            .AsyncSession(Arg.Any<Action<SessionConfigBuilder>>())
            .Returns(bootstrapSession);

        var migrationLock = new Neo4jMigrationLock(
            driver,
            "neo4j");

        var exception = await Assert.ThrowsAsync<
            MigrationLockUnavailableException>(
            async () => await migrationLock.AcquireAsync(
                "neo4j:neo4j"));

        Assert.Equal("neo4j:neo4j", exception.Scope);
        Assert.Contains(
            "constraint could not be prepared",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.IsType<InvalidOperationException>(
            exception.InnerException);

        await bootstrapSession.Received(1).DisposeAsync();
    }


    [Fact]
    public async Task Neo4jMigrationLockWrapsTransactionAcquireFailures()
    {
        var driver = Substitute.For<IDriver>();

        var bootstrapSession = Substitute.For<IAsyncSession>();
        var leaseSession = Substitute.For<IAsyncSession>();
        var transaction = Substitute.For<IAsyncTransaction>();
        var bootstrapCursor = Substitute.For<IResultCursor>();

        bootstrapCursor
            .ConsumeAsync()
            .Returns(Substitute.For<IResultSummary>());

        bootstrapSession
            .RunAsync(Arg.Any<string>())
            .Returns(bootstrapCursor);

        leaseSession
            .BeginTransactionAsync()
            .Returns(Task.FromResult(transaction));

        transaction
            .RunAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, object>>())
            .Returns(Task.FromException<IResultCursor>(
                new InvalidOperationException(
                    "Lock node creation failed.")));

        driver
            .AsyncSession(Arg.Any<Action<SessionConfigBuilder>>())
            .Returns(bootstrapSession, leaseSession);

        var migrationLock = new Neo4jMigrationLock(
            driver,
            "neo4j");

        var exception = await Assert.ThrowsAsync<
            MigrationLockUnavailableException>(
            async () => await migrationLock.AcquireAsync(
                "neo4j:neo4j"));

        Assert.Equal("neo4j:neo4j", exception.Scope);
        Assert.Contains(
            "could not be acquired",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.IsType<InvalidOperationException>(
            exception.InnerException);

        await transaction.Received(1).RollbackAsync();
        await transaction.Received(1).DisposeAsync();
        await leaseSession.Received(1).DisposeAsync();
    }


    [Fact]
    public async Task Neo4jMigrationLockRollsBackWhenLeaseReleaseFails()
    {
        var driver = Substitute.For<IDriver>();

        var bootstrapSession = Substitute.For<IAsyncSession>();
        var leaseSession = Substitute.For<IAsyncSession>();
        var transaction = Substitute.For<IAsyncTransaction>();

        var bootstrapCursor = Substitute.For<IResultCursor>();
        var acquireCursor = Substitute.For<IResultCursor>();

        bootstrapCursor
            .ConsumeAsync()
            .Returns(Substitute.For<IResultSummary>());

        acquireCursor
            .ConsumeAsync()
            .Returns(Substitute.For<IResultSummary>());

        bootstrapSession
            .RunAsync(Arg.Any<string>())
            .Returns(bootstrapCursor);

        leaseSession
            .BeginTransactionAsync()
            .Returns(Task.FromResult(transaction));

        transaction
            .RunAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, object>>())
            .Returns(
                _ => Task.FromResult<IResultCursor>(acquireCursor),
                _ => Task.FromException<IResultCursor>(
                    new InvalidOperationException(
                        "Lock release failed.")));

        driver
            .AsyncSession(Arg.Any<Action<SessionConfigBuilder>>())
            .Returns(bootstrapSession, leaseSession);

        var migrationLock = new Neo4jMigrationLock(
            driver,
            "neo4j");

        var lease = await migrationLock.AcquireAsync(
            "neo4j:neo4j");

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await lease.DisposeAsync());

        await transaction.Received(1).RollbackAsync();
        await transaction.Received(1).DisposeAsync();
        await leaseSession.Received(1).DisposeAsync();
    }


    private static INode Node(string id, string label, params (string Name, object Value)[] properties)
    {
        var node = Substitute.For<INode>();
        node.ElementId.Returns(id);
        node.Labels.Returns(string.IsNullOrEmpty(label) ? [] : [label]);
        node.Properties.Returns(properties.ToDictionary(property => property.Name, property => property.Value));
        return node;
    }

    private static IRelationship Relationship(
        string id,
        string type,
        string source,
        string target,
        params (string Name, object Value)[] properties)
    {
        var relation = Substitute.For<IRelationship>();
        relation.ElementId.Returns(id);
        relation.Type.Returns(type);
        relation.StartNodeElementId.Returns(source);
        relation.EndNodeElementId.Returns(target);
        relation.Properties.Returns(properties.ToDictionary(property => property.Name, property => property.Value));
        return relation;
    }

    private static GraphIdentity Identity(string nodeType, object value) =>
        new(typeof(object), nodeType, "Id", value);

    private static Dictionary<string, object?> Properties(params (string Name, object? Value)[] properties) =>
        properties.ToDictionary(property => property.Name, property => property.Value);

    private sealed class TestAsyncEnumerator<T>(IEnumerable<T> values) : IAsyncEnumerator<T>
    {
        private readonly IEnumerator<T> enumerator = values.GetEnumerator();

        public T Current => enumerator.Current;

        public ValueTask DisposeAsync()
        {
            enumerator.Dispose();
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> MoveNextAsync() => ValueTask.FromResult(enumerator.MoveNext());
    }
}
