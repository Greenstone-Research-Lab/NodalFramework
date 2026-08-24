using Nodal.Core.Migrations;

namespace Nodal.Neo4j;

/// <summary>Compiles portable schema intent into idempotent Neo4j Cypher DDL.</summary>
public sealed class Neo4jMigrationDialect : IGraphMigrationDialect
{
    /// <inheritdoc />
    public IReadOnlyList<MigrationCommand> Compile(IReadOnlyList<MigrationOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        return operations.SelectMany(CompileOperation).ToArray();
    }

    private static IEnumerable<MigrationCommand> CompileOperation(MigrationOperation operation) => operation switch
    {
        CreateUniqueConstraintOperation unique =>
        [
            Command($"CREATE CONSTRAINT {Escape(Name("uq", unique.NodeType, unique.PropertyName))} IF NOT EXISTS " +
                $"FOR (`node`:{Escape(unique.NodeType)}) REQUIRE `node`.{Escape(unique.PropertyName)} IS UNIQUE"),
        ],
        CreateIndexOperation index =>
        [
            Command($"CREATE INDEX {Escape(Name("ix", index.NodeType, index.PropertyName))} IF NOT EXISTS " +
                $"FOR (`node`:{Escape(index.NodeType)}) ON (`node`.{Escape(index.PropertyName)})"),
        ],
        DropIndexOperation index =>
        [
            Command($"DROP INDEX {Escape(Name("ix", index.NodeType, index.PropertyName))} IF EXISTS"),
        ],
        DropUniqueConstraintOperation constraint =>
        [
            Command($"DROP CONSTRAINT {Escape(Name("uq", constraint.NodeType, constraint.PropertyName))} IF EXISTS"),
        ],
        DropSchemaObjectOperation drop =>
        [
            Command($"DROP {(drop.Kind == MigrationSchemaObjectKind.Index ? "INDEX" : "CONSTRAINT")} " +
                $"{Escape(drop.Name)} IF EXISTS"),
        ],
        CreateNodeTypeOperation or CreateRelationTypeOperation or
        DropNodeTypeOperation or DropRelationTypeOperation or
        AddNodePropertyOperation or AddRelationPropertyOperation or
        DropNodePropertyOperation or DropRelationPropertyOperation or
        RenameNodePropertyOperation or RenameRelationPropertyOperation => [],
        _ => throw new NotSupportedException(
            $"Migration operation '{operation.GetType().Name}' is not supported by Neo4j."),
    };

    private static MigrationCommand Command(string text) => new(text, true);

    private static string Name(string prefix, string type, string property) =>
        $"nodal_{prefix}_{type}_{property}";

    private static string Escape(string identifier) =>
        $"`{identifier.Replace("`", "``", StringComparison.Ordinal)}`";
}
