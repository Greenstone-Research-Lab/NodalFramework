using System.Linq.Expressions;
using Nodal.Core.Execution;
using Nodal.Core.Metadata;
using Nodal.Core.Model;

namespace Nodal.Core.Query;

/// <summary>
/// Builds an immutable, provider-neutral query for a graph node type.
/// </summary>
/// <typeparam name="TNode">The node type returned by the query.</typeparam>
public sealed class GraphQuery<TNode>
{
    private readonly GraphQueryModel model;
    private readonly IGraphQueryExecutor? executor;
    private readonly IReadOnlyDictionary<string, string>? propertyMappings;

    internal GraphQuery(
        GraphQueryModel model,
        IGraphQueryExecutor? executor,
        IReadOnlyDictionary<string, string>? propertyMappings)
    {
        this.model = model;
        this.executor = executor;
        this.propertyMappings = propertyMappings;
    }

    /// <summary>
    /// Adds a strongly typed predicate to this query.
    /// </summary>
    /// <param name="predicate">An expression over the node type.</param>
    /// <returns>A new query containing the combined predicate.</returns>
    /// <example>
    /// <code>
    /// var adults = context.Persons.Query().Where(person => person.Age >= 18);
    /// </code>
    /// </example>
    public GraphQuery<TNode> Where(Expression<Func<TNode, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        var translated = GraphExpressionTranslator.Translate(
            predicate,
            model.Parameters.Count,
            propertyMappings);
        var parameters = model.Parameters.Concat(translated.Parameters).ToArray();

        if (model.Traversals.Count > 0)
        {
            var traversals = model.Traversals.ToArray();
            var current = traversals[^1];
            traversals[^1] = current with
            {
                Predicate = Combine(current.Predicate, translated.Predicate),
            };
            return new GraphQuery<TNode>(
                model with { Traversals = traversals, Parameters = parameters },
                executor,
                propertyMappings);
        }

        return new GraphQuery<TNode>(
            model with
            {
                Predicate = Combine(model.Predicate, translated.Predicate),
                Parameters = parameters,
            },
            executor,
            propertyMappings);
    }

    /// <summary>
    /// Traverses a relationship from its declared source type to its target type.
    /// Undirected relationship metadata automatically produces a direction-agnostic hop.
    /// </summary>
    public GraphQuery<TTarget> Traverse<TRelation, TTarget>(
        RelationSet<TNode, TRelation, TTarget> relationSet)
        where TRelation : notnull
    {
        ArgumentNullException.ThrowIfNull(relationSet);
        var direction = relationSet.Metadata.Directed
            ? GraphTraversalDirection.Outgoing
            : GraphTraversalDirection.Undirected;
        return AppendTraversal<TTarget>(
            relationSet.Metadata.Name,
            relationSet.TargetMetadata,
            direction);
    }

    /// <summary>
    /// Traverses one relationship and returns a query that preserves its source, payload, and target.
    /// </summary>
    public GraphPathQuery<TNode, TRelation, TTarget> TraversePath<TRelation, TTarget>(
        RelationSet<TNode, TRelation, TTarget> relationSet)
        where TRelation : notnull
    {
        ArgumentNullException.ThrowIfNull(relationSet);
        var direction = relationSet.Metadata.Directed
            ? GraphTraversalDirection.Outgoing
            : GraphTraversalDirection.Undirected;
        var query = AppendTraversal<TTarget>(
            relationSet.Metadata.Name,
            relationSet.TargetMetadata,
            direction);
        return GraphPathQuery<TNode, TRelation, TTarget>.Create(
            query.model with { Projection = GraphQueryProjection.Path },
            executor,
            relationSet.Metadata,
            relationSet.TargetMetadata);
    }

    /// <summary>Traverses a directed relationship from its declared target back to its source.</summary>
    public GraphQuery<TSource> TraverseIncoming<TSource, TRelation>(
        RelationSet<TSource, TRelation, TNode> relationSet)
        where TRelation : notnull
    {
        ArgumentNullException.ThrowIfNull(relationSet);
        return AppendTraversal<TSource>(
            relationSet.Metadata.Name,
            relationSet.SourceMetadata,
            relationSet.Metadata.Directed
                ? GraphTraversalDirection.Incoming
                : GraphTraversalDirection.Undirected);
    }

    /// <summary>
    /// Limits the maximum number of nodes returned by the provider.
    /// </summary>
    /// <param name="count">A positive result count.</param>
    /// <returns>A new query with the requested limit.</returns>
    public GraphQuery<TNode> Take(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        return new GraphQuery<TNode>(model with { Limit = count }, executor, propertyMappings);
    }

    /// <summary>
    /// Produces the immutable model consumed by a database provider.
    /// </summary>
    /// <returns>The provider-neutral query model.</returns>
    public GraphQueryModel ToQueryModel() => model;

    /// <summary>
    /// Executes this query with the provider configured by its context.
    /// </summary>
    /// <exception cref="InvalidOperationException">The query was created without a configured context.</exception>
    public ValueTask<IReadOnlyList<TNode>> ToListAsync(CancellationToken cancellationToken = default)
    {
        if (executor is null)
        {
            throw new InvalidOperationException(
                "This query is not attached to a NodalContext. Use ToQueryModel for compiler-only scenarios.");
        }

        return executor.ExecuteAsync<TNode>(model, cancellationToken);
    }

