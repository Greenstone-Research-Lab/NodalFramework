using Nodal.Core.Migrations;

namespace Nodal.TigerGraph;

/// <summary>Adapts an explicitly supported TigerGraph schema transport to Nodal's snapshot contract.</summary>
public sealed class TigerGraphSchemaIntrospector : IGraphSchemaIntrospector
{
    private readonly ITigerGraphSchemaIntrospectionTransport transport;
    private readonly string graphName;

    /// <summary>Initializes an introspector for one TigerGraph graph.</summary>
    public TigerGraphSchemaIntrospector(
        ITigerGraphSchemaIntrospectionTransport transport,
        string graphName)
    {
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        this.graphName = string.IsNullOrWhiteSpace(graphName)
            ? throw new ArgumentException("A graph name is required.", nameof(graphName))
            : graphName;
    }

    /// <inheritdoc />
    public async ValueTask<NodalSchemaSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await transport.CaptureSchemaAsync(graphName, cancellationToken).ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(snapshot);
        return (snapshot with
        {
            ProviderName = "TigerGraph",
            ProviderVersion = snapshot.ProviderVersion,
        }).Normalize();
    }
}
