using Neo4j.Driver;
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
        Assert.True(provider.SupportsMigrationExecution);
        Assert.True(provider.Capabilities.SupportsTransactions);
        Assert.Equal(GraphTransactionScope.ClientManaged, provider.Capabilities.TransactionScope);

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
