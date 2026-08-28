namespace Nodal.Import.Relational;

/// <summary>
/// Provides relational metadata to inspection workflows while keeping provider clients,
/// connection pooling, authentication, and secret retrieval at the application boundary.
/// </summary>
public interface IRelationalInspectionHost
{
    /// <summary>Gets the provider family recorded in the generated interaction model.</summary>
    string ProviderName { get; }

    /// <summary>Discovers the relational schema without mutating the source database.</summary>
    /// <param name="cancellationToken">Token that cancels metadata discovery.</param>
    /// <returns>A provider-neutral relational schema snapshot.</returns>
    ValueTask<RelationalSchemaSnapshot> InspectAsync(
        CancellationToken cancellationToken = default);
}
