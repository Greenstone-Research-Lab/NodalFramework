using Nodal.Core.Migrations;

namespace Nodal.Neo4j;

/// <summary>Compiles portable schema intent into idempotent Neo4j Cypher DDL.</summary>
public sealed class Neo4jMigrationDialect : IGraphMigrationDialect
{
    private readonly bool enterpriseSchemaConstraintsEnabled;

    /// <summary>Initializes the dialect with explicit Neo4j edition capabilities.</summary>
    public Neo4jMigrationDialect(bool enterpriseSchemaConstraintsEnabled = false)
    {
        this.enterpriseSchemaConstraintsEnabled = enterpriseSchemaConstraintsEnabled;
    }

    /// <inheritdoc />
    public IReadOnlyList<MigrationCommand> Compile(IReadOnlyList<MigrationOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        return operations.SelectMany(CompileOperation).ToArray();
    }

    private IEnumerable<MigrationCommand> CompileOperation(MigrationOperation operation) => operation switch
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
        CreatePropertyExistenceConstraintOperation constraint =>
        [Command(CreateExistenceConstraint(constraint))],
        DropPropertyExistenceConstraintOperation constraint =>
        [Command(DropEnterpriseConstraint("exists", constraint.EntityKind, constraint.EntityType, constraint.PropertyName))],
        CreatePropertyTypeConstraintOperation constraint =>
        [Command(CreateTypeConstraint(constraint))],
        DropPropertyTypeConstraintOperation constraint =>
        [Command(DropEnterpriseConstraint("type", constraint.EntityKind, constraint.EntityType, constraint.PropertyName))],
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

    private string CreateExistenceConstraint(CreatePropertyExistenceConstraintOperation constraint)
    {
        EnsureEnterpriseConstraints();
        var (pattern, variable) = Pattern(constraint.EntityKind, constraint.EntityType);
        return $"CREATE CONSTRAINT {Escape(ConstraintName("exists", constraint.EntityKind, constraint.EntityType, constraint.PropertyName))} " +
            $"IF NOT EXISTS FOR {pattern} REQUIRE {variable}.{Escape(constraint.PropertyName)} IS NOT NULL";
    }

    private string CreateTypeConstraint(CreatePropertyTypeConstraintOperation constraint)
    {
        EnsureEnterpriseConstraints();
        var (pattern, variable) = Pattern(constraint.EntityKind, constraint.EntityType);
        return $"CREATE CONSTRAINT {Escape(ConstraintName("type", constraint.EntityKind, constraint.EntityType, constraint.PropertyName))} " +
            $"IF NOT EXISTS FOR {pattern} REQUIRE {variable}.{Escape(constraint.PropertyName)} IS :: {StorageType(constraint.ClrType)}";
    }

    private void EnsureEnterpriseConstraints()
    {
        if (!enterpriseSchemaConstraintsEnabled)
        {
            throw new NotSupportedException(
                "Neo4j property-existence and property-type constraints require Neo4j Enterprise Edition. " +
                "Enable EnterpriseSchemaConstraintsEnabled only for a compatible server.");
        }
    }

    private string DropEnterpriseConstraint(
        string prefix,
        GraphSchemaEntityKind kind,
        string type,
        string property)
    {
        EnsureEnterpriseConstraints();
        return $"DROP CONSTRAINT {Escape(ConstraintName(prefix, kind, type, property))} IF EXISTS";
    }

    private static (string Pattern, string Variable) Pattern(GraphSchemaEntityKind kind, string type) => kind switch
    {
        GraphSchemaEntityKind.Node => ($"(`node`:{Escape(type)})", "`node`"),
        GraphSchemaEntityKind.Relation => ($"()-[`relation`:{Escape(type)}]-()", "`relation`"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown graph schema entity kind."),
    };

    private static string StorageType(Type clrType)
    {
        ArgumentNullException.ThrowIfNull(clrType);
        var type = Nullable.GetUnderlyingType(clrType) ?? clrType;
        if (type.IsEnum || type == typeof(string) || type == typeof(char) || type == typeof(Guid) || type == typeof(Uri))
        {
            return "STRING";
        }

        if (type == typeof(bool)) return "BOOLEAN";
        if (type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) ||
            type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong)) return "INTEGER";
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) return "FLOAT";
        if (type == typeof(DateOnly)) return "DATE";
        if (type == typeof(TimeOnly)) return "LOCAL TIME";
        if (type == typeof(DateTime)) return "LOCAL DATETIME";
        if (type == typeof(DateTimeOffset)) return "ZONED DATETIME";
        if (type == typeof(TimeSpan)) return "DURATION";

        throw new NotSupportedException($"CLR type '{clrType}' has no supported Neo4j property-type constraint mapping.");
    }

    private static string ConstraintName(string prefix, GraphSchemaEntityKind kind, string type, string property) =>
        Name(prefix, kind == GraphSchemaEntityKind.Node ? "node" : "relation", type, property);

    // Neo4j cannot combine schema modifications with graph writes (including
    // the Nodal history node) in one transaction. The executor therefore
    // commits the schema batch first and records its recoverable state second.
    private static MigrationCommand Command(string text) => new(text, false);

    private static string Name(string prefix, params string[] parts) =>
        $"nodal_{prefix}_{string.Join('_', parts)}";

    private static string Escape(string identifier) =>
        $"`{identifier.Replace("`", "``", StringComparison.Ordinal)}`";
}
