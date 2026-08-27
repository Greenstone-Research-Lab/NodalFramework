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

        if (query.SetOperation is not null)
        {
            return CompileSetOperation(query, query.SetOperation);
        }

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

        foreach (var pattern in query.EffectiveMatchPatterns)
        {
            builder.Append(" MATCH (")
                .Append(Escape(pattern.SourceAlias))
                .Append(')')
                .Append(RenderTraversal(pattern));
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
        filters.AddRange(query.EffectiveMatchPatterns
            .Where(pattern => pattern.Predicate is not null)
            .Select(pattern => RenderPredicate(pattern.Predicate!, pattern.TargetAlias)));
        filters.AddRange(query.EffectiveMatchPatterns
            .Where(pattern => pattern.RelationPredicate is not null)
            .Select(pattern => RenderPredicate(pattern.RelationPredicate!, pattern.RelationAlias)));
        filters.AddRange(query.EffectiveExistencePatterns.Select(RenderExistencePattern));
        if (filters.Count > 0)
        {
            builder.Append(" WHERE ").Append(string.Join(" AND ", filters));
        }

        var requiresAggregateStage = query.Projection == GraphQueryProjection.Row &&
            query.RowProjection?.EffectiveHavingPredicates.Count > 0;
        if (!requiresAggregateStage)
        {
            builder.Append(" RETURN ");
        }
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
                    .Concat(query.EffectiveMatchPatterns.SelectMany(step => new[] { step.RelationAlias, step.TargetAlias }))
                    .Select(Escape)));
        }
        else if (query.Projection == GraphQueryProjection.Row)
        {
            RenderRowProjection(builder, query);
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

    private GraphCommand CompileSetOperation(GraphQueryModel query, GraphSetOperation operation)
    {
        if (query.Projection != GraphQueryProjection.Node ||
            operation.Left.Projection != GraphQueryProjection.Node ||
            operation.Right.Projection != GraphQueryProjection.Node ||
            operation.Left.ResultNodeType != query.ResultNodeType ||
            operation.Right.ResultNodeType != query.ResultNodeType ||
            operation.Left.ResultAlias != query.Alias ||
            operation.Right.ResultAlias != query.Alias)
        {
            throw new NotSupportedException("Neo4j set operations require compatible node projections with one shared result alias.");
        }

        var left = Compile(operation.Left);
        var right = Compile(operation.Right);
        var keyword = operation.Kind switch
        {
            GraphSetOperationKind.Union => "UNION",
            GraphSetOperationKind.UnionAll => "UNION ALL",
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation.Kind, null),
        };
        var builder = new StringBuilder()
            .Append("CALL { ")
            .Append(left.Text)
            .Append(' ')
            .Append(keyword)
            .Append(' ')
            .Append(right.Text)
            .Append(" } RETURN ")
            .Append(Escape(query.Alias));
        if (query.EffectiveOrderings.Count > 0)
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

        var parameters = left.Parameters.Concat(right.Parameters)
            .ToDictionary(parameter => parameter.Key, parameter => parameter.Value, StringComparer.Ordinal);
        return new GraphCommand(builder.ToString(), parameters);
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

    private static string RenderExistencePattern(GraphExistencePattern pattern)
    {
        var relation = $"[{Escape(pattern.RelationAlias)}:{Escape(pattern.RelationType)}]";
        var target = $"({Escape(pattern.TargetAlias)}:{Escape(pattern.TargetNodeType)})";
        var traversal = pattern.Direction switch
        {
            GraphTraversalDirection.Outgoing => $"-{relation}->{target}",
            GraphTraversalDirection.Incoming => $"<-{relation}-{target}",
            GraphTraversalDirection.Undirected => $"-{relation}-{target}",
            _ => throw new ArgumentOutOfRangeException(nameof(pattern), pattern.Direction, null),
        };
        var predicates = new[]
        {
            pattern.TargetPredicate is null ? null : RenderPredicate(pattern.TargetPredicate, pattern.TargetAlias),
            pattern.RelationPredicate is null ? null : RenderPredicate(pattern.RelationPredicate, pattern.RelationAlias),
        }.Where(predicate => predicate is not null);
        var query = new StringBuilder()
            .Append("EXISTS { MATCH (")
            .Append(Escape(pattern.SourceAlias))
            .Append(')')
            .Append(traversal);
        if (predicates.Any())
        {
            query.Append(" WHERE ").Append(string.Join(" AND ", predicates));
        }
        query.Append(" }");
        return pattern.Negated ? $"NOT {query}" : query.ToString();
    }

    private static GraphRowProjection RequireRowProjection(GraphQueryModel query) => query.RowProjection is { Columns.Count: > 0 } projection
        ? projection
        : throw new InvalidOperationException("A row projection requires at least one projected column.");

    private static void RenderRowProjection(StringBuilder builder, GraphQueryModel query)
    {
        var projection = RequireRowProjection(query);
        var columns = string.Join(", ", projection.Columns.Select(RenderRowColumn));
        if (projection.EffectiveHavingPredicates.Count > 0)
        {
            builder.Append(" WITH ").Append(columns)
                .Append(" WHERE ")
                .Append(string.Join(" AND ", projection.EffectiveHavingPredicates.Select(RenderRowPredicate)))
                .Append(" RETURN ")
                .Append(string.Join(", ", projection.Columns.Select(column =>
                    $"{Escape(column.Name)} AS {Escape(column.Name)}")));
        }
        else
        {
            builder.Append(columns);
        }

        if (projection.EffectiveOrderings.Count > 0)
        {
            builder.Append(" ORDER BY ")
                .Append(string.Join(", ", projection.EffectiveOrderings.Select(RenderRowOrdering)));
        }
    }

    private static string RenderRowColumn(GraphRowColumn column)
    {
        var source = Escape(column.SourceAlias);
        var property = column.PropertyName is null ? source : $"{source}.{Escape(column.PropertyName)}";
        var expression = column.Kind switch
        {
            GraphRowColumnKind.Property => property,
            GraphRowColumnKind.Count => $"count({(column.Distinct ? "DISTINCT " : string.Empty)}{source})",
            GraphRowColumnKind.Sum => $"sum({property})",
            GraphRowColumnKind.Average => $"avg({property})",
            GraphRowColumnKind.Minimum => $"min({property})",
            GraphRowColumnKind.Maximum => $"max({property})",
            _ => throw new ArgumentOutOfRangeException(nameof(column), column.Kind, null),
        };
        return $"{expression} AS {Escape(column.Name)}";
    }

    private static string RenderRowPredicate(GraphRowPredicate predicate) =>
        $"{Escape(predicate.ColumnName)} {RenderOperator(predicate.Operator)} ${predicate.ParameterName}";

    private static string RenderRowOrdering(GraphRowOrdering ordering) =>
        $"{Escape(ordering.ColumnName)} " +
        (ordering.Direction == GraphSortDirection.Ascending ? "ASC" : "DESC");

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
