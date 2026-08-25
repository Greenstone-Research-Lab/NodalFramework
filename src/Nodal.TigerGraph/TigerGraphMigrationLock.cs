using Nodal.Core.Migrations;

namespace Nodal.TigerGraph;

/// <summary>Adapts a verified TigerGraph control-plane lock to the portable migration contract.</summary>
public sealed class TigerGraphMigrationLock(
    ITigerGraphAdministrativeControlPlane controlPlane) : IGraphMigrationLock
{
    /// <inheritdoc />
    public ValueTask<IAsyncDisposable> AcquireAsync(
        string scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        const string prefix = "tigergraph:";
        var graphName = scope.StartsWith(prefix, StringComparison.Ordinal)
            ? scope[prefix.Length..]
            : scope;
        return controlPlane.AcquireMigrationLockAsync(graphName, cancellationToken);
    }
}
