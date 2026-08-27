using System.Collections;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Nodal.Core.Query;

namespace Nodal.TigerGraph.Extensions;

/// <summary>Represents one deterministic installed GSQL query generated for a Nodal query shape.</summary>
public sealed record TigerGraphInstalledQueryDefinition(string Name, string Fingerprint, string Text);

/// <summary>Creates installed GSQL definitions for one correlated existence pattern.</summary>
public static partial class TigerGraphInstalledQueryDefinitionFactory
{
    /// <summary>Creates a parameterized installed query for one existence or anti-existence pattern.</summary>
    public static TigerGraphInstalledQueryDefinition CreateCorrelatedExistence(string graphName, GraphQueryModel query)
    {
        ValidateIdentifier(graphName, nameof(graphName));
        ArgumentNullException.ThrowIfNull(query);
        if (query.EffectiveExistencePatterns.Count != 1 || query.Traversals.Count != 0 || query.Projection != GraphQueryProjection.Node)
        {
            throw new NotSupportedException("The TigerGraph existence extension requires one existence pattern over an untraversed node query.");
        }

        var pattern = query.EffectiveExistencePatterns[0];
        ValidateIdentifier(query.NodeType, nameof(query));
        ValidateIdentifier(pattern.RelationType, nameof(query));
        ValidateIdentifier(pattern.TargetNodeType, nameof(query));
        var signature = string.Join(',', query.Parameters.Select(parameter => $"{parameter.Name}:{MapType(parameter.ClrType)}"));
        var shape = $"{query.NodeType}|{pattern.RelationType}|{pattern.TargetNodeType}|{pattern.Direction}|{pattern.Negated}|{signature}|{Describe(query.Predicate)}|{Describe(pattern.TargetPredicate)}|{Describe(pattern.RelationPredicate)}";
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(shape))).ToLowerInvariant()[..16];
        var name = $"nodal_exists_{fingerprint}";
        var parameters = string.Join(", ", query.Parameters.Select(parameter => $"{MapType(parameter.ClrType)} {parameter.Name}"));
        var source = "nodal_source";
        var target = "nodal_target";
        var relation = "nodal_relation";
        var sourceFilter = Render(query.Predicate, source);
        var matchFilter = Join(sourceFilter, Render(pattern.TargetPredicate, target), Render(pattern.RelationPredicate, relation));
        var traversal = RenderTraversal(pattern, relation, target);
        var antiFilter = Join(sourceFilter, $"NOT {source}.@nodal_match");
        var body = pattern.Negated
            ? $"OrAccum<BOOL> @nodal_match; matched = SELECT {source} FROM {query.NodeType}:{source}{traversal}{Where(matchFilter)} ACCUM {source}.@nodal_match = true; result = SELECT {source} FROM {query.NodeType}:{source}{Where(antiFilter)}; PRINT result;"
            : $"result = SELECT {source} FROM {query.NodeType}:{source}{traversal}{Where(matchFilter)}; PRINT result;";
        return new TigerGraphInstalledQueryDefinition(
            name,
            fingerprint,
            $"CREATE OR REPLACE QUERY {name}({parameters}) FOR GRAPH {graphName} {{ {body} }}\nINSTALL QUERY -FORCE {name}");
    }

    private static string RenderTraversal(GraphExistencePattern pattern, string relation, string target) => pattern.Direction switch
    {
        GraphTraversalDirection.Outgoing => $" -({pattern.RelationType}:{relation})-> {pattern.TargetNodeType}:{target}",
        GraphTraversalDirection.Incoming => $" <-({pattern.RelationType}:{relation})- {pattern.TargetNodeType}:{target}",
        GraphTraversalDirection.Undirected => $" -({pattern.RelationType}:{relation})- {pattern.TargetNodeType}:{target}",
        _ => throw new ArgumentOutOfRangeException(nameof(pattern)),
    };

    private static string? Render(GraphPredicate? predicate, string alias) => predicate switch
    {
        null => null,
        GraphComparisonPredicate value => $"{alias}.{value.PropertyName} {Operator(value.Operator)} {value.ParameterName}",
        GraphLogicalPredicate value => $"({Render(value.Left, alias)} {LogicalOperator(value.Operator)} {Render(value.Right, alias)})",
        GraphNotPredicate value => $"NOT ({Render(value.Operand, alias)})",
        GraphNullPredicate value => $"{alias}.{value.PropertyName} IS {(value.IsNull ? string.Empty : "NOT ")}NULL",
        GraphStringPredicate value => RenderString(value, alias),
        GraphInPredicate value => $"{alias}.{value.PropertyName} {(value.Negated ? "NOT " : string.Empty)}IN {value.ParameterName}",
        _ => throw new NotSupportedException($"Predicate '{predicate.GetType().Name}' is not supported by the TigerGraph existence extension."),
    };

    private static string Join(params string?[] filters) => string.Join(" AND ", filters.Where(value => !string.IsNullOrWhiteSpace(value))!);
    private static string Where(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : $" WHERE {value}";
    private static string Describe(GraphPredicate? value) => value switch
    {
        null => "none",
        GraphComparisonPredicate item => $"comparison:{item.PropertyName}:{item.Operator}:{item.ParameterName}",
        GraphLogicalPredicate item => $"logical:{Describe(item.Left)}:{item.Operator}:{Describe(item.Right)}",
        GraphNotPredicate item => $"not:{Describe(item.Operand)}",
        GraphNullPredicate item => $"null:{item.PropertyName}:{item.IsNull}",
        GraphStringPredicate item => $"string:{item.PropertyName}:{item.Operator}:{item.ParameterName}",
        GraphInPredicate item => $"in:{item.PropertyName}:{item.ParameterName}:{item.Negated}",
        _ => value.GetType().FullName ?? value.GetType().Name,
    };
    private static string Operator(GraphComparisonOperator value) => value switch { GraphComparisonOperator.Equal => "==", GraphComparisonOperator.NotEqual => "!=", GraphComparisonOperator.GreaterThan => ">", GraphComparisonOperator.GreaterThanOrEqual => ">=", GraphComparisonOperator.LessThan => "<", GraphComparisonOperator.LessThanOrEqual => "<=", _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    private static string LogicalOperator(GraphLogicalOperator value) => value switch { GraphLogicalOperator.And => "AND", GraphLogicalOperator.Or => "OR", _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    private static string RenderString(GraphStringPredicate value, string alias)
    {
        var parameter = value.Operator switch
        {
            GraphStringOperator.StartsWith => $"{value.ParameterName} + \"%\"",
            GraphStringOperator.Contains => $"\"%\" + {value.ParameterName} + \"%\"",
            GraphStringOperator.EndsWith => $"\"%\" + {value.ParameterName}",
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };
        return $"{alias}.{value.PropertyName} LIKE {parameter}";
    }
    private static string MapType(Type type)
    {
        if (type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type))
        {
            var elementType = type.IsArray ? type.GetElementType()! : type.IsGenericType ? type.GetGenericArguments()[0] : typeof(string);
            return $"SET<{MapType(elementType)}>";
        }
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsEnum) type = Enum.GetUnderlyingType(type);
        if (type == typeof(string) || type == typeof(char) || type == typeof(Guid)) return "STRING";
        if (type == typeof(bool)) return "BOOL";
        if (type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong)) return "INT";
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) return "DOUBLE";
        if (type == typeof(DateTime) || type == typeof(DateTimeOffset)) return "DATETIME";
        throw new NotSupportedException($"CLR type '{type}' cannot be used as a TigerGraph query parameter.");
    }
    private static void ValidateIdentifier(string value, string parameterName) { if (!IdentifierPattern().IsMatch(value)) throw new ArgumentException("Invalid TigerGraph identifier.", parameterName); }
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)] private static partial Regex IdentifierPattern();
}
