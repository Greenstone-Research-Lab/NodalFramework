namespace Nodal.Core.Migrations;

/// <summary>
/// Classifies one migration preflight finding.
/// </summary>
public enum MigrationPreflightKind
{
    /// <summary>
    /// The operation is supported and safe to plan.
    /// </summary>
    Supported,

    /// <summary>
    /// The provider cannot execute the operation.
    /// </summary>
    Unsupported,

    /// <summary>
    /// The operation can remove or irreversibly alter schema/data.
    /// </summary>
    Destructive,

    /// <summary>
    /// The operation is valid but requires operator attention.
    /// </summary>
    Warning
}

/// <summary>
/// Describes one migration preflight finding.
/// </summary>
/// <param name="Kind">The finding classification.</param>
/// <param name="Code">A stable machine-readable finding code.</param>
/// <param name="Message">A safe human-readable explanation.</param>
/// <param name="OperationType">The affected operation type.</param>
public sealed record MigrationPreflightIssue(
    MigrationPreflightKind Kind,
    string Code,
    string Message,
    Type OperationType);

/// <summary>
/// Represents the result of validating a migration before execution.
/// </summary>
/// <param name="Issues">The findings produced during preflight.</param>
/// <param name="ProviderName">The provider dialect name used for analysis.</param>
public sealed record MigrationPreflightResult(
    IReadOnlyList<MigrationPreflightIssue> Issues,
    string ProviderName = "Unknown")
{
    /// <summary>
    /// Gets whether the plan contains no unsupported operation.
    /// </summary>
    public bool IsValid =>
        Issues.All(issue =>
            issue.Kind is not MigrationPreflightKind.Unsupported);

    /// <summary>
    /// Gets whether explicit destructive-operation approval is required.
    /// </summary>
    public bool RequiresApproval =>
        Issues.Any(issue =>
            issue.Kind is MigrationPreflightKind.Destructive);

    /// <summary>
    /// Gets whether the result contains warnings.
    /// </summary>
    public bool HasWarnings =>
        Issues.Any(issue =>
            issue.Kind is MigrationPreflightKind.Warning);

    /// <summary>
    /// Throws when the result contains an unsupported operation.
    /// </summary>
    public void ThrowIfInvalid()
    {
        var unsupported = Issues
            .Where(issue =>
                issue.Kind is MigrationPreflightKind.Unsupported)
            .ToArray();

        if (unsupported.Length == 0)
        {
            return;
        }

        var message = string.Join(
            Environment.NewLine,
            unsupported.Select(issue =>
                $"[{issue.Code}] {issue.Message}"));

        var first = unsupported[0];
        throw new NodalCapabilityNotSupportedException(
            ProviderName,
            first.Code,
            $"Migration preflight failed:{Environment.NewLine}{message}");
    }
}
