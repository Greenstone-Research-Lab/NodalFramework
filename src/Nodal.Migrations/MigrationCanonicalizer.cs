using System.Text;
using Nodal.Core.Migrations;

namespace Nodal.Migrations;

/// <summary>
/// Produces a deterministic representation of provider-neutral migration operations
/// and provider-specific commands.
/// </summary>
internal static class MigrationCanonicalizer
{
    public static string Build(
        IReadOnlyList<MigrationOperation> operations,
        IReadOnlyList<MigrationCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(commands);

        var canonical = new StringBuilder();

        canonical.AppendLine("operations:v1");

        foreach (var operation in operations)
        {
            canonical.AppendLine(CanonicalOperation(operation));
        }

        canonical.AppendLine("commands:v1");

        foreach (var command in commands)
        {
            canonical.Append("command|")
                .Append(Token(command.Kind.ToString()))
                .Append('|')
                .Append(command.IsTransactional ? "transactional" : "non-transactional")
                .Append('|')
                .Append(Token(command.Text))
                .AppendLine();
        }

        return canonical.ToString();
    }

    private static string CanonicalOperation(MigrationOperation operation) =>
        operation switch
        {
            CreateNodeTypeOperation node =>
                string.Join('|',
                    "create-node",
                    Token(node.NodeType),
                    Token(node.KeyProperty),
                    Token(TypeName(node.KeyClrType)),
                    Properties(node.Properties)),

            CreateRelationTypeOperation relation =>
                string.Join('|',
                    "create-relation",
                    Token(relation.RelationType),
                    Token(relation.SourceType),
                    Token(relation.TargetType),
                    relation.Directed ? "directed" : "undirected",
                    Properties(relation.Properties)),

            CreateUniqueConstraintOperation constraint =>
                string.Join('|',
                    "create-unique",
                    Token(constraint.NodeType),
                    Token(constraint.PropertyName)),

            CreateIndexOperation index =>
                string.Join('|',
                    "create-index",
                    Token(index.NodeType),
                    Token(index.PropertyName)),

            DropNodeTypeOperation node =>
                string.Join('|',
                    "drop-node",
                    Token(node.NodeType)),

            DropRelationTypeOperation relation =>
                string.Join('|',
                    "drop-relation",
                    Token(relation.RelationType)),

            DropSchemaObjectOperation schemaObject =>
                string.Join('|',
                    "drop-schema-object",
                    Token(schemaObject.Name),
                    Token(schemaObject.Kind.ToString())),

            _ => throw new NotSupportedException(
                $"Migration operation '{operation.GetType().Name}' cannot be canonicalized.")
        };

    private static string Properties(IReadOnlyList<GraphSchemaProperty>? properties)
    {
        if (properties is null || properties.Count == 0)
        {
            return "properties:none";
        }

        var ordered = properties
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ThenBy(property => TypeName(property.ClrType), StringComparer.Ordinal);

        return "properties:" + string.Join(',',
            ordered.Select(property =>
                $"{Token(property.Name)}:{Token(TypeName(property.ClrType))}"));
    }

    private static string TypeName(Type? type) =>
        type?.AssemblyQualifiedName
        ?? type?.FullName
        ?? "null";

    private static string Token(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
}
