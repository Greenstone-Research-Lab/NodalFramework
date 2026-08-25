using Nodal.Core.Migrations;

namespace Nodal.TigerGraph;

/// <summary>Describes the privileged TigerGraph administration features verified for one graph.</summary>
/// <param name="ServerVersion">The verified TigerGraph server version, or <c>unknown</c>.</param>
/// <param name="CanReadSchema">Whether schema metadata can be inspected.</param>
/// <param name="CanWriteSchema">Whether schema-change jobs can be created and run.</param>
/// <param name="CanInspectJobs">Whether deterministic job existence can be inspected.</param>
/// <param name="CanCleanupJobs">Whether temporary jobs can be removed.</param>
/// <param name="LockScope">The strongest migration-lock scope supplied by the transport.</param>
public sealed record TigerGraphAdministrativeCapabilities(
    string ServerVersion,
    bool CanReadSchema,
    bool CanWriteSchema,
    bool CanInspectJobs,
    bool CanCleanupJobs,
    TigerGraphMigrationLockScope LockScope)
{
    /// <summary>Throws when the control plane cannot safely execute migrations.</summary>
    public void EnsureMigrationSupport()
    {
        if (!CanReadSchema || !CanWriteSchema || !CanInspectJobs || !CanCleanupJobs ||
            LockScope is TigerGraphMigrationLockScope.None)
        {
            throw new NodalCapabilityNotSupportedException(
                "TigerGraph",
                "NODAL-TIGERGRAPH-MIGRATION-CONTROL-PLANE",
                "TigerGraph migrations require verified schema read/write, job inspection, " +
                "job cleanup, and graph-scoped locking capabilities.");
        }
    }
}

/// <summary>Identifies the coordination boundary of a TigerGraph migration lock.</summary>
public enum TigerGraphMigrationLockScope
{
    /// <summary>No migration locking is available.</summary>
    None,

    /// <summary>The lock coordinates migrators inside the current application process.</summary>
    Process,

    /// <summary>The lock coordinates migrators across hosts for the target graph.</summary>
    Distributed,
}

/// <summary>
/// Extends command execution with the control-plane operations required for recoverable migrations.
/// Managed deployments can implement this interface without exposing vendor payloads to Nodal Core.
/// </summary>
public interface ITigerGraphAdministrativeControlPlane : ITigerGraphAdministrativeTransport
{
    /// <summary>Discovers and verifies administrative capabilities for one graph.</summary>
    ValueTask<TigerGraphAdministrativeCapabilities> DiscoverCapabilitiesAsync(
        string graphName,
        CancellationToken cancellationToken = default);

    /// <summary>Returns whether a named schema-change job currently exists.</summary>
    ValueTask<bool> SchemaJobExistsAsync(
        string graphName,
        string jobName,
        CancellationToken cancellationToken = default);

    /// <summary>Acquires the transport's strongest graph-scoped migration lock.</summary>
    ValueTask<IAsyncDisposable> AcquireMigrationLockAsync(
        string graphName,
        CancellationToken cancellationToken = default);
}
