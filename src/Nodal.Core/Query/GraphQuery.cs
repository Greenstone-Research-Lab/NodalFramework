using System.Linq.Expressions;
using Nodal.Core.Analytics;
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
            direction, 1, 1, false);
    }

    /// <summary>
    /// Traverses a relationship using caller-defined aliases for the relationship and reached node.
    /// </summary>
    /// <typeparam name="TRelation">The relationship payload type.</typeparam>
    /// <typeparam name="TTarget">The reached node type.</typeparam>
    /// <param name="relationSet">The relationship set to traverse.</param>
    /// <param name="relationAlias">The portable alias assigned to the relationship payload.</param>
    /// <param name="targetAlias">The portable alias assigned to the reached node.</param>
    /// <returns>A query over the reached node type.</returns>
    /// <example>
    /// <code>
    /// var orders = context.Customers.Query("customer")
    ///     .Traverse(context.CustomerOrders, "placed", "order");
    /// </code>
    /// </example>
    public GraphQuery<TTarget> Traverse<TRelation, TTarget>(
        RelationSet<TNode, TRelation, TTarget> relationSet,
        string relationAlias,
        string targetAlias)
        where TRelation : notnull
    {
        ArgumentNullException.ThrowIfNull(relationSet);
        return AppendTraversal<TTarget>(
            relationSet.Metadata.Name,
            relationSet.TargetMetadata,
            relationSet.Metadata.Directed ? GraphTraversalDirection.Outgoing : GraphTraversalDirection.Undirected,
            1,
            1,
            false,
            relationAlias,
            targetAlias);
    }

    /// <summary>Traverses a relationship repeatedly within inclusive depth bounds.</summary>
    public GraphQuery<TTarget> Traverse<TRelation, TTarget>(
        RelationSet<TNode, TRelation, TTarget> relationSet,
        int minDepth,
        int maxDepth)
        where TRelation : notnull
    {
        ArgumentNullException.ThrowIfNull(relationSet);
        ValidateDepth(minDepth, maxDepth);
        return AppendTraversal<TTarget>(
            relationSet.Metadata.Name,
            relationSet.TargetMetadata,
            relationSet.Metadata.Directed ? GraphTraversalDirection.Outgoing : GraphTraversalDirection.Undirected,
            minDepth,
            maxDepth,
            false);
    }

    /// <summary>Preserves the preceding match when the requested relationship does not exist.</summary>
    public GraphQuery<TTarget> TraverseOptional<TRelation, TTarget>(
        RelationSet<TNode, TRelation, TTarget> relationSet)
        where TRelation : notnull
    {
        ArgumentNullException.ThrowIfNull(relationSet);
        return AppendTraversal<TTarget>(
            relationSet.Metadata.Name,
            relationSet.TargetMetadata,
            relationSet.Metadata.Directed ? GraphTraversalDirection.Outgoing : GraphTraversalDirection.Undirected,
            1,
            1,
            true);
    }

    /// <summary>
    /// Adds another required relationship pattern while preserving the current result node as the query result.
    /// </summary>
    /// <typeparam name="TRelation">The relationship payload type.</typeparam>
    /// <typeparam name="TTarget">The node type reached by the additional pattern.</typeparam>
    /// <param name="relationSet">The relationship pattern beginning at the current result node.</param>
    /// <param name="relationAlias">The portable alias assigned to the matched relationship.</param>
    /// <param name="targetAlias">The portable alias assigned to the matched target node.</param>
    /// <param name="targetPredicate">An optional predicate over the additional target node.</param>
    /// <param name="relationPredicate">An optional predicate over the additional relationship payload.</param>
    /// <returns>A query whose result node remains unchanged and which requires the additional pattern.</returns>
    /// <example>
    /// <code>
    /// var customers = context.Customers.Query("customer")
    ///     .AlsoMatch(context.CustomerOrders, "placed", "order", order => order.Total > 100m)
    ///     .AlsoMatch(context.CustomerTickets, "opened", "ticket");
    /// </code>
    /// </example>
    public GraphQuery<TNode> AlsoMatch<TRelation, TTarget>(
        RelationSet<TNode, TRelation, TTarget> relationSet,
        string relationAlias,
        string targetAlias,
        Expression<Func<TTarget, bool>>? targetPredicate = null,
        Expression<Func<TRelation, bool>>? relationPredicate = null)
        where TRelation : notnull
    {
        ArgumentNullException.ThrowIfNull(relationSet);
        if (model.CycleBehavior == GraphCycleBehavior.SimplePath)
        {
            throw new InvalidOperationException(
                "AlsoMatch cannot be combined with WithoutCycles because branch-wide simple-path semantics are not yet defined.");
        }

        relationAlias = GraphQueryAliases.Validate(relationAlias, nameof(relationAlias));
        targetAlias = GraphQueryAliases.Validate(targetAlias, nameof(targetAlias));
        EnsureAliasesAreAvailable(relationAlias, targetAlias);
        var parameterOffset = model.Parameters.Count;
        var targetMappings = relationSet.TargetMetadata.Properties.ToDictionary(
            property => property.Key,
            property => property.Value.Name);
        var relationMappings = relationSet.Metadata.Properties.ToDictionary(
            property => property.Key,
            property => property.Value.Name);
        var translatedTarget = targetPredicate is null
            ? null
            : GraphExpressionTranslator.Translate(targetPredicate, parameterOffset, targetMappings);
        var translatedRelation = relationPredicate is null
            ? null
            : GraphExpressionTranslator.Translate(
                relationPredicate,
                parameterOffset + (translatedTarget?.Parameters.Count ?? 0),
                relationMappings);
        var pattern = new GraphTraversalStep(
            relationSet.Metadata.Name,
            relationSet.TargetMetadata.Name,
            model.ResultAlias,
            relationAlias,
            targetAlias,
            relationSet.Metadata.Directed ? GraphTraversalDirection.Outgoing : GraphTraversalDirection.Undirected,
            translatedTarget?.Predicate,
            translatedRelation?.Predicate);
        return Copy(model with
        {
            Parameters = [.. model.Parameters, .. (translatedTarget?.Parameters ?? []), .. (translatedRelation?.Parameters ?? [])],
            MatchPatterns = [.. model.EffectiveMatchPatterns, pattern],
        });
    }

    /// <summary>Traverses any of several relationship types that share the same payload and target types.</summary>
    public GraphQuery<TTarget> TraverseAny<TTarget>(
        params IGraphRelationSet<TNode, TTarget>[] relationSets)
    {
        ArgumentNullException.ThrowIfNull(relationSets);
        if (relationSets.Length == 0 || relationSets.Any(relation => relation is null))
        {
            throw new ArgumentException("At least one non-null relationship set is required.", nameof(relationSets));
        }

        var first = relationSets[0];
        var direction = first.Metadata.Directed
            ? GraphTraversalDirection.Outgoing
            : GraphTraversalDirection.Undirected;
        if (relationSets.Any(relation => relation.TargetMetadata.ClrType != first.TargetMetadata.ClrType ||
                                         (relation.Metadata.Directed ? GraphTraversalDirection.Outgoing :
                                             GraphTraversalDirection.Undirected) != direction))
        {
            throw new ArgumentException(
                "All relationship sets must have the same target type and direction.", nameof(relationSets));
        }

        var query = AppendTraversal<TTarget>(
            first.Metadata.Name, first.TargetMetadata, direction, 1, 1, false);
        var traversals = query.model.Traversals.ToArray();
        traversals[^1] = traversals[^1] with
        {
            AlternativeRelationTypes = relationSets.Skip(1).Select(relation => relation.Metadata.Name).ToArray(),
        };
        return new GraphQuery<TTarget>(query.model with { Traversals = traversals }, executor, query.propertyMappings);
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
            direction, 1, 1, false);
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
                : GraphTraversalDirection.Undirected,
            1, 1, false);
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

    /// <summary>Skips a non-negative number of ordered results at the provider.</summary>
    public GraphQuery<TNode> Skip(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return Copy(model with { Offset = count });
    }

    /// <summary>Removes duplicate result nodes.</summary>
    public GraphQuery<TNode> Distinct() => Copy(model with { Distinct = true });

    /// <summary>Orders results by a mapped property in ascending order.</summary>
    public GraphQuery<TNode> OrderBy<TProperty>(Expression<Func<TNode, TProperty>> keySelector) =>
        SetOrdering(keySelector, GraphSortDirection.Ascending, append: false);

    /// <summary>Orders results by a mapped property in descending order.</summary>
    public GraphQuery<TNode> OrderByDescending<TProperty>(Expression<Func<TNode, TProperty>> keySelector) =>
        SetOrdering(keySelector, GraphSortDirection.Descending, append: false);

    /// <summary>Adds an ascending ordering after the existing ordering clauses.</summary>
    public GraphQuery<TNode> ThenBy<TProperty>(Expression<Func<TNode, TProperty>> keySelector) =>
        SetOrdering(keySelector, GraphSortDirection.Ascending, append: true);

    /// <summary>Adds a descending ordering after the existing ordering clauses.</summary>
    public GraphQuery<TNode> ThenByDescending<TProperty>(Expression<Func<TNode, TProperty>> keySelector) =>
        SetOrdering(keySelector, GraphSortDirection.Descending, append: true);

    /// <summary>Returns detached objects without identity resolution or change tracking.</summary>
    public GraphQuery<TNode> AsNoTracking() => Copy(model with { TrackingBehavior = GraphTrackingBehavior.NoTracking });

    /// <summary>Rejects paths that visit the same vertex more than once.</summary>
    public GraphQuery<TNode> WithoutCycles() => Copy(model with { CycleBehavior = GraphCycleBehavior.SimplePath });

    /// <summary>
    /// Retains result nodes only when a related node satisfying the supplied predicates exists.
    /// </summary>
    /// <typeparam name="TRelation">The relationship payload type.</typeparam>
    /// <typeparam name="TTarget">The related target node type.</typeparam>
    /// <param name="relationSet">The relationship pattern correlated with the current result node.</param>
    /// <param name="targetPredicate">An optional predicate over the related target node.</param>
    /// <param name="relationPredicate">An optional predicate over the relationship payload.</param>
    /// <returns>A new query containing a correlated exists condition.</returns>
    /// <example>
    /// <code>
    /// var activeCustomers = context.Customers.Query()
    ///     .WhereExists(context.CustomerOrders, order => order.Total > 100m);
    /// </code>
    /// </example>
    public GraphQuery<TNode> WhereExists<TRelation, TTarget>(
        RelationSet<TNode, TRelation, TTarget> relationSet,
        Expression<Func<TTarget, bool>>? targetPredicate = null,
        Expression<Func<TRelation, bool>>? relationPredicate = null)
        where TRelation : notnull => AddExistencePattern(
            relationSet,
            targetPredicate,
            relationPredicate,
            negated: false);

    /// <summary>
    /// Retains result nodes only when no related node satisfies the supplied predicates.
    /// </summary>
    /// <typeparam name="TRelation">The relationship payload type.</typeparam>
    /// <typeparam name="TTarget">The related target node type.</typeparam>
    /// <param name="relationSet">The relationship pattern correlated with the current result node.</param>
    /// <param name="targetPredicate">An optional predicate over the related target node.</param>
    /// <param name="relationPredicate">An optional predicate over the relationship payload.</param>
    /// <returns>A new query containing a correlated not-exists condition.</returns>
    /// <example>
    /// <code>
    /// var customersWithoutRefunds = context.Customers.Query()
    ///     .WhereNotExists(context.CustomerRefunds);
    /// </code>
    /// </example>
    public GraphQuery<TNode> WhereNotExists<TRelation, TTarget>(
        RelationSet<TNode, TRelation, TTarget> relationSet,
        Expression<Func<TTarget, bool>>? targetPredicate = null,
        Expression<Func<TRelation, bool>>? relationPredicate = null)
        where TRelation : notnull => AddExistencePattern(
            relationSet,
            targetPredicate,
            relationPredicate,
            negated: true);

    /// <summary>
    /// Begins a provider-native analytics operation over this node selection and a same-node relationship type.
    /// </summary>
    /// <example>
    /// <code>
    /// var ranked = await context.People.Query()
    ///     .Analyze(context.Friendships)
    ///     .PageRank()
    ///     .OnProjection("social")
    ///     .Top(20)
    ///     .ToListAsync();
    /// </code>
    /// </example>
    public GraphAnalyticsBuilder<TNode, TRelation> Analyze<TRelation>(
        RelationSet<TNode, TRelation, TNode> relationSet)
        where TRelation : notnull
    {
        ArgumentNullException.ThrowIfNull(relationSet);
        return new GraphAnalyticsBuilder<TNode, TRelation>(model, executor, relationSet.Metadata);
    }

    /// <summary>Begins an unweighted shortest-path query between two strongly typed node selectors.</summary>
    public GraphShortestPathQuery<TNode, TRelation> ShortestPathTo<TRelation>(
        GraphQuery<TNode> target,
        RelationSet<TNode, TRelation, TNode> relationSet)
        where TRelation : notnull
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(relationSet);
        if (model.Traversals.Count != 0 || target.model.Traversals.Count != 0)
        {
            throw new InvalidOperationException("Shortest-path endpoints must be node selectors without traversals.");
        }
        var rebasedTarget = Rebase(target.model, model.Parameters.Count);
        var pathModel = new GraphAnalyticsQueryModel(
            GraphAnalyticsAlgorithm.ShortestPath,
            GraphAnalyticsFamily.PathFinding,
            model,
            relationSet.Metadata.Name,
            relationSet.Metadata.Directed,
            "nodal",
            TargetNodes: rebasedTarget);
        return new GraphShortestPathQuery<TNode, TRelation>(
            pathModel, executor, relationSet.Metadata, propertyMappings);
    }

    /// <summary>Projects materialized nodes into a caller-defined result shape.</summary>
    public GraphProjectedQuery<TNode, TResult> Select<TResult>(Expression<Func<TNode, TResult>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return new GraphProjectedQuery<TNode, TResult>(this, selector.Compile());
    }

    /// <summary>
    /// Starts a provider-side scalar or aggregate result-row projection.
    /// </summary>
    /// <returns>A row-query builder over the current result alias.</returns>
    /// <example>
    /// <code>
    /// var summary = await context.Orders.Query("order")
    ///     .ToRows()
    ///     .Count("orderCount")
    ///     .Sum("totalValue", order => order.Total)
    ///     .ToListAsync();
    /// </code>
    /// </example>
    public GraphRowQuery<TNode> ToRows() => new(model, executor, propertyMappings);

    /// <summary>Combines compatible node queries and removes duplicate result nodes.</summary>
    public GraphSetQuery<TNode> Union(GraphQuery<TNode> other) => CreateSetOperation(other, GraphSetOperationKind.Union);

    /// <summary>Combines compatible node queries while preserving duplicate result nodes.</summary>
    public GraphSetQuery<TNode> UnionAll(GraphQuery<TNode> other) => CreateSetOperation(other, GraphSetOperationKind.UnionAll);

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

    /// <summary>Executes the query and returns every bound node and relationship as a normalized subgraph.</summary>
    public ValueTask<GraphQueryResult> ToSubgraphAsync(CancellationToken cancellationToken = default)
    {
        if (executor is null)
        {
            throw new InvalidOperationException(
                "This query is not attached to a NodalContext. Use ToQueryModel for compiler-only scenarios.");
        }

        return executor.ExecuteSubgraphAsync(
            model with { Projection = GraphQueryProjection.Subgraph }, cancellationToken);
    }

    /// <summary>Streams results through an asynchronous enumerable.</summary>
    public async IAsyncEnumerable<TNode> AsAsyncEnumerable(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var item in await ToListAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
        }
    }

    /// <summary>Returns the first result and throws when the query is empty.</summary>
    public async ValueTask<TNode> FirstAsync(CancellationToken cancellationToken = default)
    {
        var results = await Copy(model with { Limit = 1 }).ToListAsync(cancellationToken).ConfigureAwait(false);
        return results.Count == 0 ? throw new InvalidOperationException("Sequence contains no elements.") : results[0];
    }

    /// <summary>Returns the first result or the default value when the query is empty.</summary>
    public async ValueTask<TNode?> FirstOrDefaultAsync(CancellationToken cancellationToken = default)
    {
        var results = await Copy(model with { Limit = 1 }).ToListAsync(cancellationToken).ConfigureAwait(false);
        return results.Count == 0 ? default : results[0];
    }

    /// <summary>Returns the only result and verifies that no second result exists.</summary>
    public async ValueTask<TNode> SingleAsync(CancellationToken cancellationToken = default) =>
        (await Copy(model with { Limit = 2 }).ToListAsync(cancellationToken).ConfigureAwait(false)).Single();

    /// <summary>Returns the only result or the default value and verifies uniqueness.</summary>
    public async ValueTask<TNode?> SingleOrDefaultAsync(CancellationToken cancellationToken = default) =>
        (await Copy(model with { Limit = 2 }).ToListAsync(cancellationToken).ConfigureAwait(false)).SingleOrDefault();

    /// <summary>Determines whether at least one matching result exists.</summary>
    public async ValueTask<bool> AnyAsync(CancellationToken cancellationToken = default) =>
        (await Copy(model with { Limit = 1 }).ToListAsync(cancellationToken).ConfigureAwait(false)).Count != 0;

    /// <summary>Counts matching results.</summary>
    public async ValueTask<int> CountAsync(CancellationToken cancellationToken = default)
    {
        if (model.Offset is not null || model.Limit is not null)
        {
            return (await ToListAsync(cancellationToken).ConfigureAwait(false)).Count;
        }

        if (executor is null)
        {
            throw new InvalidOperationException(
                "This query is not attached to a NodalContext. Use ToQueryModel for compiler-only scenarios.");
        }

        return await executor.ExecuteCountAsync(
            model with { Projection = GraphQueryProjection.Count, Orderings = [] }, cancellationToken)
            .ConfigureAwait(false);
    }

    private GraphQuery<TNode> SetOrdering<TProperty>(
        Expression<Func<TNode, TProperty>> selector,
        GraphSortDirection direction,
        bool append)
    {
        var ordering = new GraphOrdering(
            GraphExpressionTranslator.TranslateProperty(selector, propertyMappings),
            model.ResultAlias,
            direction);
        var existing = model.EffectiveOrderings;
        if (append && existing.Count == 0)
        {
            throw new InvalidOperationException("ThenBy requires a preceding OrderBy or OrderByDescending call.");
        }

        return Copy(model with { Orderings = append ? [.. existing, ordering] : [ordering] });
    }

    private GraphQuery<TNode> Copy(GraphQueryModel next) => new(next, executor, propertyMappings);

    private GraphSetQuery<TNode> CreateSetOperation(GraphQuery<TNode> other, GraphSetOperationKind kind)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (!ReferenceEquals(executor, other.executor) || model.ResultNodeType != other.model.ResultNodeType || model.ResultAlias != other.model.ResultAlias ||
            model.Projection != GraphQueryProjection.Node || other.model.Projection != GraphQueryProjection.Node)
        {
            throw new ArgumentException("Set-operation operands must be compatible node queries from the same context and alias.", nameof(other));
        }
        var rebasedOther = Rebase(other.model, model.Parameters.Count);
        var outer = new GraphQueryModel(model.ResultNodeType, model.ResultAlias, null, [], null, []);
        return new GraphSetQuery<TNode>(new GraphSetOperation(kind, model, rebasedOther), executor, propertyMappings, outer);
    }

    private GraphQuery<TNode> AddExistencePattern<TRelation, TTarget>(
        RelationSet<TNode, TRelation, TTarget> relationSet,
        Expression<Func<TTarget, bool>>? targetPredicate,
        Expression<Func<TRelation, bool>>? relationPredicate,
        bool negated)
        where TRelation : notnull
    {
        ArgumentNullException.ThrowIfNull(relationSet);
        var parameterOffset = model.Parameters.Count;
        var targetMappings = relationSet.TargetMetadata.Properties.ToDictionary(
            property => property.Key,
            property => property.Value.Name);
        var relationMappings = relationSet.Metadata.Properties.ToDictionary(
            property => property.Key,
            property => property.Value.Name);
        var translatedTarget = targetPredicate is null
            ? null
            : GraphExpressionTranslator.Translate(targetPredicate, parameterOffset, targetMappings);
        var translatedRelation = relationPredicate is null
            ? null
            : GraphExpressionTranslator.Translate(
                relationPredicate,
                parameterOffset + (translatedTarget?.Parameters.Count ?? 0),
                relationMappings);
        var index = model.EffectiveExistencePatterns.Count + 1;
        var pattern = new GraphExistencePattern(
            relationSet.Metadata.Name,
            relationSet.TargetMetadata.Name,
            model.ResultAlias,
            $"existsRelation{index}",
            $"existsNode{index}",
            relationSet.Metadata.Directed
                ? GraphTraversalDirection.Outgoing
                : GraphTraversalDirection.Undirected,
            translatedTarget?.Predicate,
            translatedRelation?.Predicate,
            negated);
        return Copy(model with
        {
            Parameters = [.. model.Parameters, .. (translatedTarget?.Parameters ?? []), .. (translatedRelation?.Parameters ?? [])],
            ExistencePatterns = [.. model.EffectiveExistencePatterns, pattern],
        });
    }

    private GraphQuery<TTarget> AppendTraversal<TTarget>(
        string relationType,
        GraphNodeMetadata targetMetadata,
        GraphTraversalDirection direction,
        int minDepth,
        int maxDepth,
        bool optional,
        string? relationAlias = null,
        string? targetAlias = null)
    {
        var index = model.Traversals.Count + 1;
        relationAlias ??= $"relation{index}";
        targetAlias ??= $"node{index}";
        relationAlias = GraphQueryAliases.Validate(relationAlias, nameof(relationAlias));
        targetAlias = GraphQueryAliases.Validate(targetAlias, nameof(targetAlias));
        EnsureAliasesAreAvailable(relationAlias, targetAlias);
        var step = new GraphTraversalStep(
            relationType,
            targetMetadata.Name,
            model.ResultAlias,
            relationAlias,
            targetAlias,
            direction,
            null,
            null,
            minDepth,
            maxDepth,
            optional);
        var mappings = targetMetadata.Properties.ToDictionary(
            property => property.Key,
            property => property.Value.Name);
        return new GraphQuery<TTarget>(
            model with { Traversals = [.. model.Traversals, step] },
            executor,
            mappings);
    }

    private void EnsureAliasesAreAvailable(string relationAlias, string targetAlias)
    {
        if (string.Equals(relationAlias, targetAlias, StringComparison.Ordinal))
        {
            throw new ArgumentException("Relationship and target aliases must be distinct.", nameof(targetAlias));
        }

        var aliases = new HashSet<string>(StringComparer.Ordinal)
        {
            model.Alias,
        };
        foreach (var step in model.Traversals.Concat(model.EffectiveMatchPatterns))
        {
            aliases.Add(step.RelationAlias);
            aliases.Add(step.TargetAlias);
        }
        foreach (var pattern in model.EffectiveExistencePatterns)
        {
            aliases.Add(pattern.RelationAlias);
            aliases.Add(pattern.TargetAlias);
        }

        if (aliases.Contains(relationAlias) || aliases.Contains(targetAlias))
        {
            throw new ArgumentException("Graph query aliases must be unique within one query pattern.", nameof(relationAlias));
        }
    }

    private static GraphPredicate Combine(GraphPredicate? current, GraphPredicate next) =>
        current is null
            ? next
            : new GraphLogicalPredicate(current, GraphLogicalOperator.And, next);

    private static void ValidateDepth(int minDepth, int maxDepth)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minDepth);
        if (maxDepth < minDepth)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDepth), "Maximum depth cannot be less than minimum depth.");
        }
    }

    private static GraphQueryModel Rebase(GraphQueryModel source, int offset)
    {
        if (offset == 0)
        {
            return source;
        }
        var names = source.Parameters.ToDictionary(
            parameter => parameter.Name,
            parameter => $"p{offset + int.Parse(parameter.Name.AsSpan(1), System.Globalization.CultureInfo.InvariantCulture)}");
        return source with
        {
            Predicate = source.Predicate is null ? null : Rename(source.Predicate, names),
            Parameters = source.Parameters.Select(parameter => parameter with { Name = names[parameter.Name] }).ToArray(),
            Traversals = source.Traversals.Select(traversal => traversal with
            {
                Predicate = traversal.Predicate is null ? null : Rename(traversal.Predicate, names),
                RelationPredicate = traversal.RelationPredicate is null ? null : Rename(traversal.RelationPredicate, names),
            }).ToArray(),
            ExistencePatterns = source.EffectiveExistencePatterns.Select(pattern => pattern with
            {
                TargetPredicate = pattern.TargetPredicate is null ? null : Rename(pattern.TargetPredicate, names),
                RelationPredicate = pattern.RelationPredicate is null ? null : Rename(pattern.RelationPredicate, names),
            }).ToArray(),
            MatchPatterns = source.EffectiveMatchPatterns.Select(pattern => pattern with
            {
                Predicate = pattern.Predicate is null ? null : Rename(pattern.Predicate, names),
                RelationPredicate = pattern.RelationPredicate is null ? null : Rename(pattern.RelationPredicate, names),
            }).ToArray(),
            RowProjection = source.RowProjection is null
                ? null
                : source.RowProjection with
                {
                    HavingPredicates = source.RowProjection.EffectiveHavingPredicates.Select(predicate =>
                        predicate with { ParameterName = names[predicate.ParameterName] }).ToArray(),
                },
        };
    }

    private static GraphPredicate Rename(GraphPredicate predicate, IReadOnlyDictionary<string, string> names) => predicate switch
    {
        GraphComparisonPredicate value => value with { ParameterName = names[value.ParameterName] },
        GraphLogicalPredicate value => value with { Left = Rename(value.Left, names), Right = Rename(value.Right, names) },
        GraphNotPredicate value => value with { Operand = Rename(value.Operand, names) },
        GraphStringPredicate value => value with { ParameterName = names[value.ParameterName] },
        GraphInPredicate value => value with { ParameterName = names[value.ParameterName] },
        GraphNullPredicate value => value,
        _ => throw new NotSupportedException($"Predicate '{predicate.GetType().Name}' cannot be rebased."),
    };
}

/// <summary>Represents a strongly typed client projection over a provider-executed graph query.</summary>
public sealed class GraphProjectedQuery<TSource, TResult>(GraphQuery<TSource> source, Func<TSource, TResult> selector)
{
    /// <summary>Executes the graph query and projects each materialized node.</summary>
    public async ValueTask<IReadOnlyList<TResult>> ToListAsync(CancellationToken cancellationToken = default) =>
        (await source.ToListAsync(cancellationToken).ConfigureAwait(false)).Select(selector).ToArray();

    /// <summary>Streams projected results asynchronously.</summary>
    public async IAsyncEnumerable<TResult> AsAsyncEnumerable(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in source.AsAsyncEnumerable(cancellationToken).ConfigureAwait(false))
        {
            yield return selector(item);
        }
    }
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
