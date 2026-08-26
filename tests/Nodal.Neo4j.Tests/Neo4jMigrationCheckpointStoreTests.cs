using System.Globalization;
using Neo4j.Driver;
using Nodal.Core.Migrations;
using NSubstitute;

namespace Nodal.Neo4j.Tests;

public sealed class Neo4jMigrationCheckpointStoreTests
{
    [Fact]
    public async Task ReadsCheckpointAndConfiguresDatabase()
    {
        var fixture = new CheckpointStoreFixture(
            Record(
                ("token", "page-2"),
                ("processed", 25),
                ("updatedAt", "2026-08-25T10:15:30.0000000+00:00")));
        var store = new Neo4jMigrationCheckpointStore(fixture.Driver, "neo4j");

        var checkpoint = await store.GetAsync("normalize", fixture.CancellationToken);

        Assert.NotNull(checkpoint);
        Assert.Equal("normalize", checkpoint.BackfillName);
        Assert.Equal("page-2", checkpoint.ContinuationToken);
        Assert.Equal(25, checkpoint.Processed);
        Assert.Equal(
            DateTimeOffset.Parse(
                "2026-08-25T10:15:30.0000000+00:00",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind),
            checkpoint.UpdatedAt);
        Assert.Equal("neo4j", Build(fixture.SessionBuilder).Database);
        await fixture.Session.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task ReturnsNullWhenCheckpointDoesNotExistWithoutSelectingDatabase()
    {
        var fixture = new CheckpointStoreFixture();
        var store = new Neo4jMigrationCheckpointStore(fixture.Driver);

        var checkpoint = await store.GetAsync("missing");

        Assert.Null(checkpoint);
        Assert.Null(Build(fixture.SessionBuilder).Database);
    }

    [Fact]
    public async Task SavesAndRemovesCheckpointWithParameterizedCommands()
    {
        var fixture = new CheckpointStoreFixture();
        var store = new Neo4jMigrationCheckpointStore(fixture.Driver, "neo4j");
        var checkpoint = new MigrationBackfillCheckpoint(
            "normalize",
            "page-3",
            50,
            DateTimeOffset.Parse(
                "2026-08-25T12:00:00+03:00",
                CultureInfo.InvariantCulture));

        await store.SaveAsync(checkpoint, fixture.CancellationToken);
        await store.RemoveAsync(checkpoint.BackfillName, fixture.CancellationToken);

        await fixture.Runner.Received(1).RunAsync(
            Arg.Is<string>(query => query.StartsWith("MERGE", StringComparison.Ordinal)),
            Arg.Is<IDictionary<string, object?>>(parameters =>
                Equals(parameters["name"], checkpoint.BackfillName) &&
                Equals(parameters["token"], checkpoint.ContinuationToken) &&
                Equals(parameters["processed"], checkpoint.Processed) &&
                Equals(parameters["updatedAt"], "2026-08-25T09:00:00.0000000+00:00")));
        await fixture.Runner.Received(1).RunAsync(
            Arg.Is<string>(query => query.Contains("DELETE checkpoint", StringComparison.Ordinal)),
            Arg.Is<IDictionary<string, object>>(parameters =>
                Equals(parameters["name"], checkpoint.BackfillName)));
        await fixture.Cursor.Received(2).ConsumeAsync();
        await fixture.Session.Received(2).DisposeAsync();
    }

    [Fact]
    public async Task ValidatesRequiredArgumentsBeforeOpeningSession()
    {
        var driver = Substitute.For<IDriver>();
        var store = new Neo4jMigrationCheckpointStore(driver);

        Assert.Throws<ArgumentNullException>(
            () => new Neo4jMigrationCheckpointStore(null!));
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await store.GetAsync(" "));
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await store.SaveAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await store.RemoveAsync(string.Empty));
        driver.DidNotReceive().AsyncSession(Arg.Any<Action<SessionConfigBuilder>>());
    }

    private static IRecord Record(params (string Name, object Value)[] values)
    {
        var record = Substitute.For<IRecord>();
        foreach (var value in values)
        {
            record[value.Name].Returns(value.Value);
        }

        return record;
    }

    private static SessionConfig Build(SessionConfigBuilder builder) =>
        (SessionConfig)typeof(SessionConfigBuilder)
            .GetMethod(
                "Build",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)!
            .Invoke(builder, null)!;

    private static SessionConfigBuilder CreateSessionBuilder()
    {
        var config = (SessionConfig)Activator.CreateInstance(
            typeof(SessionConfig),
            nonPublic: true)!;
        return (SessionConfigBuilder)Activator.CreateInstance(
            typeof(SessionConfigBuilder),
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic,
            binder: null,
            args: [config],
            culture: null)!;
    }

    private sealed class CheckpointStoreFixture
    {
        public CheckpointStoreFixture(params IRecord[] records)
        {
            Cursor.GetAsyncEnumerator(Arg.Any<CancellationToken>())
                .Returns(_ => new TestAsyncEnumerator<IRecord>(records));
            Cursor.ConsumeAsync().Returns(Substitute.For<IResultSummary>());
            Runner.RunAsync(
                    Arg.Any<string>(),
                    Arg.Any<IDictionary<string, object>>())
                .Returns(Cursor);
            Driver.AsyncSession(Arg.Any<Action<SessionConfigBuilder>>())
                .Returns(call =>
                {
                    call.Arg<Action<SessionConfigBuilder>>()(SessionBuilder);
                    return Session;
                });
            Session.ExecuteReadAsync(
                    Arg.Any<Func<IAsyncQueryRunner, Task<MigrationBackfillCheckpoint?>>>(),
                    Arg.Any<Action<TransactionConfigBuilder>>())
                .Returns(call => call.ArgAt<Func<IAsyncQueryRunner, Task<MigrationBackfillCheckpoint?>>>(0)(Runner));
            Session.ExecuteWriteAsync(
                    Arg.Any<Func<IAsyncQueryRunner, Task>>(),
                    Arg.Any<Action<TransactionConfigBuilder>>())
                .Returns(call => call.ArgAt<Func<IAsyncQueryRunner, Task>>(0)(Runner));
        }

        public CancellationToken CancellationToken { get; } = new CancellationTokenSource().Token;

        public IDriver Driver { get; } = Substitute.For<IDriver>();

        public IResultCursor Cursor { get; } = Substitute.For<IResultCursor>();

        public IAsyncQueryRunner Runner { get; } = Substitute.For<IAsyncQueryRunner>();

        public IAsyncSession Session { get; } = Substitute.For<IAsyncSession>();

        public SessionConfigBuilder SessionBuilder { get; } = CreateSessionBuilder();
    }

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
