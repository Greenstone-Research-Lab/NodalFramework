using Nodal.Core;
using Nodal.Core.Migrations;

namespace Nodal.Migrations;

/// <summary>Provides migration operations through <see cref="NodalContext.Database"/>.</summary>
public static class NodalDatabaseMigrationExtensions
{
    /// <summary>Builds a side-effect-free plan for all pending migrations.</summary>
    public static ValueTask<MigrationPlan> PlanMigrationsAsync(
        this NodalDatabaseFacade database,
        IEnumerable<NodalMigration> migrations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        return new MigrationRunner(database.GetMigrationProvider()).PlanAsync(migrations, cancellationToken);
    }

    /// <summary>Applies all pending migrations in declaration order.</summary>
    public static ValueTask<MigrationPlan> MigrateAsync(
        this NodalDatabaseFacade database,
        IEnumerable<NodalMigration> migrations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        return new MigrationRunner(database.GetMigrationProvider()).MigrateAsync(migrations, cancellationToken);
    }

    /// <summary>Reverts one explicitly selected migration.</summary>
    public static ValueTask<MigrationExecution> RevertMigrationAsync(
        this NodalDatabaseFacade database,
        NodalMigration migration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        return new MigrationRunner(database.GetMigrationProvider()).RevertAsync(migration, cancellationToken);
    }
}
