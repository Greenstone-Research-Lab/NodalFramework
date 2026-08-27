using System.Collections.Concurrent;
using Nodal.Core.Migrations;

namespace Nodal.TigerGraph.Extensions;

/// <summary>Installs generated TigerGraph extension queries once per installer and graph fingerprint.</summary>
public sealed class TigerGraphInstalledQueryInstaller
{
    private readonly ConcurrentDictionary<string, Lazy<Task>> installations = new(StringComparer.Ordinal);
    private readonly ITigerGraphAdministrativeTransport transport;
    private readonly string graphName;

    /// <summary>Initializes an installer using the explicit administrative transport supplied by the host.</summary>
    public TigerGraphInstalledQueryInstaller(ITigerGraphAdministrativeTransport transport, string graphName)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentException.ThrowIfNullOrWhiteSpace(graphName);
        this.transport = transport;
        this.graphName = graphName;
    }

    /// <summary>Creates and installs a generated query. Concurrent calls for the same fingerprint share one operation.</summary>
    public async ValueTask InstallAsync(TigerGraphInstalledQueryDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var key = $"{graphName}:{definition.Fingerprint}";
        var lazy = installations.GetOrAdd(key, _ => new Lazy<Task>(() => InstallCoreAsync(definition), LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            installations.TryRemove(new KeyValuePair<string, Lazy<Task>>(key, lazy));
            throw;
        }
    }

    private async Task InstallCoreAsync(TigerGraphInstalledQueryDefinition definition)
    {
        var statements = definition.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (statements.Length != 2)
        {
            throw new InvalidOperationException("An installed-query definition must contain one create command and one install command.");
        }

        await transport.ExecuteAsync(new MigrationCommand(statements[0], false, MigrationCommandKind.QueryDefinition)).ConfigureAwait(false);
        await transport.ExecuteAsync(new MigrationCommand(statements[1], false, MigrationCommandKind.QueryInstallation)).ConfigureAwait(false);
    }
}
