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
        var shape = $"{query.NodeType}|{pattern.RelationType}|{pattern.TargetNodeType}|{pattern.Direction}|{pattern.Negated}|{Describe(query.Predicate)}|{Describe(pattern.TargetPredicate)}|{Describe(pattern.RelationPredicate)}";
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
        return new TigerGraphInstalledQueryDefinition(name, fingerprint, $"CREATE QUERY {name}({parameters}) FOR GRAPH {graphName} {{ {body} }}\nINSTALL QUERY {name}");
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
        GraphLogicalPredicate value => $"({Render(value.Left, alias)} {value.Operator} {Render(value.Right, alias)})",
        GraphNotPredicate value => $"NOT ({Render(value.Operand, alias)})",
        GraphNullPredicate value => $"{alias}.{value.PropertyName} IS {(value.IsNull ? string.Empty : "NOT ")}NULL",
        GraphInPredicate value => $"{alias}.{value.PropertyName} {(value.Negated ? "NOT " : string.Empty)}IN {value.ParameterName}",
        _ => throw new NotSupportedException($"Predicate '{predicate.GetType().Name}' is not supported by the TigerGraph existence extension."),
    };

    private static string Join(params string?[] filters) => string.Join(" AND ", filters.Where(value => !string.IsNullOrWhiteSpace(value))!);
    private static string Where(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : $" WHERE {value}";
    private static string Describe(GraphPredicate? value) => value?.GetType().Name ?? "none";
    private static string Operator(GraphComparisonOperator value) => value switch { GraphComparisonOperator.Equal => "==", GraphComparisonOperator.NotEqual => "!=", GraphComparisonOperator.GreaterThan => ">", GraphComparisonOperator.GreaterThanOrEqual => ">=", GraphComparisonOperator.LessThan => "<", GraphComparisonOperator.LessThanOrEqual => "<=", _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    private static string MapType(Type type) => (Nullable.GetUnderlyingType(type) ?? type) == typeof(string) ? "STRING" : "INT";
    private static void ValidateIdentifier(string value, string parameterName) { if (!IdentifierPattern().IsMatch(value)) throw new ArgumentException("Invalid TigerGraph identifier.", parameterName); }
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)] private static partial Regex IdentifierPattern();
}
