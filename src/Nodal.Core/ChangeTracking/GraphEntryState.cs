namespace Nodal.Core.ChangeTracking;

/// <summary>
/// Describes how a graph entry participates in the next unit-of-work commit.
/// </summary>
public enum GraphEntryState
{
    /// <summary>The entry is not managed by the current context.</summary>
    Detached,

    /// <summary>The entry is tracked but has no pending changes.</summary>
    Unchanged,

    /// <summary>The entry must be created.</summary>
    Added,

    /// <summary>The entry must be updated.</summary>
    Modified,

    /// <summary>The entry must be deleted.</summary>
    Deleted,
}
