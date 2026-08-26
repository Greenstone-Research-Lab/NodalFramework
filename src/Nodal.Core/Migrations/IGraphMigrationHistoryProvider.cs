namespace Nodal.Core.Migrations;

/// <summary>
/// Exposes stateful migration history as an optional provider capability.
/// </summary>
/// <remarks>
/// A provider should implement this interface only when it can persist
/// migration lifecycle states safely for its database and graph scope.
/// </remarks>
public interface IGraphMigrationHistoryProvider
{
    /// <summary>
    /// Gets the provider-specific migration history store.
    /// </summary>
    IGraphMigrationHistoryStore MigrationHistory { get; }

    /// <summary>
    /// Gets the stable provider/database/graph scope used by migration history.
    /// </summary>
    string MigrationHistoryScope { get; }
}
