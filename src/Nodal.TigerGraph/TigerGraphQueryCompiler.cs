using System.Text;
using System.Text.RegularExpressions;
using Nodal.Core.Providers;
using Nodal.Core.Query;
using Nodal.TigerGraph.Extensions;

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
    private readonly TigerGraphInstalledQueryCatalog? installedQueries;

    /// <summary>
    /// Initializes a compiler for a specific TigerGraph graph.
    /// </summary>
    /// <param name="graphName">The target graph name.</param>
    public TigerGraphQueryCompiler(string graphName)
        : this(graphName, null)
    {
    }

    internal TigerGraphQueryCompiler(string graphName, TigerGraphInstalledQueryCatalog? installedQueries)
    {
        ValidateIdentifier(graphName, nameof(graphName));
        this.graphName = graphName;
        this.installedQueries = installedQueries;
    }

    /// <inheritdoc />
    public GraphCommand Compile(GraphQueryModel query)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateIdentifier(query.NodeType, nameof(query.NodeType));
        ValidateIdentifier(query.Alias, nameof(query.Alias));
        if (query.SetOperation is not null)
        {
            throw new NotSupportedException(
                "TigerGraph interpreted GSQL does not provide a portable set-operation execution path. " +
                "Use an installed provider extension until Nodal supplies an installed-query implementation.");
        }
        if (query.EffectiveExistencePatterns.Count > 0)
        {
            if (installedQueries is null)
            {
                throw new NotSupportedException(
                    "TigerGraph interpreted GSQL does not provide a portable correlated-subquery execution path. " +
                    "Configure the correlated-existence installed-query extension and an administrative transport.");
            }

            var definition = TigerGraphInstalledQueryDefinitionFactory.CreateCorrelatedExistence(graphName, query);
            return new GraphCommand(
                string.Empty,
                query.Parameters.ToDictionary(parameter => parameter.Name, parameter => NormalizeValue(parameter.Value), StringComparer.Ordinal),
                installedQueries.Register(definition));
        }
        if (query.EffectiveMatchPatterns.Count > 0)
        {
            throw new NotSupportedException(
                "TigerGraph interpreted GSQL does not provide a portable multiple-pattern execution path. " +
                "Use an installed provider extension until Nodal supplies an installed-query implementation.");
        }
        foreach (var traversal in query.Traversals)
        {
            foreach (var relationType in traversal.RelationTypes)
            {
                ValidateIdentifier(relationType, nameof(traversal.RelationType));
            }
            ValidateIdentifier(traversal.TargetNodeType, nameof(traversal.TargetNodeType));
            ValidateIdentifier(traversal.RelationAlias, nameof(traversal.RelationAlias));
            ValidateIdentifier(traversal.TargetAlias, nameof(traversal.TargetAlias));
            if (traversal.Optional)
            {
                throw new NotSupportedException("TigerGraph interpreted GSQL does not provide optional-match semantics.");
            }
        }

        var useSyntaxV2 = query.Projection == GraphQueryProjection.Row ||
            query.Traversals.Any(traversal => traversal.MinDepth != 1 || traversal.MaxDepth != 1);
        if (useSyntaxV2 && query.CycleBehavior == GraphCycleBehavior.SimplePath)
        {
            throw new NotSupportedException(
                "TigerGraph does not expose intermediate repeated-hop aliases required to enforce vertex-simple paths.");
        }
        if (useSyntaxV2 && query.Projection == GraphQueryProjection.Path)
        {
            throw new NotSupportedException(
                "TigerGraph variable-depth path payload projection is not supported because a repeated edge alias is not a single EDGE value.");
        }

        var declaration = string.Join(", ", query.Parameters.Select(
            parameter => $"{MapType(parameter.ClrType)} {parameter.Name}"));

        var builder = new StringBuilder()
            .Append("INTERPRET QUERY (")
            .Append(declaration)
            .Append(") FOR GRAPH ")
            .Append(graphName)
            .Append(useSyntaxV2 ? " SYNTAX V2 { " : " { ");

        if (query.Projection == GraphQueryProjection.Row)
        {
            builder.Append(RenderRowSelection(query))
                .Append("PRINT nodal_rows; }");
        }
        else if (query.Projection == GraphQueryProjection.Count)
        {
            builder.Append(RenderSelection(query, "result", query.ResultAlias, false, useSyntaxV2))
                .Append("PRINT result.size() AS nodal_count; }");
        }
        else if (query.Projection == GraphQueryProjection.Path)
        {
            builder.Append("ListAccum<EDGE> @@nodal_relations; ")
                .Append(RenderSelection(query, "nodal_sources", query.Alias, collectRelations: true, useSyntaxV2))
                .Append(RenderSelection(query, "nodal_targets", query.ResultAlias, collectRelations: false, useSyntaxV2))
                .Append("PRINT nodal_sources, @@nodal_relations AS nodal_relations, nodal_targets; }");
        }
        else if (query.Projection == GraphQueryProjection.Subgraph)
        {
            var aliases = new[] { query.Alias }.Concat(query.Traversals.Select(step => step.TargetAlias)).ToArray();
            var names = aliases.Select((_, index) => $"nodal_nodes_{index}").ToArray();
            builder.Append("ListAccum<EDGE> @@nodal_relations; ");
            for (var index = 0; index < aliases.Length; index++)
            {
                var selection = RenderSelection(query, names[index], aliases[index], false, useSyntaxV2);
                builder.Append(index == 0 ? AddRelationAccumulation(selection, query) : selection);
            }

            builder.Append("PRINT ")
                .Append(string.Join(", ", names))
                .Append(", @@nodal_relations AS nodal_relations; }");
        }
        else
        {
            builder.Append(RenderSelection(query, "result", query.ResultAlias, collectRelations: false, useSyntaxV2))
                .Append("PRINT result; }");
        }

        return new GraphCommand(
            builder.ToString(),
            query.Parameters.ToDictionary(parameter => parameter.Name, parameter => NormalizeValue(parameter.Value)));
    }

    private static string RenderRowSelection(GraphQueryModel query)
    {
        query = NormalizeRowAliases(query);
        var projection = query.RowProjection ?? throw new InvalidOperationException(
            "A row projection is required when compiling a row query.");
        var hasAggregates = projection.Columns.Any(column => column.Kind != GraphRowColumnKind.Property);
        var propertyExpressions = projection.Columns
            .Where(column => column.Kind == GraphRowColumnKind.Property)
            .Select(RenderRowExpression)
            .ToArray();
        var builder = new StringBuilder("SELECT ");

        if (query.Distinct)
        {
            builder.Append("DISTINCT ");
        }

        builder.Append(string.Join(", ", projection.Columns.Select(RenderRowColumn)))
            .Append(" INTO nodal_rows FROM ");
        if (query.Traversals.Count == 0)
        {
            builder.Append(query.NodeType).Append(':').Append(query.Alias);
        }
        else
        {
            builder.Append('(').Append(query.Alias).Append(':').Append(query.NodeType).Append(')');
        }

        foreach (var traversal in query.Traversals)
        {
            builder.Append(RenderTraversal(traversal, useSyntaxV2: true));
        }

        var filters = RenderFilters(query);
        if (filters.Count > 0)
        {
            builder.Append(" WHERE ").Append(string.Join(" AND ", filters));
        }

        if (hasAggregates && propertyExpressions.Length > 0)
        {
            builder.Append(" GROUP BY ").Append(string.Join(", ", propertyExpressions));
        }

        if (projection.EffectiveHavingPredicates.Count > 0)
        {
            builder.Append(" HAVING ")
                .Append(string.Join(" AND ", projection.EffectiveHavingPredicates.Select(predicate =>
                    $"{predicate.ColumnName} {RenderOperator(predicate.Operator)} {predicate.ParameterName}")));
        }

        if (projection.EffectiveOrderings.Count > 0)
        {
            builder.Append(" ORDER BY ")
                .Append(string.Join(", ", projection.EffectiveOrderings.Select(ordering =>
                    $"{ordering.ColumnName} {(ordering.Direction == GraphSortDirection.Ascending ? "ASC" : "DESC")}")));
        }

        if (query.Limit is not null && query.Offset is not null)
        {
            builder.Append(" LIMIT ").Append(query.Limit.Value).Append(" OFFSET ").Append(query.Offset.Value);
        }
        else if (query.Limit is not null)
        {
            builder.Append(" LIMIT ").Append(query.Limit.Value);
        }
        else if (query.Offset is not null)
        {
            throw new NotSupportedException("TigerGraph requires Take when Skip is used.");
        }

        return builder.Append("; ").ToString();
    }

    private static GraphQueryModel NormalizeRowAliases(GraphQueryModel query)
    {
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [query.Alias] = "nodal_v0",
        };
        var traversals = query.Traversals.Select((traversal, index) =>
        {
            var relationAlias = $"nodal_e{index}";
            var targetAlias = $"nodal_v{index + 1}";
            aliases[traversal.RelationAlias] = relationAlias;
            aliases[traversal.TargetAlias] = targetAlias;
            return traversal with { RelationAlias = relationAlias, TargetAlias = targetAlias };
        }).ToArray();
        var projection = query.RowProjection ?? throw new InvalidOperationException(
            "A row projection is required when normalizing a row query.");

        return query with
        {
            Alias = aliases[query.Alias],
            Traversals = traversals,
            RowProjection = projection with
            {
                Columns = projection.Columns.Select(column => column with
                {
                    SourceAlias = aliases.TryGetValue(column.SourceAlias, out var alias)
                        ? alias
                        : throw new InvalidOperationException(
                            $"Row column '{column.Name}' refers to unknown alias '{column.SourceAlias}'."),
                }).ToArray(),
            },
        };
    }

    private static string RenderRowColumn(GraphRowColumn column) =>
        $"{RenderRowExpression(column)} AS {column.Name}";

    private static string RenderRowExpression(GraphRowColumn column)
    {
        var property = string.IsNullOrEmpty(column.PropertyName)
            ? null
            : $"{column.SourceAlias}.{column.PropertyName}";
        return column.Kind switch
        {
            GraphRowColumnKind.Property when property is not null => property,
            GraphRowColumnKind.Count => $"COUNT({(column.Distinct ? "DISTINCT " : string.Empty)}{column.SourceAlias})",
            GraphRowColumnKind.Sum when property is not null => $"SUM({property})",
            GraphRowColumnKind.Average when property is not null => $"AVG({property})",
            GraphRowColumnKind.Minimum when property is not null => $"MIN({property})",
            GraphRowColumnKind.Maximum when property is not null => $"MAX({property})",
            _ => throw new InvalidOperationException(
                $"Row column '{column.Name}' requires a mapped property for '{column.Kind}'."),
        };
    }

    private static List<string> RenderFilters(GraphQueryModel query)
    {
        var filters = new List<string>();
        if (query.CycleBehavior == GraphCycleBehavior.SimplePath)
        {
            var aliases = new[] { query.Alias }.Concat(query.Traversals.Select(step => step.TargetAlias)).ToArray();
            for (var left = 0; left < aliases.Length; left++)
            {
                for (var right = left + 1; right < aliases.Length; right++)
                {
                    filters.Add($"{aliases[left]} != {aliases[right]}");
                }
            }
        }
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
        return filters;
    }

    private static string AddRelationAccumulation(string selection, GraphQueryModel query)
    {
        if (query.Traversals.Count == 0)
        {
            return selection;
        }

        var accumulations = query.Traversals.Select(step => $"@@nodal_relations += {step.RelationAlias}");
        var insertionIndex = selection.IndexOf(" ORDER BY", StringComparison.Ordinal);
        if (insertionIndex < 0)
        {
            insertionIndex = selection.IndexOf(" LIMIT", StringComparison.Ordinal);
        }
        if (insertionIndex < 0)
        {
            insertionIndex = selection.IndexOf(';', StringComparison.Ordinal);
        }
        return selection.Insert(insertionIndex, $" ACCUM {string.Join(", ", accumulations)}");
    }

    private static string RenderSelection(
        GraphQueryModel query,
        string resultName,
        string selectedAlias,
        bool collectRelations,
        bool useSyntaxV2)
    {
        var builder = new StringBuilder()
            .Append(resultName)
            .Append(" = SELECT ")
            .Append(selectedAlias)
            .Append(" FROM ");

        if (useSyntaxV2)
        {
            builder.Append('(').Append(query.Alias).Append(':').Append(query.NodeType).Append(')');
        }
        else
        {
            builder.Append(query.NodeType).Append(':').Append(query.Alias);
        }

        foreach (var traversal in query.Traversals)
        {
            builder.Append(RenderTraversal(traversal, useSyntaxV2));
        }

        var filters = RenderFilters(query);
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

        if (query.EffectiveOrderings.Count > 0)
        {
            builder.Append(" ORDER BY ").Append(string.Join(", ", query.EffectiveOrderings.Select(RenderOrdering)));
        }

        if (query.Limit is not null && query.Offset is not null)
        {
            builder.Append(" LIMIT ").Append(query.Limit.Value).Append(" OFFSET ").Append(query.Offset.Value);
        }
        else if (query.Limit is not null)
        {
            builder.Append(" LIMIT ").Append(query.Limit.Value);
        }
        else if (query.Offset is not null)
        {
            throw new NotSupportedException("TigerGraph requires Take when Skip is used.");
        }

        return builder.Append("; ").ToString();
    }

    private static string RenderPredicate(GraphPredicate predicate, string alias) => predicate switch
    {
        GraphComparisonPredicate comparison =>
            $"{alias}.{comparison.PropertyName} {RenderOperator(comparison.Operator)} {comparison.ParameterName}",
        GraphLogicalPredicate logical =>
            $"({RenderPredicate(logical.Left, alias)} {RenderLogicalOperator(logical.Operator)} {RenderPredicate(logical.Right, alias)})",
        GraphNotPredicate not => $"NOT ({RenderPredicate(not.Operand, alias)})",
        GraphNullPredicate nullCheck =>
            $"{alias}.{nullCheck.PropertyName} IS {(nullCheck.IsNull ? string.Empty : "NOT ")}NULL",
        GraphStringPredicate text => RenderStringPredicate(text, alias),
        GraphInPredicate membership =>
            $"{alias}.{membership.PropertyName} {(membership.Negated ? "NOT " : string.Empty)}IN {membership.ParameterName}",
        _ => throw new NotSupportedException($"Predicate '{predicate.GetType().Name}' is not supported by TigerGraph."),
    };

    private static string RenderTraversal(GraphTraversalStep traversal, bool useSyntaxV2)
    {
        if (traversal.MinDepth < 0 || traversal.MaxDepth < traversal.MinDepth)
        {
            throw new ArgumentOutOfRangeException(nameof(traversal), "Traversal depth bounds are invalid.");
        }
        var depth = traversal.MinDepth == 1 && traversal.MaxDepth == 1
            ? string.Empty
            : $"*{traversal.MinDepth}..{traversal.MaxDepth}";
        var types = traversal.RelationTypes.Count == 1
            ? traversal.RelationType
            : $"({string.Join('|', traversal.RelationTypes)})";
        if (useSyntaxV2)
        {
            var v2Types = string.Join('|', traversal.RelationTypes);
            var edge = $"[{traversal.RelationAlias}:{v2Types}{depth}]";
            var v2Target = $"({traversal.TargetAlias}:{traversal.TargetNodeType})";
            return traversal.Direction switch
            {
                GraphTraversalDirection.Outgoing => $"-{edge}->{v2Target}",
                GraphTraversalDirection.Incoming => $"<-{edge}-{v2Target}",
                GraphTraversalDirection.Undirected => $"-{edge}-{v2Target}",
                _ => throw new ArgumentOutOfRangeException(nameof(traversal), traversal.Direction, null),
            };
        }
        var relation = $"({types}{depth}:{traversal.RelationAlias})";
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

    private static string RenderStringPredicate(GraphStringPredicate predicate, string alias)
    {
        var parameter = predicate.Operator switch
        {
            GraphStringOperator.StartsWith => $"{predicate.ParameterName} + \"%\"",
            GraphStringOperator.Contains => $"\"%\" + {predicate.ParameterName} + \"%\"",
            GraphStringOperator.EndsWith => $"\"%\" + {predicate.ParameterName}",
            _ => throw new ArgumentOutOfRangeException(nameof(predicate), predicate.Operator, null),
        };
        return $"{alias}.{predicate.PropertyName} LIKE {parameter}";
    }

    private static string RenderOrdering(GraphOrdering ordering) =>
        $"{ordering.Alias}.{ordering.PropertyName} " +
        (ordering.Direction == GraphSortDirection.Ascending ? "ASC" : "DESC");

    private static string MapType(Type type)
    {
        if (type != typeof(string) && typeof(System.Collections.IEnumerable).IsAssignableFrom(type))
        {
            var elementType = type.IsArray
                ? type.GetElementType()!
                : type.IsGenericType ? type.GetGenericArguments()[0] : typeof(string);
            return $"SET<{MapType(elementType)}>";
        }
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
