namespace Nodal.Core.Migrations;

/// <summary>Reads a provider's live schema into the provider-neutral snapshot contract.</summary>
public interface IGraphSchemaIntrospector
{
    /// <summary>Captures the current graph schema without mutating the provider.</summary>
    ValueTask<NodalSchemaSnapshot> CaptureAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>Provides provider identity for a schema introspector.</summary>
public interface IGraphSchemaIntrospectionProvider
{
    /// <summary>Gets the live schema introspector.</summary>
    IGraphSchemaIntrospector SchemaIntrospector { get; }
}
