using Nodal.Analytics.Observations;
using Nodal.Core.Analytics;
using Nodal.Core.Execution;
using Nodal.Core.Model;
using Nodal.Core.Providers;
using Nodal.Core.Query;

namespace Nodal.Analytics.Tests;

public sealed class GraphQueryObservationSourceTests
{
    [Fact]
    public async Task ExecutesBoundedSubgraphAndMaterializesObservation()
    {
        var executor = new RecordingExecutor(Result());
        var source = new GraphQueryObservationSource(executor);
        var request = new GraphObservationRequest(
            Query(),
            new GraphObservationOptions { MaxNodes = 5, MaxRelations = 5 });

        var observation = await source.ObserveAsync(request);

        Assert.Single(observation.Nodes);
        Assert.Equal(6, executor.Query!.Limit);
        Assert.Equal(GraphQueryProjection.Subgraph, executor.Query.Projection);
    }

    [Fact]
    public async Task ProviderConstructorCompilesAndExecutesTheBoundedQuery()
    {
        var provider = new RecordingProvider(Result());
        var source = new GraphQueryObservationSource(provider);

        var observation = await source.ObserveAsync(new GraphObservationRequest(
            Query(),
            new GraphObservationOptions { MaxNodes = 3 }));

        Assert.Single(observation.Nodes);
        Assert.Equal(4, provider.Executor.Command!.Parameters["limit"]);
    }

    [Fact]
    public async Task PreservesStricterCallerQueryLimit()
    {
        var executor = new RecordingExecutor(Result());
        var source = new GraphQueryObservationSource(executor);

        await source.ObserveAsync(new GraphObservationRequest(
            Query() with { Limit = 2 },
            new GraphObservationOptions { MaxNodes = 5 }));

        Assert.Equal(2, executor.Query!.Limit);
    }

    [Fact]
    public async Task PropagatesCallerCancellationWithoutReturningPartialObservation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var source = new GraphQueryObservationSource(new RecordingExecutor(Result(), waitForCancellation: true));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await source.ObserveAsync(
                new GraphObservationRequest(Query(), new GraphObservationOptions()),
                cancellation.Token));
    }

    [Fact]
    public async Task EnforcesConfiguredTimeout()
    {
        var source = new GraphQueryObservationSource(new RecordingExecutor(Result(), waitForCancellation: true));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await source.ObserveAsync(new GraphObservationRequest(
                Query(),
                new GraphObservationOptions(),
                TimeSpan.FromMilliseconds(10))));
    }

    [Fact]
    public async Task TransportFailureIsNotConvertedToAnObservation()
    {
        var source = new GraphQueryObservationSource(new RecordingExecutor(
            Result(), failure: new InvalidOperationException("transport failed")));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await source.ObserveAsync(new GraphObservationRequest(Query(), new GraphObservationOptions())));

        Assert.Equal("transport failed", exception.Message);
    }

    [Fact]
    public async Task OverLimitResultFailsWithoutPartialObservation()
    {
        var result = new GraphQueryResult(
            [Node("1"), Node("2")]);
        var source = new GraphQueryObservationSource(new RecordingExecutor(result));

        await Assert.ThrowsAsync<GraphObservationLimitExceededException>(async () =>
            await source.ObserveAsync(new GraphObservationRequest(
                Query(),
                new GraphObservationOptions { MaxNodes = 1 })));
    }

    [Fact]
    public async Task RejectsInvalidConstructionAndRequests()
    {
        Assert.Throws<ArgumentNullException>(() => new GraphQueryObservationSource((IGraphQueryExecutor)null!));
        Assert.Throws<ArgumentNullException>(() => new GraphQueryObservationSource((IGraphProvider)null!));
        var source = new GraphQueryObservationSource(new RecordingExecutor(Result()));

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await source.ObserveAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(async () => await source.ObserveAsync(
            new GraphObservationRequest(Query() with { Projection = GraphQueryProjection.Node }, new GraphObservationOptions())));
        await Assert.ThrowsAsync<ArgumentException>(async () => await source.ObserveAsync(
            new GraphObservationRequest(Query() with { Limit = 0 }, new GraphObservationOptions())));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await source.ObserveAsync(
            new GraphObservationRequest(Query(), new GraphObservationOptions { MaxNodes = 0 })));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await source.ObserveAsync(
            new GraphObservationRequest(Query(), new GraphObservationOptions(), TimeSpan.Zero)));
    }

    private static GraphQueryModel Query() => new(
        "Food", "n", null, [], null, [], GraphQueryProjection.Subgraph);

    private static GraphQueryResult Result() => new([Node("1")]);

    private static GraphNodeRecord Node(string id) => new(
        "Food", id, new Dictionary<string, object?>());

    private sealed class RecordingExecutor(
        GraphQueryResult result,
        bool waitForCancellation = false,
        Exception? failure = null) : IGraphQueryExecutor
    {
        public GraphQueryModel? Query { get; private set; }

        public async ValueTask<GraphQueryResult> ExecuteSubgraphAsync(
            GraphQueryModel query,
            CancellationToken cancellationToken = default)
        {
            Query = query;
            if (failure is not null)
            {
                throw failure;
            }

            if (waitForCancellation)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }

        public ValueTask<IReadOnlyList<TNode>> ExecuteAsync<TNode>(GraphQueryModel query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<IReadOnlyList<GraphPath<TSource, TRelation, TTarget>>> ExecutePathsAsync<TSource, TRelation, TTarget>(GraphQueryModel query, CancellationToken cancellationToken = default)
            where TRelation : notnull => throw new NotSupportedException();

        public ValueTask<int> ExecuteCountAsync(GraphQueryModel query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<IReadOnlyList<GraphAnalyticsRecord<TNode>>> ExecuteAnalyticsAsync<TNode>(GraphAnalyticsQueryModel query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<IReadOnlyList<GraphRoute<TNode, TRelation>>> ExecuteRoutesAsync<TNode, TRelation>(GraphAnalyticsQueryModel query, CancellationToken cancellationToken = default)
            where TRelation : notnull => throw new NotSupportedException();
    }

    private sealed class RecordingProvider(GraphQueryResult result) : IGraphProvider
    {
        public RecordingCommandExecutor Executor { get; } = new(result);

        public IGraphQueryCompiler QueryCompiler { get; } = new RecordingCompiler();

        public IGraphCommandExecutor CommandExecutor => Executor;

        public IGraphResultMaterializer ResultMaterializer { get; } = new UnsupportedMaterializer();
    }

    private sealed class RecordingCompiler : IGraphQueryCompiler
    {
        public GraphCommand Compile(GraphQueryModel query) => new(
            "provider-query",
            new Dictionary<string, object?> { ["limit"] = query.Limit });
    }

    private sealed class RecordingCommandExecutor(GraphQueryResult result) : IGraphCommandExecutor
    {
        public GraphCommand? Command { get; private set; }

        public ValueTask<GraphQueryResult> ExecuteAsync(
            GraphCommand command,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Command = command;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class UnsupportedMaterializer : IGraphResultMaterializer
    {
        public IReadOnlyList<TNode> Materialize<TNode>(GraphQueryResult result) => throw new NotSupportedException();

        public IReadOnlyList<GraphPath<TSource, TRelation, TTarget>> MaterializePaths<TSource, TRelation, TTarget>(
            GraphQueryResult result)
            where TRelation : notnull => throw new NotSupportedException();
    }
}
