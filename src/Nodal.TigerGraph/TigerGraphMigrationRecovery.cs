namespace Nodal.TigerGraph;

/// <summary>
/// Provides explicit operator-controlled reconciliation for TigerGraph schema jobs with unknown outcomes.
/// </summary>
public sealed class TigerGraphMigrationRecovery
{
    private readonly TigerGraphSchemaJobJournalStore journal;

    /// <summary>Initializes the recovery service over a durable TigerGraph schema-job journal.</summary>
    public TigerGraphMigrationRecovery(TigerGraphSchemaJobJournalStore journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        this.journal = journal;
    }

    /// <summary>Reads the durable execution state for one migration.</summary>
    public ValueTask<TigerGraphSchemaJobJournalEntry?> InspectAsync(
        string migrationId,
        CancellationToken cancellationToken = default) =>
        journal.GetAsync(migrationId, cancellationToken);

    /// <summary>
    /// Confirms, after external schema inspection, that an unknown schema job applied successfully.
    /// The next migration run performs cleanup and history reconciliation without replaying the schema change.
    /// </summary>
    public ValueTask ConfirmSchemaAppliedAsync(
        string migrationId,
        CancellationToken cancellationToken = default) =>
        ResolveAsync(migrationId, schemaApplied: true, cancellationToken);

    /// <summary>
    /// Confirms, after external schema inspection, that an unknown schema job did not apply.
    /// The next migration run may safely clean a stale job and execute the migration again.
    /// </summary>
    public ValueTask ConfirmSchemaNotAppliedAsync(
        string migrationId,
        CancellationToken cancellationToken = default) =>
        ResolveAsync(migrationId, schemaApplied: false, cancellationToken);

    private async ValueTask ResolveAsync(
        string migrationId,
        bool schemaApplied,
        CancellationToken cancellationToken)
    {
        var entry = await journal.GetAsync(migrationId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"TigerGraph migration journal entry '{migrationId}' was not found.");
        if (entry.Phase is not TigerGraphSchemaJobPhase.SchemaOutcomeUnknown)
        {
            throw new InvalidOperationException(
                $"TigerGraph migration '{migrationId}' is in phase '{entry.Phase}' and does not require outcome reconciliation.");
        }

        await journal.SaveAsync(entry with
        {
            Phase = schemaApplied
                ? TigerGraphSchemaJobPhase.SchemaAppliedHistoryPending
                : TigerGraphSchemaJobPhase.Failed,
            UpdatedAt = DateTimeOffset.UtcNow,
            FailureMessage = null,
            FailureType = null,
        }, cancellationToken).ConfigureAwait(false);
    }
}
