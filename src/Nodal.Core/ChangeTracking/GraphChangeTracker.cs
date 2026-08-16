namespace Nodal.Core.ChangeTracking;

/// <summary>
/// Exposes the entries currently managed by a graph context.
/// </summary>
public sealed class GraphChangeTracker
{
    private readonly GraphStateManager stateManager;

    internal GraphChangeTracker(GraphStateManager stateManager) => this.stateManager = stateManager;

    /// <summary>Gets a stable snapshot of all currently tracked node and relationship entries.</summary>
    public IReadOnlyList<GraphEntry> Entries() => stateManager.Entries.ToArray();

    /// <summary>Gets entries with the requested unit-of-work state.</summary>
    public IReadOnlyList<GraphEntry> Entries(GraphEntryState state) =>
        stateManager.Entries.Where(entry => entry.State == state).ToArray();
}
