namespace Nodal.Core.Migrations;

/// <summary>
/// Controls safety behavior during migration execution.
/// </summary>
public sealed record MigrationExecutionOptions
{
    /// <summary>
    /// Gets whether destructive schema operations are explicitly approved.
    /// </summary>
    /// <remarks>
    /// The default is false. This prevents accidental data or schema removal.
    /// </remarks>
    public bool AllowDestructiveOperations { get; init; }
}
