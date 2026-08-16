namespace Nodal.Core.Migrations;

/// <summary>
/// Converts provider-neutral migration operations into database-specific commands.
/// </summary>
public interface IGraphMigrationDialect
{
    /// <summary>
    /// Compiles a validated sequence of migration operations.
    /// </summary>
    IReadOnlyList<MigrationCommand> Compile(IReadOnlyList<MigrationOperation> operations);
}

/// <summary>
/// Represents one provider-specific migration command.
/// </summary>
/// <param name="Text">The command text.</param>
/// <param name="IsTransactional">Whether the provider can execute the command transactionally.</param>
/// <param name="Kind">The command category used to select a safe execution channel.</param>
public sealed record MigrationCommand(
    string Text,
    bool IsTransactional,
    MigrationCommandKind Kind = MigrationCommandKind.Schema);

/// <summary>Classifies migration commands so providers can route administrative operations safely.</summary>
public enum MigrationCommandKind
{
    /// <summary>Changes graph schema metadata.</summary>
    Schema,

    /// <summary>Defines a stored or compiled query.</summary>
    QueryDefinition,

    /// <summary>Installs a previously defined query.</summary>
    QueryInstallation,
}

/// <summary>Exposes provider migration compilation and persisted-history execution.</summary>
public interface IGraphMigrationProvider
{
    /// <summary>Gets whether administrative migration execution is configured.</summary>
    bool SupportsMigrationExecution { get; }

    /// <summary>Gets the provider-specific migration dialect.</summary>
    IGraphMigrationDialect MigrationDialect { get; }

    /// <summary>Gets the provider-specific migration runtime.</summary>
    IGraphMigrationExecutor MigrationExecutor { get; }
}

/// <summary>Reads migration history and applies or reverts one complete migration.</summary>
public interface IGraphMigrationExecutor
{
    /// <summary>Returns migration identifiers already recorded for the target graph.</summary>
    ValueTask<IReadOnlyDictionary<string, string>> GetAppliedMigrationsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Executes upward commands and records the migration.</summary>
    ValueTask ApplyAsync(MigrationExecution execution, CancellationToken cancellationToken = default);

    /// <summary>Executes downward commands and removes the migration history entry.</summary>
    ValueTask RevertAsync(MigrationExecution execution, CancellationToken cancellationToken = default);
}

/// <summary>Contains one checksummed provider-specific migration execution.</summary>
public sealed record MigrationExecution(
    string Id,
    string Checksum,
    IReadOnlyList<MigrationCommand> Commands);
