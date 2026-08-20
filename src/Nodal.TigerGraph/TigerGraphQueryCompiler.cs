using System.Text;
using System.Text.RegularExpressions;
using Nodal.Core.Providers;
using Nodal.Core.Query;

namespace Nodal.TigerGraph;

/// <summary>
/// Compiles Nodal query models into parameterized interpreted GSQL queries.
/// </summary>
/// <remarks>
/// Interpreted queries provide the development-time execution path. A subsequent
/// compiled-query layer can install the same generated query and invoke its REST++ endpoint.
/// </remarks>
public sealed partial class TigerGraphQueryCompiler : IGraphQueryCompiler
{
    private readonly string graphName;

    /// <summary>
    /// Initializes a compiler for a specific TigerGraph graph.
    /// </summary>
    /// <param name="graphName">The target graph name.</param>
    public TigerGraphQueryCompiler(string graphName)
    {
        ValidateIdentifier(graphName, nameof(graphName));
        this.graphName = graphName;
    }

    /// <inheritdoc />
    public GraphCommand Compile(GraphQueryModel query)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateIdentifier(query.NodeType, nameof(query.NodeType));
        ValidateIdentifier(query.Alias, nameof(query.Alias));
        foreach (var traversal in query.Traversals)
        {
            ValidateIdentifier(traversal.RelationType, nameof(traversal.RelationType));
            ValidateIdentifier(traversal.TargetNodeType, nameof(traversal.TargetNodeType));
            ValidateIdentifier(traversal.RelationAlias, nameof(traversal.RelationAlias));
            ValidateIdentifier(traversal.TargetAlias, nameof(traversal.TargetAlias));
        }

        var declaration = string.Join(", ", query.Parameters.Select(
            parameter => $"{MapType(parameter.ClrType)} {parameter.Name}"));

        var builder = new StringBuilder()
            .Append("INTERPRET QUERY (")
            .Append(declaration)
            .Append(") FOR GRAPH ")
            .Append(graphName)
            .Append(" { ");

        if (query.Projection == GraphQueryProjection.Path)
        {
            builder.Append("ListAccum<EDGE> @@nodal_relations; ")
                .Append(RenderSelection(query, "nodal_sources", query.Alias, collectRelations: true))
                .Append(RenderSelection(query, "nodal_targets", query.ResultAlias, collectRelations: false))
                .Append("PRINT nodal_sources, @@nodal_relations AS nodal_relations, nodal_targets; }");
        }
        else
        {
            builder.Append(RenderSelection(query, "result", query.ResultAlias, collectRelations: false))
                .Append("PRINT result; }");
        }

        return new GraphCommand(
            builder.ToString(),
            query.Parameters.ToDictionary(parameter => parameter.Name, parameter => NormalizeValue(parameter.Value)));
    }

    private static string RenderSelection(
        GraphQueryModel query,
        string resultName,
        string selectedAlias,
        bool collectRelations)
    {
        var builder = new StringBuilder()
            .Append(resultName)
            .Append(" = SELECT ")
            .Append(selectedAlias)
            .Append(" FROM ")
            .Append(query.NodeType)
            .Append(':')
            .Append(query.Alias);

        foreach (var traversal in query.Traversals)
        {
            builder.Append(RenderTraversal(traversal));
        }

        var filters = new List<string>();
        if (query.Predicate is not null)
        {
            filters.Add(RenderPredicate(query.Predicate, query.Alias));
        }

        filters.AddRange(query.Traversals
            .Where(traversal => traversal.Predicate is not null)
            .Select(traversal => RenderPredicate(traversal.Predicate!, traversal.TargetAlias)));
        filters.AddRange(query.Traversals
            .Where(traversal => traversal.RelationPredicate is not null)
            .Select(traversal => RenderPredicate(traversal.RelationPredicate!, traversal.RelationAlias)));
        if (filters.Count > 0)
        {
            builder.Append(" WHERE ").Append(string.Join(" AND ", filters));
        }

        if (collectRelations)
        {
            var last = query.Traversals[^1];
            builder.Append(" ACCUM @@nodal_relations += ")
                .Append(last.RelationAlias);
        }

        if (query.Limit is not null)
        {
            builder.Append(" LIMIT ").Append(query.Limit.Value);
        }

        return builder.Append("; ").ToString();
    }

    private static string RenderPredicate(GraphPredicate predicate, string alias) => predicate switch
    {
        GraphComparisonPredicate comparison =>
            $"{alias}.{comparison.PropertyName} {RenderOperator(comparison.Operator)} {comparison.ParameterName}",
        GraphLogicalPredicate logical =>
            $"({RenderPredicate(logical.Left, alias)} {RenderLogicalOperator(logical.Operator)} {RenderPredicate(logical.Right, alias)})",
        _ => throw new NotSupportedException($"Predicate '{predicate.GetType().Name}' is not supported by TigerGraph."),
    };

    private static string RenderTraversal(GraphTraversalStep traversal)
    {
        var relation = $"({traversal.RelationType}:{traversal.RelationAlias})";
        var target = $" {traversal.TargetNodeType}:{traversal.TargetAlias}";
        return traversal.Direction switch
        {
            GraphTraversalDirection.Outgoing => $" -{relation}->" + target,
            GraphTraversalDirection.Incoming => $" <-{relation}-" + target,
            GraphTraversalDirection.Undirected => $" -{relation}-" + target,
            _ => throw new ArgumentOutOfRangeException(nameof(traversal), traversal.Direction, null),
        };
    }

    private static string RenderOperator(GraphComparisonOperator comparison) => comparison switch
    {
        GraphComparisonOperator.Equal => "==",
        GraphComparisonOperator.NotEqual => "!=",
        GraphComparisonOperator.GreaterThan => ">",
        GraphComparisonOperator.GreaterThanOrEqual => ">=",
        GraphComparisonOperator.LessThan => "<",
        GraphComparisonOperator.LessThanOrEqual => "<=",
        _ => throw new ArgumentOutOfRangeException(nameof(comparison), comparison, null),
    };

    private static string RenderLogicalOperator(GraphLogicalOperator logical) => logical switch
    {
        GraphLogicalOperator.And => "AND",
        GraphLogicalOperator.Or => "OR",
        _ => throw new ArgumentOutOfRangeException(nameof(logical), logical, null),
    };

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

        if (type == typeof(bool))
        {
            return "BOOL";
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

        if (type == typeof(DateTime) || type == typeof(DateTimeOffset))
        {
            return "DATETIME";
        }

        throw new NotSupportedException($"CLR type '{type}' cannot be used as a TigerGraph query parameter.");
    }

    private static object? NormalizeValue(object? value) => value switch
    {
        Guid guid => guid.ToString("D"),
        DateTimeOffset dateTime => dateTime.UtcDateTime,
        Enum enumeration => Convert.ToInt64(enumeration, System.Globalization.CultureInfo.InvariantCulture),
        _ => value,
    };

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
