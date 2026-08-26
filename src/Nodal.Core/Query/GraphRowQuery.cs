using System.Linq.Expressions;
using Nodal.Core.Execution;

namespace Nodal.Core.Query;

/// <summary>
/// Builds an immutable provider-side scalar and aggregate result-row projection.
/// </summary>
/// <typeparam name="TNode">The CLR type represented by the current query result alias.</typeparam>
public sealed class GraphRowQuery<TNode>
{
    private readonly GraphQueryModel model;
    private readonly IGraphQueryExecutor? executor;
    private readonly IReadOnlyDictionary<string, string>? propertyMappings;

    internal GraphRowQuery(
        GraphQueryModel model,
        IGraphQueryExecutor? executor,
        IReadOnlyDictionary<string, string>? propertyMappings)
    {
        this.model = model;
        this.executor = executor;
        this.propertyMappings = propertyMappings;
    }

    /// <summary>
    /// Adds a mapped property to the provider-side row projection.
    /// </summary>
    /// <typeparam name="TValue">The property CLR type.</typeparam>
    /// <param name="name">The stable result-column name.</param>
    /// <param name="selector">The mapped property to return.</param>
    /// <returns>A new row query containing the scalar column.</returns>
    public GraphRowQuery<TNode> Select<TValue>(string name, Expression<Func<TNode, TValue>> selector) =>
        AddPropertyColumn(name, GraphRowColumnKind.Property, selector);

    /// <summary>Counts the currently bound result-node alias.</summary>
    /// <param name="name">The stable result-column name.</param>
    /// <param name="distinct">Whether repeated bound nodes are counted once.</param>
    /// <returns>A new row query containing a count column.</returns>
    public GraphRowQuery<TNode> Count(string name, bool distinct = false) =>
        AddColumn(new GraphRowColumn(ValidateColumnName(name), GraphRowColumnKind.Count, model.ResultAlias, Distinct: distinct));

    /// <summary>Sums a mapped numeric property at the provider.</summary>
    public GraphRowQuery<TNode> Sum<TValue>(string name, Expression<Func<TNode, TValue>> selector) =>
        AddPropertyColumn(name, GraphRowColumnKind.Sum, selector);

    /// <summary>Averages a mapped numeric property at the provider.</summary>
    public GraphRowQuery<TNode> Average<TValue>(string name, Expression<Func<TNode, TValue>> selector) =>
        AddPropertyColumn(name, GraphRowColumnKind.Average, selector);

    /// <summary>Returns the minimum mapped property value at the provider.</summary>
    public GraphRowQuery<TNode> Min<TValue>(string name, Expression<Func<TNode, TValue>> selector) =>
        AddPropertyColumn(name, GraphRowColumnKind.Minimum, selector);

    /// <summary>Returns the maximum mapped property value at the provider.</summary>
    public GraphRowQuery<TNode> Max<TValue>(string name, Expression<Func<TNode, TValue>> selector) =>
        AddPropertyColumn(name, GraphRowColumnKind.Maximum, selector);

    /// <summary>Orders projected rows by a named column in ascending order.</summary>
    public GraphRowQuery<TNode> OrderBy(string columnName) => SetOrdering(columnName, GraphSortDirection.Ascending, append: false);

    /// <summary>Orders projected rows by a named column in descending order.</summary>
    public GraphRowQuery<TNode> OrderByDescending(string columnName) => SetOrdering(columnName, GraphSortDirection.Descending, append: false);

    /// <summary>Adds ascending ordering after an existing projected-row ordering.</summary>
    public GraphRowQuery<TNode> ThenBy(string columnName) => SetOrdering(columnName, GraphSortDirection.Ascending, append: true);

    /// <summary>Adds descending ordering after an existing projected-row ordering.</summary>
    public GraphRowQuery<TNode> ThenByDescending(string columnName) => SetOrdering(columnName, GraphSortDirection.Descending, append: true);

