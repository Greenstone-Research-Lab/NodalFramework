using Nodal.Core.Execution;
using Nodal.Core.Query;

namespace Nodal.Analytics.Observations;

/// <summary>Produces canonical observations through a provider-neutral query executor.</summary>
/// <example>
/// <code>
/// IGraphObservationSource source = new GraphQueryObservationSource(queryExecutor);
/// GraphObservation observation = await source.ObserveAsync(request, cancellationToken);
/// </code>
/// </example>
public sealed class GraphQueryObservationSource : IGraphObservationSource
{
    private readonly Func<GraphQueryModel, CancellationToken, ValueTask<GraphQueryResult>> executeSubgraph;

    /// <summary>Initializes a provider-neutral observation source.</summary>
    /// <param name="queryExecutor">The configured provider query executor.</param>
    public GraphQueryObservationSource(IGraphQueryExecutor queryExecutor)
    {
        ArgumentNullException.ThrowIfNull(queryExecutor);
        executeSubgraph = queryExecutor.ExecuteSubgraphAsync;
    }

    /// <summary>Initializes a source directly from the provider used by an application context.</summary>
    /// <param name="provider">The configured graph database provider.</param>
    public GraphQueryObservationSource(IGraphProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        executeSubgraph = async (query, cancellationToken) =>
        {
            var command = provider.QueryCompiler.Compile(query);
            return await provider.CommandExecutor.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
        };
    }

    /// <inheritdoc />
    public async ValueTask<GraphObservation> ObserveAsync(
        GraphObservationRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        var query = ApplyDefensiveLimit(request.Query, request.Options.MaxNodes);

        if (request.Timeout is not { } timeout)
        {
            var result = await executeSubgraph(query, cancellationToken).ConfigureAwait(false);
            return GraphObservationMaterializer.Materialize(result, request.Options);
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var timedResult = await executeSubgraph(query, timeoutSource.Token).ConfigureAwait(false);
        return GraphObservationMaterializer.Materialize(timedResult, request.Options);
    }

    private static GraphQueryModel ApplyDefensiveLimit(GraphQueryModel query, int maximumNodes)
    {
        var defensiveLimit = maximumNodes == int.MaxValue ? int.MaxValue : maximumNodes + 1;
        return query with { Limit = query.Limit is { } limit ? Math.Min(limit, defensiveLimit) : defensiveLimit };
    }

    private static void Validate(GraphObservationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Query);
        ArgumentNullException.ThrowIfNull(request.Options);
        if (request.Query.Projection != GraphQueryProjection.Subgraph)
        {
            throw new ArgumentException("An observation request requires a subgraph query projection.", nameof(request));
        }

        if (request.Query.Limit is <= 0)
        {
            throw new ArgumentException("A query limit must be positive when supplied.", nameof(request));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.Options.MaxNodes);
        if (request.Timeout is { } timeout && timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "An observation timeout must be positive.");
        }
    }
}
