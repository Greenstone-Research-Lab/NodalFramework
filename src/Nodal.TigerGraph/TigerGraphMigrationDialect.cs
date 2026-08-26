using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Nodal.Core.Migrations;

namespace Nodal.TigerGraph;

/// <summary>Compiles portable schema operations into one deterministic TigerGraph schema-change job.</summary>
public sealed partial class TigerGraphMigrationDialect : IGraphMigrationDialect
{
    private readonly string graphName;

    /// <summary>Initializes the dialect for one graph.</summary>
    public TigerGraphMigrationDialect(string graphName)
    {
        ValidateIdentifier(graphName, nameof(graphName));
        this.graphName = graphName;
    }

    /// <inheritdoc />
    public IReadOnlyList<MigrationCommand> Compile(IReadOnlyList<MigrationOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        if (operations.Count == 0)
        {
            return [];
        }

        var statements = operations.Select(CompileOperation).ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", statements))))
            .ToLowerInvariant()[..12];
        var jobName = $"nodal_{hash}";
        return
        [
            new MigrationCommand(
                $"CREATE SCHEMA_CHANGE JOB {jobName} FOR GRAPH {graphName} {{ {string.Join(" ", statements)} }}",
                false),
            new MigrationCommand($"RUN SCHEMA_CHANGE JOB {jobName}", false),
            new MigrationCommand($"DROP JOB {jobName}", false),
        ];
    }

    private static string CompileOperation(MigrationOperation operation) => operation switch
    {
        CreateNodeTypeOperation node => CompileNode(node),
        CreateRelationTypeOperation relation => CompileRelation(relation),
        CreateIndexOperation index =>
            $"ALTER VERTEX {Identifier(index.NodeType)} ADD INDEX " +
            $"{Identifier($"nodal_ix_{index.NodeType}_{index.PropertyName}")} ON ({Identifier(index.PropertyName)});",
        AddNodePropertyOperation property =>
            $"ALTER VERTEX {Identifier(property.NodeType)} ADD ATTRIBUTE " +
            $"({Identifier(property.Property.Name)} {MapType(property.Property.ClrType, primaryKey: false)});",
        AddRelationPropertyOperation property =>
            $"ALTER EDGE {Identifier(property.RelationType)} ADD ATTRIBUTE " +
            $"({Identifier(property.Property.Name)} {MapType(property.Property.ClrType, primaryKey: false)});",
        DropNodePropertyOperation property =>
            $"ALTER VERTEX {Identifier(property.NodeType)} DROP ATTRIBUTE " +
            $"({Identifier(property.PropertyName)});",
        DropRelationPropertyOperation property =>
            $"ALTER EDGE {Identifier(property.RelationType)} DROP ATTRIBUTE " +
            $"({Identifier(property.PropertyName)});",
        DropNodeTypeOperation node => $"DROP VERTEX {Identifier(node.NodeType)};",
        DropRelationTypeOperation relation => $"DROP EDGE {Identifier(relation.RelationType)};",
        CreateUniqueConstraintOperation => throw new NotSupportedException(
            "TigerGraph supports uniqueness through vertex primary IDs; arbitrary unique constraints are not supported."),
        DropIndexOperation index =>
            $"ALTER VERTEX {Identifier(index.NodeType)} DROP INDEX " +
            $"{Identifier($"nodal_ix_{index.NodeType}_{index.PropertyName}")};",
        DropSchemaObjectOperation => throw new NotSupportedException(
            "TigerGraph schema-object removal requires an owning vertex type. Use the typed DropIndex operation for secondary indexes."),
        DropUniqueConstraintOperation => throw new NotSupportedException(
            "TigerGraph does not expose arbitrary unique constraints beyond primary IDs."),
        CreatePropertyExistenceConstraintOperation or
        DropPropertyExistenceConstraintOperation or
        CreatePropertyTypeConstraintOperation or
        DropPropertyTypeConstraintOperation => throw new NotSupportedException(
            "TigerGraph does not expose portable property existence or type constraints."),
        RenameNodePropertyOperation or RenameRelationPropertyOperation => throw new NotSupportedException(
            "TigerGraph property rename requires a provider-specific backfill and is not implicit."),
        AlterNodePropertyTypeOperation or AlterRelationPropertyTypeOperation => throw new NotSupportedException(
            "TigerGraph property type alteration requires an explicit provider-specific backfill."),
        _ => throw new NotSupportedException(
            $"Migration operation '{operation.GetType().Name}' is not supported by TigerGraph."),
    };

    private static string CompileNode(CreateNodeTypeOperation operation)
    {
        var properties = operation.Properties ?? [];
        var keyType = MapType(operation.KeyClrType ?? typeof(string), primaryKey: true);
        var attributes = properties
            .Where(property => !string.Equals(property.Name, operation.KeyProperty, StringComparison.Ordinal))
            .Select(property => $"{Identifier(property.Name)} {MapType(property.ClrType, primaryKey: false)}")
            .ToArray();
        var suffix = attributes.Length == 0 ? string.Empty : $", {string.Join(", ", attributes)}";
        return $"ADD VERTEX {Identifier(operation.NodeType)} " +
            $"(PRIMARY_ID {Identifier(operation.KeyProperty)} {keyType}{suffix}) WITH primary_id_as_attribute=\"true\";";
    }

    private static string CompileRelation(CreateRelationTypeOperation operation)
    {
        var attributes = (operation.Properties ?? [])
            .Select(property => $"{Identifier(property.Name)} {MapType(property.ClrType, primaryKey: false)}")
            .ToArray();
        var suffix = attributes.Length == 0 ? string.Empty : $", {string.Join(", ", attributes)}";
        var direction = operation.Directed ? "DIRECTED" : "UNDIRECTED";
        return $"ADD {direction} EDGE {Identifier(operation.RelationType)} " +
            $"(FROM {Identifier(operation.SourceType)}, TO {Identifier(operation.TargetType)}{suffix});";
    }

    private static string MapType(Type type, bool primaryKey)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsEnum)
        {
            type = Enum.GetUnderlyingType(type);
        }

        if (type == typeof(string) || type == typeof(char) || type == typeof(Guid))
        {
            return "STRING";
        }

        if (type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) ||
            type == typeof(ushort) || type == typeof(int) || type == typeof(uint) ||
            type == typeof(long) || type == typeof(ulong))
        {
            return "INT";
        }

        if (!primaryKey && (type == typeof(float) || type == typeof(double) || type == typeof(decimal)))
        {
            return "DOUBLE";
        }

        if (!primaryKey && type == typeof(bool))
        {
            return "BOOL";
        }

        if (!primaryKey && (type == typeof(DateTime) || type == typeof(DateTimeOffset)))
        {
            return "DATETIME";
        }

        throw new NotSupportedException(
            $"CLR type '{type}' cannot be mapped to a TigerGraph {(primaryKey ? "primary ID" : "attribute")} type.");
    }

    private static string Identifier(string identifier)
    {
        ValidateIdentifier(identifier, nameof(identifier));
        return identifier;
    }

    private static void ValidateIdentifier(string identifier, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier, parameterName);
        if (!IdentifierPattern().IsMatch(identifier))
        {
            throw new ArgumentException($"'{identifier}' is not a valid TigerGraph identifier.", parameterName);
        }
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();
}
