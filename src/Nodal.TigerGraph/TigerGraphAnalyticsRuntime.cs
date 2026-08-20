using Nodal.Core.Analytics;

namespace Nodal.TigerGraph;

/// <summary>Exposes the installed-query analytics contract declared for a TigerGraph deployment.</summary>
public sealed class TigerGraphAnalyticsRuntime : IGraphAnalyticsRuntime
{
    private readonly GraphAnalyticsRuntimeSnapshot snapshot;

    /// <summary>Initializes a runtime snapshot from explicitly configured installed queries.</summary>
    public TigerGraphAnalyticsRuntime(IReadOnlyDictionary<GraphAnalyticsAlgorithm, string> installedQueries)
    {
        ArgumentNullException.ThrowIfNull(installedQueries);
        snapshot = new GraphAnalyticsRuntimeSnapshot(
            null,
            installedQueries.Values.ToHashSet(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            installedQueries.Keys.ToHashSet(),
            false);
    }

    /// <inheritdoc />
    public ValueTask<GraphAnalyticsRuntimeSnapshot> DiscoverAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(snapshot);
    }

    /// <inheritdoc />
    public ValueTask EnsureProjectionAsync(
        GraphProjectionDefinition projection,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException(new NotSupportedException(
            "TigerGraph analytics operate on the named database graph and do not use GDS-style projections."));

    /// <inheritdoc />
    public ValueTask DropProjectionAsync(string name, CancellationToken cancellationToken = default) =>
        ValueTask.FromException(new NotSupportedException(
            "TigerGraph analytics operate on the named database graph and do not use GDS-style projections."));
}
