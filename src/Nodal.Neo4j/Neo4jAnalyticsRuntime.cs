using Neo4j.Driver;
using Nodal.Core.Analytics;

namespace Nodal.Neo4j;

/// <summary>Discovers Neo4j GDS procedures and manages named in-memory projections.</summary>
public sealed class Neo4jAnalyticsRuntime : IGraphAnalyticsRuntime, IDisposable
{
    private readonly IDriver driver;
    private readonly string? database;
    private readonly IReadOnlySet<GraphAnalyticsAlgorithm> algorithms;
    private readonly TimeSpan cacheDuration;
    private readonly SemaphoreSlim gate = new(1, 1);
    private GraphAnalyticsRuntimeSnapshot? snapshot;
    private DateTimeOffset expiresAt;

    /// <summary>Initializes a runtime over an existing pooled Neo4j driver.</summary>
    public Neo4jAnalyticsRuntime(
        IDriver driver,
        string? database,
        IReadOnlySet<GraphAnalyticsAlgorithm> algorithms,
        TimeSpan cacheDuration)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(algorithms);
        ArgumentOutOfRangeException.ThrowIfLessThan(cacheDuration, TimeSpan.Zero);
        this.driver = driver;
        this.database = database;
        this.algorithms = algorithms;
        this.cacheDuration = cacheDuration;
    }

    /// <inheritdoc />
    public async ValueTask<GraphAnalyticsRuntimeSnapshot> DiscoverAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && snapshot is not null && DateTimeOffset.UtcNow < expiresAt)
        {
            return snapshot;
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!forceRefresh && snapshot is not null && DateTimeOffset.UtcNow < expiresAt)
            {
                return snapshot;
            }
            var version = await ReadScalarAsync("RETURN gds.version() AS value", cancellationToken).ConfigureAwait(false);
            var procedures = await ReadStringsAsync("CALL gds.list() YIELD name RETURN name AS value", cancellationToken)
                .ConfigureAwait(false);
            var projections = await ReadStringsAsync(
                "CALL gds.graph.list() YIELD graphName RETURN graphName AS value", cancellationToken)
                .ConfigureAwait(false);
            snapshot = new GraphAnalyticsRuntimeSnapshot(
                Convert.ToString(version, System.Globalization.CultureInfo.InvariantCulture),
                procedures.ToHashSet(StringComparer.Ordinal),
                projections.ToHashSet(StringComparer.Ordinal),
                algorithms,
                true);
            expiresAt = DateTimeOffset.UtcNow.Add(cacheDuration);
            return snapshot;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask EnsureProjectionAsync(
        GraphProjectionDefinition projection,
        CancellationToken cancellationToken = default)
    {
        ValidateProjection(projection);
        var existing = await ReadScalarAsync(
            "CALL gds.graph.exists($name) YIELD exists RETURN exists AS value",
            cancellationToken,
            new Dictionary<string, object> { ["name"] = projection.Name }).ConfigureAwait(false);
        if (existing is true)
        {
            return;
        }

        var relationship = new Dictionary<string, object>
        {
            [projection.RelationshipType] = new Dictionary<string, object?>
            {
                ["orientation"] = projection.Directed ? "NATURAL" : "UNDIRECTED",
                ["properties"] = projection.WeightProperty is null ? Array.Empty<string>() : new[] { projection.WeightProperty },
            },
        };
        await ExecuteAsync(
            "CALL gds.graph.project($name, $nodeType, $relationships) YIELD graphName RETURN graphName AS value",
            new Dictionary<string, object>
            {
                ["name"] = projection.Name,
                ["nodeType"] = projection.NodeType,
                ["relationships"] = relationship,
            }, cancellationToken, write: true).ConfigureAwait(false);
        Invalidate();
    }

    /// <inheritdoc />
    public async ValueTask DropProjectionAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        await ExecuteAsync(
            "CALL gds.graph.drop($name, false) YIELD graphName RETURN graphName AS value",
            new Dictionary<string, object> { ["name"] = name }, cancellationToken, write: true).ConfigureAwait(false);
        Invalidate();
    }

    private async ValueTask<object?> ReadScalarAsync(
        string text,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, object>? parameters = null)
    {
        var records = await ExecuteAsync(text, parameters ?? new Dictionary<string, object>(), cancellationToken)
            .ConfigureAwait(false);
        return records.Count == 0 ? null : records[0].Values.GetValueOrDefault("value");
    }

    private async ValueTask<IReadOnlyList<string>> ReadStringsAsync(string text, CancellationToken cancellationToken)
    {
        var records = await ExecuteAsync(text, new Dictionary<string, object>(), cancellationToken).ConfigureAwait(false);
        return records.Select(record => Convert.ToString(record["value"], System.Globalization.CultureInfo.InvariantCulture))
            .Where(value => value is not null).Select(value => value!).ToArray();
    }

    private async ValueTask<IReadOnlyList<IRecord>> ExecuteAsync(
        string text,
        IReadOnlyDictionary<string, object> parameters,
        CancellationToken cancellationToken,
        bool write = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var session = driver.AsyncSession(ConfigureSession);
        async Task<List<IRecord>> Execute(IAsyncQueryRunner transaction)
        {
            var cursor = await transaction.RunAsync(text, parameters.ToDictionary(item => item.Key, item => item.Value))
                .ConfigureAwait(false);
            return await cursor.ToListAsync(cancellationToken).ConfigureAwait(false);
        }
        return write
            ? await session.ExecuteWriteAsync(Execute).ConfigureAwait(false)
            : await session.ExecuteReadAsync(Execute).ConfigureAwait(false);
    }

    private void ConfigureSession(SessionConfigBuilder builder)
    {
        if (!string.IsNullOrWhiteSpace(database))
        {
            builder.WithDatabase(database);
        }
    }

    private void Invalidate()
    {
        snapshot = null;
        expiresAt = default;
    }

    private static void ValidateProjection(GraphProjectionDefinition projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentException.ThrowIfNullOrWhiteSpace(projection.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(projection.NodeType);
        ArgumentException.ThrowIfNullOrWhiteSpace(projection.RelationshipType);
    }

    /// <summary>Releases the discovery synchronization primitive.</summary>
    public void Dispose() => gate.Dispose();
}
