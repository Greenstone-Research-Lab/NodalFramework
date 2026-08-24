using Nodal.Core.Migrations;

namespace Nodal.Migrations;

/// <summary>
/// Validates the deterministic schema-evolution operation order.
/// </summary>
internal static class MigrationOperationOrderingValidator
{
    public static void Validate(
        IReadOnlyList<MigrationOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);

        var cleanupStarted = false;

        foreach (var operation in operations)
        {
            if (IsCleanup(operation))
            {
                cleanupStarted = true;
                continue;
            }

            if (cleanupStarted)
            {
                throw new InvalidOperationException(
                    "Migration operations must be ordered as schema, " +
                    "backfill, then cleanup. A non-cleanup operation was " +
                    "declared after cleanup had started.");
            }
        }
    }

    private static bool IsCleanup(MigrationOperation operation) =>
        operation is
            DropNodeTypeOperation or
            DropRelationTypeOperation or
            DropSchemaObjectOperation or
            DropIndexOperation or
            DropUniqueConstraintOperation or
            DropNodePropertyOperation or
            DropRelationPropertyOperation;
}