    private GraphQuery<TTarget> AppendTraversal<TTarget>(
        string relationType,
        GraphNodeMetadata targetMetadata,
        GraphTraversalDirection direction)
    {
        var index = model.Traversals.Count + 1;
        var step = new GraphTraversalStep(
            relationType,
            targetMetadata.Name,
            model.ResultAlias,
            $"relation{index}",
            $"node{index}",
            direction,
            null);
        var mappings = targetMetadata.Properties.ToDictionary(
            property => property.Key,
            property => property.Value.Name);
        return new GraphQuery<TTarget>(
            model with { Traversals = [.. model.Traversals, step] },
            executor,
            mappings);
    }

    private static GraphPredicate Combine(GraphPredicate? current, GraphPredicate next) =>
        current is null
            ? next
            : new GraphLogicalPredicate(current, GraphLogicalOperator.And, next);
}

/// <summary>Builds a strongly typed query for one source–relationship–target path.</summary>
public sealed class GraphPathQuery<TSource, TRelation, TTarget>
    where TRelation : notnull
{
    private readonly GraphQueryModel model;
    private readonly IGraphQueryExecutor? executor;
    private readonly IReadOnlyDictionary<string, string> relationMappings;
    private readonly IReadOnlyDictionary<string, string> targetMappings;

    private GraphPathQuery(
        GraphQueryModel model,
        IGraphQueryExecutor? executor,
        IReadOnlyDictionary<string, string> relationMappings,
        IReadOnlyDictionary<string, string> targetMappings)
    {
        this.model = model;
        this.executor = executor;
        this.relationMappings = relationMappings;
        this.targetMappings = targetMappings;
    }

    internal static GraphPathQuery<TSource, TRelation, TTarget> Create(
        GraphQueryModel model,
        IGraphQueryExecutor? executor,
        GraphRelationMetadata relationMetadata,
        GraphNodeMetadata targetMetadata) => new(
            model,
            executor,
            relationMetadata.Properties.ToDictionary(property => property.Key, property => property.Value.Name),
            targetMetadata.Properties.ToDictionary(property => property.Key, property => property.Value.Name));

    /// <summary>Adds a predicate over the relationship payload.</summary>
    public GraphPathQuery<TSource, TRelation, TTarget> WhereRelation(
        Expression<Func<TRelation, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var translated = GraphExpressionTranslator.Translate(predicate, model.Parameters.Count, relationMappings);
        var traversals = model.Traversals.ToArray();
        var last = traversals[^1];
        traversals[^1] = last with
        {
            RelationPredicate = Combine(last.RelationPredicate, translated.Predicate),
        };
        return Copy(model with
        {
            Traversals = traversals,
            Parameters = [.. model.Parameters, .. translated.Parameters],
        });
    }

    /// <summary>Adds a predicate over the reached node.</summary>
    public GraphPathQuery<TSource, TRelation, TTarget> WhereTarget(
        Expression<Func<TTarget, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var translated = GraphExpressionTranslator.Translate(predicate, model.Parameters.Count, targetMappings);
        var traversals = model.Traversals.ToArray();
        var last = traversals[^1];
        traversals[^1] = last with { Predicate = Combine(last.Predicate, translated.Predicate) };
        return Copy(model with
        {
            Traversals = traversals,
            Parameters = [.. model.Parameters, .. translated.Parameters],
        });
    }

    /// <summary>Limits the maximum number of paths returned by the provider.</summary>
    public GraphPathQuery<TSource, TRelation, TTarget> Take(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        return Copy(model with { Limit = count });
    }

    /// <summary>Produces the immutable provider-neutral query model.</summary>
    public GraphQueryModel ToQueryModel() => model;

    /// <summary>Executes and materializes the requested paths.</summary>
    public ValueTask<IReadOnlyList<GraphPath<TSource, TRelation, TTarget>>> ToListAsync(
        CancellationToken cancellationToken = default)
    {
        if (executor is null)
        {
            throw new InvalidOperationException(
                "This path query is not attached to a NodalContext. Use ToQueryModel for compiler-only scenarios.");
        }

        return executor.ExecutePathsAsync<TSource, TRelation, TTarget>(model, cancellationToken);
    }

    /// <summary>Executes the path query and projects only its relationship payloads.</summary>
    public async ValueTask<IReadOnlyList<TRelation>> ToRelationsAsync(
        CancellationToken cancellationToken = default)
    {
        var paths = await ToListAsync(cancellationToken).ConfigureAwait(false);
        return paths.Select(path => path.Relation).ToArray();
    }

    /// <summary>Executes the query and requires exactly one path.</summary>
    public async ValueTask<GraphPath<TSource, TRelation, TTarget>> SingleAsync(
        CancellationToken cancellationToken = default)
    {
        var paths = await ToListAsync(cancellationToken).ConfigureAwait(false);
        return paths.Single();
    }

    private GraphPathQuery<TSource, TRelation, TTarget> Copy(GraphQueryModel next) => new(
        next,
        executor,
        relationMappings,
        targetMappings);

    private static GraphPredicate Combine(GraphPredicate? current, GraphPredicate next) =>
        current is null ? next : new GraphLogicalPredicate(current, GraphLogicalOperator.And, next);
}
