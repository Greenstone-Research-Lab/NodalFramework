using System.Linq.Expressions;
using Nodal.Core.Execution;

namespace Nodal.Core.Query;

/// <summary>Builds a portable union over two compatible graph-node queries.</summary>
public sealed class GraphSetQuery<TNode>
{
    private readonly GraphSetOperation operation;
    private readonly IGraphQueryExecutor? executor;
    private readonly IReadOnlyDictionary<string, string>? propertyMappings;
    private readonly GraphQueryModel outer;

    internal GraphSetQuery(GraphSetOperation operation, IGraphQueryExecutor? executor,
        IReadOnlyDictionary<string, string>? propertyMappings, GraphQueryModel outer)
    {
        this.operation = operation;
        this.executor = executor;
        this.propertyMappings = propertyMappings;
        this.outer = outer;
    }

    /// <summary>Limits the combined result set after the set operation completes.</summary>
    public GraphSetQuery<TNode> Take(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        return Copy(outer with { Limit = count });
    }

    /// <summary>Skips an ordered number of results after the set operation completes.</summary>
    /// <exception cref="InvalidOperationException">Thrown when no deterministic ordering has been defined.</exception>
    public GraphSetQuery<TNode> Skip(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (outer.EffectiveOrderings.Count == 0)
        {
            throw new InvalidOperationException("Skip requires OrderBy or OrderByDescending for deterministic set-query pagination.");
        }
        return Copy(outer with { Offset = count });
    }

    /// <summary>Orders the combined result set by a mapped node property.</summary>
    public GraphSetQuery<TNode> OrderBy<TValue>(Expression<Func<TNode, TValue>> selector) =>
        SetOrdering(selector, GraphSortDirection.Ascending, false);

    /// <summary>Orders the combined result set by a mapped node property in descending order.</summary>
    public GraphSetQuery<TNode> OrderByDescending<TValue>(Expression<Func<TNode, TValue>> selector) =>
        SetOrdering(selector, GraphSortDirection.Descending, false);

    /// <summary>Adds a secondary ascending ordering to the combined result set.</summary>
    public GraphSetQuery<TNode> ThenBy<TValue>(Expression<Func<TNode, TValue>> selector) =>
        SetOrdering(selector, GraphSortDirection.Ascending, true);

    /// <summary>Adds a secondary descending ordering to the combined result set.</summary>
    public GraphSetQuery<TNode> ThenByDescending<TValue>(Expression<Func<TNode, TValue>> selector) =>
        SetOrdering(selector, GraphSortDirection.Descending, true);

    /// <summary>Produces the immutable provider-neutral set-query model.</summary>
    public GraphQueryModel ToQueryModel() => outer with { SetOperation = operation };

    /// <summary>Executes the combined node query.</summary>
    public ValueTask<IReadOnlyList<TNode>> ToListAsync(CancellationToken cancellationToken = default)
    {
        if (executor is null)
        {
            throw new InvalidOperationException("This set query is not attached to a NodalContext.");
        }
        return executor.ExecuteAsync<TNode>(ToQueryModel(), cancellationToken);
    }

    private GraphSetQuery<TNode> SetOrdering<TValue>(Expression<Func<TNode, TValue>> selector,
        GraphSortDirection direction, bool append)
    {
        ArgumentNullException.ThrowIfNull(selector);
        if (append && outer.EffectiveOrderings.Count == 0)
        {
            throw new InvalidOperationException("ThenBy requires a preceding OrderBy or OrderByDescending call.");
        }
        var ordering = new GraphOrdering(GraphExpressionTranslator.TranslateProperty(selector, propertyMappings), outer.Alias, direction);
        return Copy(outer with { Orderings = append ? [.. outer.EffectiveOrderings, ordering] : [ordering] });
    }

    private GraphSetQuery<TNode> Copy(GraphQueryModel next) => new(operation, executor, propertyMappings, next);
}
