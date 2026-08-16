using System.Text;
using Nodal.Core.Providers;
using Nodal.Core.Query;

namespace Nodal.Neo4j;

/// <summary>
/// Compiles Nodal query models into parameterized Cypher commands.
/// </summary>
public sealed class Neo4jQueryCompiler : IGraphQueryCompiler
{
    /// <inheritdoc />
    public GraphCommand Compile(GraphQueryModel query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var builder = new StringBuilder()
            .Append("MATCH (")
            .Append(Escape(query.Alias))
            .Append(':')
            .Append(Escape(query.NodeType))
            .Append(')');

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

        builder.Append(" RETURN ");
        if (query.Projection == GraphQueryProjection.Path)
        {
            var last = query.Traversals[^1];
            builder.Append(Escape(last.SourceAlias))
                .Append(", ")
                .Append(Escape(last.RelationAlias))
                .Append(", ")
                .Append(Escape(last.TargetAlias));
        }
        else
        {
            builder.Append(Escape(query.ResultAlias));
        }
        if (query.Limit is not null)
        {
            builder.Append(" LIMIT ").Append(query.Limit.Value);
        }

        return new GraphCommand(
            builder.ToString(),
            query.Parameters.ToDictionary(parameter => parameter.Name, parameter => parameter.Value));
    }

    private static string RenderPredicate(GraphPredicate predicate, string alias) => predicate switch
    {
        GraphComparisonPredicate comparison =>
            $"{Escape(alias)}.{Escape(comparison.PropertyName)} {RenderOperator(comparison.Operator)} ${comparison.ParameterName}",
        GraphLogicalPredicate logical =>
            $"({RenderPredicate(logical.Left, alias)} {RenderLogicalOperator(logical.Operator)} {RenderPredicate(logical.Right, alias)})",
        _ => throw new NotSupportedException($"Predicate '{predicate.GetType().Name}' is not supported by Neo4j."),
    };

    private static string RenderTraversal(GraphTraversalStep traversal)
    {
        var relation = $"[{Escape(traversal.RelationAlias)}:{Escape(traversal.RelationType)}]";
        var target = $"({Escape(traversal.TargetAlias)}:{Escape(traversal.TargetNodeType)})";
        return traversal.Direction switch
        {
            GraphTraversalDirection.Outgoing => $"-{relation}->{target}",
            GraphTraversalDirection.Incoming => $"<-{relation}-{target}",
            GraphTraversalDirection.Undirected => $"-{relation}-{target}",
            _ => throw new ArgumentOutOfRangeException(nameof(traversal), traversal.Direction, null),
        };
    }

    private static string RenderOperator(GraphComparisonOperator comparison) => comparison switch
    {
        GraphComparisonOperator.Equal => "=",
        GraphComparisonOperator.NotEqual => "<>",
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

    private static string Escape(string identifier) => $"`{identifier.Replace("`", "``", StringComparison.Ordinal)}`";
}
