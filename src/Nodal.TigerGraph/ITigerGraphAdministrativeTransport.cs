using Nodal.Core.Migrations;

namespace Nodal.TigerGraph;

/// <summary>
/// Executes privileged GSQL administrative commands through an environment-appropriate channel.
/// </summary>
/// <remarks>
/// Self-managed deployments can implement this contract with the GSQL shell, while managed
/// deployments can use their supported administrative API. Nodal does not assume an undocumented endpoint.
/// </remarks>
public interface ITigerGraphAdministrativeTransport
{
    /// <summary>Executes one schema or query-administration command and fails on rejection.</summary>
    ValueTask ExecuteAsync(MigrationCommand command, CancellationToken cancellationToken = default);
}
