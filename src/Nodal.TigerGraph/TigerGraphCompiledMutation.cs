using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Nodal.Core.ChangeTracking;
using Nodal.Core.Mutations;

namespace Nodal.TigerGraph;

/// <summary>
/// Represents a deterministic installed-query definition and the values supplied to one execution.
/// </summary>
internal sealed record TigerGraphCompiledMutation(
    string QueryName,
    string Definition,
    IReadOnlyDictionary<string, string> Parameters);

/// <summary>
/// Compiles one mutation-plan shape into a reusable, parameterized GSQL transaction.
/// </summary>
internal sealed partial class TigerGraphMutationCompiler
{
    private readonly string graphName;

    public TigerGraphMutationCompiler(string graphName)
    {
        ValidateIdentifier(graphName, nameof(graphName));
        this.graphName = graphName;
    }

    public TigerGraphCompiledMutation Compile(GraphMutationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        var declarations = new List<string>();
        var statements = new List<string>();
        var shape = new StringBuilder();
        var parameterIndex = 0;

        foreach (var operation in plan.Operations)
        {
            switch (operation)
            {
                case CreateNodeOperation create:
                    CompileNodeUpsert(create.Identity, create.Properties);
                    break;
                case UpdateNodeOperation update:
                    CompileNodeUpsert(update.Identity, update.Properties);
                    break;
                case DeleteNodeOperation delete:
                    CompileNodeDelete(delete.Identity);
                    break;
                case CreateRelationOperation create:
                    CompileRelationUpsert(create.Source, create.RelationType, create.Target, create.Properties);
                    break;
                case UpdateRelationOperation update:
                    CompileRelationUpsert(update.Source, update.RelationType, update.Target, update.Properties);
                    break;
                case DeleteRelationOperation delete:
                    CompileRelationDelete(delete.Source, delete.RelationType, delete.Target, delete.Directed);
                    break;
                default:
                    throw new NotSupportedException(
                        $"TigerGraph operation '{operation.GetType().Name}' cannot be compiled.");
            }
        }

        var shapeHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(shape.ToString()))).ToLowerInvariant()[..16];
        var queryName = $"nodal_apply_mutations_{shapeHash}";
        var definition = $"CREATE OR REPLACE QUERY {queryName}({string.Join(", ", declarations)}) " +
            $"FOR GRAPH {graphName} SYNTAX v2 {{ {string.Join(" ", statements)} PRINT \"ok\" AS status; }}";
        return new TigerGraphCompiledMutation(queryName, definition, parameters);

        string AddParameter(object? value)
        {
            if (value is null)
            {
                throw new NotSupportedException(
                    "A null property cannot be used in a compiled TigerGraph mutation because its GSQL type is unknown.");
            }

            var parameterName = $"p{parameterIndex++}";
            var type = MapType(value.GetType());
            declarations.Add($"{type} {parameterName}");
            parameters.Add(parameterName, FormatValue(value));
            shape.Append(type).Append(':').Append(parameterName).Append('|');
            return parameterName;
        }

        void CompileNodeUpsert(GraphIdentity identity, IReadOnlyDictionary<string, object?> properties)
        {
            var nodeType = Identifier(identity.NodeType);
            var key = AddParameter(identity.Value);
            var attributes = properties
                .Where(property => !string.Equals(property.Key, identity.KeyProperty, StringComparison.Ordinal))
                .OrderBy(property => property.Key, StringComparer.Ordinal)
                .ToArray();
            var columns = new List<string> { "PRIMARY_ID" };
            var values = new List<string> { key };
            foreach (var property in attributes)
            {
                columns.Add(Identifier(property.Key));
                values.Add(AddParameter(property.Value));
            }

            statements.Add($"INSERT INTO {nodeType} ({string.Join(", ", columns)}) " +
                $"VALUES ({string.Join(", ", values)});");
            shape.Append("node-upsert:").Append(nodeType).Append(':')
                .Append(Identifier(identity.KeyProperty)).Append(':')
                .AppendJoin(',', columns).Append(';');
        }

        void CompileNodeDelete(GraphIdentity identity)
        {
            var nodeType = Identifier(identity.NodeType);
            var keyProperty = Identifier(identity.KeyProperty);
            var key = AddParameter(identity.Value);
            statements.Add($"DELETE n FROM {nodeType}:n WHERE n.{keyProperty} == {key};");
            shape.Append("node-delete:").Append(nodeType).Append(':').Append(keyProperty).Append(';');
        }

        void CompileRelationUpsert(
            GraphIdentity source,
            string relationType,
            GraphIdentity target,
            IReadOnlyDictionary<string, object?> properties)
        {
            var edgeType = Identifier(relationType);
            var sourceType = Identifier(source.NodeType);
            var targetType = Identifier(target.NodeType);
            var sourceKey = AddParameter(source.Value);
            var targetKey = AddParameter(target.Value);
            var attributes = properties.OrderBy(property => property.Key, StringComparer.Ordinal).ToArray();
            var columns = new List<string> { "FROM", "TO" };
            var values = new List<string> { $"{sourceKey} {sourceType}", $"{targetKey} {targetType}" };
            foreach (var property in attributes)
            {
                columns.Add(Identifier(property.Key));
                values.Add(AddParameter(property.Value));
            }

            statements.Add($"INSERT INTO {edgeType} ({string.Join(", ", columns)}) " +
                $"VALUES ({string.Join(", ", values)});");
            shape.Append("edge-upsert:").Append(sourceType).Append(':').Append(edgeType).Append(':')
                .Append(targetType).Append(':').AppendJoin(',', columns).Append(';');
        }

        void CompileRelationDelete(
            GraphIdentity source,
            string relationType,
            GraphIdentity target,
            bool directed)
        {
            var sourceType = Identifier(source.NodeType);
            var targetType = Identifier(target.NodeType);
            var edgeType = Identifier(relationType);
            var sourceProperty = Identifier(source.KeyProperty);
            var targetProperty = Identifier(target.KeyProperty);
            var sourceKey = AddParameter(source.Value);
            var targetKey = AddParameter(target.Value);
            var edge = directed ? $"-({edgeType}:e)->" : $"-({edgeType}:e)-";
            statements.Add($"DELETE e FROM {sourceType}:s {edge} {targetType}:t " +
                $"WHERE s.{sourceProperty} == {sourceKey} AND t.{targetProperty} == {targetKey};");
            shape.Append("edge-delete:").Append(sourceType).Append(':').Append(edgeType).Append(':')
                .Append(targetType).Append(':').Append(directed).Append(':')
                .Append(sourceProperty).Append(':').Append(targetProperty).Append(';');
        }
    }

    private static string MapType(Type type)
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

        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
        {
            return "DOUBLE";
        }

        if (type == typeof(bool))
        {
            return "BOOL";
        }

        if (type == typeof(DateTime) || type == typeof(DateTimeOffset))
        {
            return "DATETIME";
        }

        throw new NotSupportedException($"CLR type '{type}' cannot be used as a GSQL query parameter.");
    }

    private static string FormatValue(object value) => value switch
    {
        bool boolean => boolean ? "true" : "false",
        DateTime dateTime => dateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset dateTimeOffset => dateTimeOffset.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
        Guid guid => guid.ToString("D", CultureInfo.InvariantCulture),
        Enum enumeration => Convert.ToString(
            Convert.ChangeType(enumeration, Enum.GetUnderlyingType(enumeration.GetType()), CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture)!,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

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
