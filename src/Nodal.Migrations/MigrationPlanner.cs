using Nodal.Core.Migrations;

namespace Nodal.Migrations;

/// <summary>
/// Builds provider-specific execution plans from Nodal migrations.
/// </summary>
public sealed class MigrationPlanner(IGraphMigrationDialect dialect)
{
    /// <summary>
    /// Creates an ordered provider-specific plan for applying a migration.
    /// </summary>
    public IReadOnlyList<MigrationCommand> PlanUp(
        NodalMigration migration)
    {
        ArgumentNullException.ThrowIfNull(migration);

        var builder = new MigrationBuilder();
        migration.Up(builder);

        return dialect.Compile(builder.Operations);
    }

    /// <summary>
    /// Creates an ordered provider-specific plan for reverting a migration.
    /// </summary>
    public IReadOnlyList<MigrationCommand> PlanDown(
        NodalMigration migration)
    {
        ArgumentNullException.ThrowIfNull(migration);

        var builder = new MigrationBuilder();
        migration.Down(builder);

        return dialect.Compile(builder.Operations);
    }

    /// <summary>
    /// Analyzes an upward migration before provider execution.
    /// </summary>
    public MigrationPreflightResult PreflightUp(
        NodalMigration migration)
    {
        ArgumentNullException.ThrowIfNull(migration);

        var builder = new MigrationBuilder();
        migration.Up(builder);

        return new MigrationPreflightAnalyzer(dialect)
            .Analyze(builder.Operations);
    }

    /// <summary>
    /// Analyzes a downward migration before provider execution.
    /// </summary>
    public MigrationPreflightResult PreflightDown(
        NodalMigration migration)
    {
        ArgumentNullException.ThrowIfNull(migration);

        var builder = new MigrationBuilder();
        migration.Down(builder);

        return new MigrationPreflightAnalyzer(dialect)
            .Analyze(builder.Operations);
    }
}
