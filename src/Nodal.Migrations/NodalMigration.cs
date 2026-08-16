namespace Nodal.Migrations;

/// <summary>
/// Defines reversible, provider-neutral changes to a graph model.
/// </summary>
public abstract class NodalMigration
{
    /// <summary>Gets the stable identifier persisted in migration history.</summary>
    public virtual string Id => GetType().Name;

    /// <summary>Gets whether this migration explicitly supports a downward plan.</summary>
    public virtual bool IsReversible => true;

    /// <summary>
    /// Builds the operations that apply this migration.
    /// </summary>
    protected internal abstract void Up(MigrationBuilder migration);

    /// <summary>
    /// Builds the operations that revert this migration.
    /// </summary>
    protected internal abstract void Down(MigrationBuilder migration);
}
