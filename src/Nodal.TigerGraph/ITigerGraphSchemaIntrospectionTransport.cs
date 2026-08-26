using Nodal.Core.Migrations;

namespace Nodal.TigerGraph;

/// <summary>Supplies a TigerGraph schema description through a supported admin channel.</summary>
/// <remarks>
/// TigerGraph deployments expose different administrative channels (GSQL shell, managed APIs,
/// and version-specific endpoints). The provider deliberately keeps that transport injectable.
/// </remarks>
public interface ITigerGraphSchemaIntrospectionTransport
{
    /// <summary>Reads the current graph schema without applying mutations.</summary>
    ValueTask<NodalSchemaSnapshot> CaptureSchemaAsync(
        string graphName,
        CancellationToken cancellationToken = default);
}
