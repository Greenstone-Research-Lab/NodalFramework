using Nodal.Core.Migrations;

namespace Nodal.Migrations;

/// <summary>
/// Analyzes provider-neutral migration operations before execution.
/// </summary>
public sealed class MigrationPreflightAnalyzer
{
    private readonly IGraphMigrationDialect dialect;

    /// <summary>
    /// Initializes a preflight analyzer for one provider dialect.
    /// </summary>
    /// <param name="dialect">
    /// The provider-specific migration dialect.
    /// </param>
    public MigrationPreflightAnalyzer(
        IGraphMigrationDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(dialect);

        this.dialect = dialect;
    }

    /// <summary>
    /// Analyzes one migration without executing provider commands.
    /// </summary>
    public MigrationPreflightResult Analyze(
        NodalMigration migration)
    {
        ArgumentNullException.ThrowIfNull(migration);

        var builder = new MigrationBuilder();
        migration.Up(builder);

        return Analyze(builder.Operations);
    }

    /// <summary>
    /// Analyzes provider-neutral migration operations.
    /// </summary>
    public MigrationPreflightResult Analyze(
        IReadOnlyList<MigrationOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);

        var issues = new List<MigrationPreflightIssue>();

        foreach (var operation in operations)
        {
            AnalyzeOperation(operation, issues);
        }

        return new MigrationPreflightResult(issues);
    }

    private void AnalyzeOperation(
        MigrationOperation operation,
        List<MigrationPreflightIssue> issues)
    {
        if (IsDestructive(operation))
        {
            issues.Add(new MigrationPreflightIssue(
                MigrationPreflightKind.Destructive,
                "NODAL-MIGRATION-DESTRUCTIVE",
                $"Operation '{operation.GetType().Name}' " +
                "may remove schema or data and requires explicit approval.",
                operation.GetType()));
        }

        IReadOnlyList<MigrationCommand> commands;

        try
        {
            commands = dialect.Compile([operation]);
        }
        catch (NotSupportedException exception)
        {
            issues.Add(new MigrationPreflightIssue(
                MigrationPreflightKind.Unsupported,
                "NODAL-MIGRATION-UNSUPPORTED",
                exception.Message,
                operation.GetType()));

            return;
        }

        if (commands.Count == 0)
        {
            issues.Add(new MigrationPreflightIssue(
                MigrationPreflightKind.Warning,
                "NODAL-MIGRATION-NATIVE-SCHEMA",
                $"Provider emitted no schema command for " +
                $"'{operation.GetType().Name}'. " +
                "The provider may manage this model implicitly.",
                operation.GetType()));

            return;
        }

        issues.Add(new MigrationPreflightIssue(
            MigrationPreflightKind.Supported,
            "NODAL-MIGRATION-SUPPORTED",
            $"Operation '{operation.GetType().Name}' " +
            "has a provider execution plan.",
            operation.GetType()));
    }

    private static bool IsDestructive(
        MigrationOperation operation) =>
        operation is
            DropNodeTypeOperation or
            DropRelationTypeOperation or
            DropSchemaObjectOperation;
}