    /// <summary>
    /// Filters aggregated rows using a parameterized predicate evaluated after aggregation.
    /// </summary>
    /// <typeparam name="TValue">The CLR type of the comparison value.</typeparam>
    /// <param name="columnName">The projected aggregate column to filter.</param>
    /// <param name="comparison">The comparison operation.</param>
    /// <param name="value">The parameterized comparison value.</param>
    /// <returns>A new row query containing the aggregate-stage predicate.</returns>
    /// <example>
    /// <code>var popular = query.ToRows().Count("orderCount").Having("orderCount", GraphComparisonOperator.GreaterThan, 10);</code>
    /// </example>
    public GraphRowQuery<TNode> Having<TValue>(
        string columnName,
        GraphComparisonOperator comparison,
        TValue value)
    {
        var projection = RequireProjection();
        columnName = RequireColumn(columnName, projection);
        if (!projection.Columns.Any(column => column.Name == columnName && column.Kind != GraphRowColumnKind.Property))
        {
            throw new InvalidOperationException("Having requires an aggregate result column.");
        }
        var parameterName = $"p{model.Parameters.Count}";
        return Copy(model with
        {
            Parameters = [.. model.Parameters, new GraphQueryParameter(parameterName, value, typeof(TValue))],
            RowProjection = projection with
            {
                HavingPredicates = [.. projection.EffectiveHavingPredicates,
                    new GraphRowPredicate(columnName, comparison, parameterName)],
            },
        });
    }

    /// <summary>Limits the number of projected rows returned by the provider.</summary>
    public GraphRowQuery<TNode> Take(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        return Copy(model with { Limit = count });
    }

    /// <summary>Produces the immutable query model consumed by the selected provider compiler.</summary>
    public GraphQueryModel ToQueryModel() => model with
    {
        Projection = GraphQueryProjection.Row,
        RowProjection = model.RowProjection ?? throw new InvalidOperationException("At least one row column is required."),
    };

    /// <summary>Executes the provider-side row projection.</summary>
    public async ValueTask<IReadOnlyList<GraphQueryRow>> ToListAsync(CancellationToken cancellationToken = default)
    {
        if (executor is null)
        {
            throw new InvalidOperationException(
                "This row query is not attached to a NodalContext. Use ToQueryModel for compiler-only scenarios.");
        }

        var result = await executor.ExecuteRowsAsync(ToQueryModel(), cancellationToken).ConfigureAwait(false);
        return result.Select(row => new GraphQueryRow(row.Values)).ToArray();
    }

    private GraphRowQuery<TNode> AddPropertyColumn<TValue>(
        string name,
        GraphRowColumnKind kind,
        Expression<Func<TNode, TValue>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return AddColumn(new GraphRowColumn(
            ValidateColumnName(name),
            kind,
            model.ResultAlias,
            GraphExpressionTranslator.TranslateProperty(selector, propertyMappings)));
    }

    private GraphRowQuery<TNode> AddColumn(GraphRowColumn column)
    {
        var columns = model.RowProjection?.Columns ?? [];
        if (columns.Any(existing => string.Equals(existing.Name, column.Name, StringComparison.Ordinal)))
        {
            throw new ArgumentException($"A '{column.Name}' row column already exists.", nameof(column));
        }
        return Copy(model with { Projection = GraphQueryProjection.Row, RowProjection = new GraphRowProjection([.. columns, column]) });
    }

    private GraphRowQuery<TNode> SetOrdering(string columnName, GraphSortDirection direction, bool append)
    {
        var projection = RequireProjection();
        columnName = RequireColumn(columnName, projection);
        if (append && projection.EffectiveOrderings.Count == 0)
        {
            throw new InvalidOperationException("ThenBy requires a preceding OrderBy or OrderByDescending call.");
        }
        var ordering = new GraphRowOrdering(columnName, direction);
        return Copy(model with
        {
            RowProjection = projection with
            {
                Orderings = append ? [.. projection.EffectiveOrderings, ordering] : [ordering],
            },
        });
    }

    private GraphRowProjection RequireProjection()
    {
        return model.RowProjection ?? throw new InvalidOperationException(
            "At least one row column is required before adding ordering or aggregate predicates.");
    }

    private static string RequireColumn(string columnName, GraphRowProjection projection)
    {
        columnName = ValidateColumnName(columnName);
        return projection.Columns.Any(column => string.Equals(column.Name, columnName, StringComparison.Ordinal))
            ? columnName
            : throw new ArgumentException($"The row projection does not contain a '{columnName}' column.", nameof(columnName));
    }

    private GraphRowQuery<TNode> Copy(GraphQueryModel next) => new(next, executor, propertyMappings);

    private static string ValidateColumnName(string name) => GraphQueryAliases.Validate(name, nameof(name));
}
