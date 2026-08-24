namespace Nodal.Core.Migrations;

/// <summary>
/// Provides stateful persistence for migration lifecycle records.
/// </summary>
/// <remarks>
/// This is an optional provider capability. Providers that cannot persist
/// lifecycle state safely must not implement this interface.
/// </remarks>
public interface IGraphMigrationHistoryStore
{
    /// <summary>
    /// Reads migration history for the current provider/database/graph scope.
    /// </summary>
    /// <param name="cancellationToken">
    /// Token used to cancel the history read.
    /// </param>
    /// <returns>
    /// Migration records keyed by their stable migration identifier.
    /// </returns>
    ValueTask<IReadOnlyDictionary<string, MigrationHistoryEntry>>
        GetMigrationHistoryAsync(
            CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists or replaces one migration lifecycle record.
    /// </summary>
    /// <param name="entry">
    /// The migration lifecycle entry to persist.
    /// </param>
    /// <param name="cancellationToken">
    /// Token used to cancel the history write.
    /// </param>
    ValueTask SaveMigrationHistoryAsync(
        MigrationHistoryEntry entry,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes one migration lifecycle record after a successful revert.
    /// </summary>
    /// <param name="migrationId">
    /// The stable migration identifier to remove.
    /// </param>
    /// <param name="cancellationToken">
    /// Token used to cancel the history removal.
    /// </param>
    ValueTask RemoveMigrationHistoryAsync(
        string migrationId,
        CancellationToken cancellationToken = default);
}
