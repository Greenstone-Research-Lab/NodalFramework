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

        if (query.CycleBehavior == GraphCycleBehavior.SimplePath &&
            query.Traversals.Any(traversal => traversal.Optional))
        {
            throw new NotSupportedException("A simple path cannot contain an optional traversal.");
        }

        var builder = new StringBuilder()
            .Append(query.CycleBehavior == GraphCycleBehavior.SimplePath ? "MATCH `nodalPath` = (" : "MATCH (")
            .Append(Escape(query.Alias))
            .Append(':')
            .Append(Escape(query.NodeType))
            .Append(')');

        var hasOptionalTraversal = query.Traversals.Any(traversal => traversal.Optional);
        if (hasOptionalTraversal && query.Predicate is not null)
        {
            builder.Append(" WHERE ").Append(RenderPredicate(query.Predicate, query.Alias));
        }

        foreach (var traversal in query.Traversals)
        {
            if (hasOptionalTraversal)
            {
                builder.Append(traversal.Optional ? " OPTIONAL MATCH (" : " MATCH (")
                    .Append(Escape(traversal.SourceAlias))
                    .Append(')')
                    .Append(RenderTraversal(traversal));
                var traversalFilters = new List<string>();
                if (traversal.Predicate is not null)
                {
                    traversalFilters.Add(RenderPredicate(traversal.Predicate, traversal.TargetAlias));
                }
                if (traversal.RelationPredicate is not null)
                {
                    traversalFilters.Add(RenderPredicate(traversal.RelationPredicate, traversal.RelationAlias));
                }
                if (traversalFilters.Count > 0)
                {
                    builder.Append(" WHERE ").Append(string.Join(" AND ", traversalFilters));
                }
            }
            else
            {
                builder.Append(RenderTraversal(traversal));
            }
        }

        var filters = new List<string>();
        if (query.CycleBehavior == GraphCycleBehavior.SimplePath)
        {
            filters.Add("all(`nodalVertex` IN nodes(`nodalPath`) WHERE single(`nodalCandidate` IN nodes(`nodalPath`) WHERE `nodalCandidate` = `nodalVertex`))");
        }
        if (!hasOptionalTraversal && query.Predicate is not null)
        {
            filters.Add(RenderPredicate(query.Predicate, query.Alias));
        }

        if (!hasOptionalTraversal)
        {
            filters.AddRange(query.Traversals
                .Where(traversal => traversal.Predicate is not null)
                .Select(traversal => RenderPredicate(traversal.Predicate!, traversal.TargetAlias)));
            filters.AddRange(query.Traversals
                .Where(traversal => traversal.RelationPredicate is not null)
                .Select(traversal => RenderPredicate(traversal.RelationPredicate!, traversal.RelationAlias)));
        }
        if (filters.Count > 0)
        {
            builder.Append(" WHERE ").Append(string.Join(" AND ", filters));
        }

        builder.Append(" RETURN ");
        if (query.Distinct && query.Projection != GraphQueryProjection.Count)
        {
            builder.Append("DISTINCT ");
        }
        if (query.Projection == GraphQueryProjection.Count)
        {
            builder.Append("count(");
            if (query.Distinct)
            {
                builder.Append("DISTINCT ");
            }
            builder.Append(Escape(query.ResultAlias)).Append(") AS `nodal_count`");
        }
        else if (query.Projection == GraphQueryProjection.Path)
        {
            var last = query.Traversals[^1];
            builder.Append(Escape(last.SourceAlias))
                .Append(", ")
                .Append(Escape(last.RelationAlias))
                .Append(", ")
                .Append(Escape(last.TargetAlias));
        }
        else if (query.Projection == GraphQueryProjection.Subgraph)
        {
            builder.Append(string.Join(", ",
                new[] { query.Alias }
                    .Concat(query.Traversals.SelectMany(step => new[] { step.RelationAlias, step.TargetAlias }))
                    .Select(Escape)));
        }
        else
        {
            builder.Append(Escape(query.ResultAlias));
        }
        if (query.Projection != GraphQueryProjection.Count && query.EffectiveOrderings.Count > 0)
        {
            builder.Append(" ORDER BY ").Append(string.Join(", ", query.EffectiveOrderings.Select(RenderOrdering)));
        }
        if (query.Offset is not null)
        {
            builder.Append(" SKIP ").Append(query.Offset.Value);
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
        GraphNotPredicate not => $"NOT ({RenderPredicate(not.Operand, alias)})",
        GraphNullPredicate nullCheck =>
            $"{Escape(alias)}.{Escape(nullCheck.PropertyName)} IS {(nullCheck.IsNull ? string.Empty : "NOT ")}NULL",
        GraphStringPredicate text => RenderStringPredicate(text, alias),
        GraphInPredicate membership =>
            $"{Escape(alias)}.{Escape(membership.PropertyName)} {(membership.Negated ? "NOT " : string.Empty)}IN ${membership.ParameterName}",
        _ => throw new NotSupportedException($"Predicate '{predicate.GetType().Name}' is not supported by Neo4j."),
    };

    private static string RenderTraversal(GraphTraversalStep traversal)
    {
        if (traversal.MinDepth < 0 || traversal.MaxDepth < traversal.MinDepth)
        {
            throw new ArgumentOutOfRangeException(nameof(traversal), "Traversal depth bounds are invalid.");
        }
        var depth = traversal.MinDepth == 1 && traversal.MaxDepth == 1
            ? string.Empty
            : $"*{traversal.MinDepth}..{traversal.MaxDepth}";
        var relationTypes = string.Join("|", traversal.RelationTypes.Select(Escape));
        var relation = $"[{Escape(traversal.RelationAlias)}:{relationTypes}{depth}]";
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

    private static string RenderStringPredicate(GraphStringPredicate predicate, string alias)
    {
        var property = $"{Escape(alias)}.{Escape(predicate.PropertyName)}";
        return predicate.Operator switch
        {
            GraphStringOperator.StartsWith => $"{property} STARTS WITH ${predicate.ParameterName}",
            GraphStringOperator.Contains => $"{property} CONTAINS ${predicate.ParameterName}",
            GraphStringOperator.EndsWith => $"{property} ENDS WITH ${predicate.ParameterName}",
            _ => throw new ArgumentOutOfRangeException(nameof(predicate), predicate.Operator, null),
        };
    }

    private static string RenderOrdering(GraphOrdering ordering) =>
        $"{Escape(ordering.Alias)}.{Escape(ordering.PropertyName)} " +
        (ordering.Direction == GraphSortDirection.Ascending ? "ASC" : "DESC");

    private static string Escape(string identifier) => $"`{identifier.Replace("`", "``", StringComparison.Ordinal)}`";
}
