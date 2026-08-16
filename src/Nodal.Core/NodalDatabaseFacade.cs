using Nodal.Core.Execution;
using Nodal.Core.Migrations;

namespace Nodal.Core;

/// <summary>Exposes database-wide provider services without leaking transport-specific objects.</summary>
public sealed class NodalDatabaseFacade
{
    private readonly IGraphProvider provider;

    internal NodalDatabaseFacade(IGraphProvider provider) => this.provider = provider;

    /// <summary>Gets whether the configured provider implements migration execution.</summary>
    public bool SupportsMigrations => provider is IGraphMigrationProvider migrationProvider &&
        migrationProvider.SupportsMigrationExecution;

    /// <summary>Gets the migration provider or reports that this provider is query-only.</summary>
    public IGraphMigrationProvider GetMigrationProvider()
    {
        if (provider is not IGraphMigrationProvider migrationProvider ||
            !migrationProvider.SupportsMigrationExecution)
        {
            throw new NotSupportedException(
                $"Graph provider '{provider.GetType().Name}' does not have migration execution configured.");
        }

        return migrationProvider;
    }
}
