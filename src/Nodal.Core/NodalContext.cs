using Nodal.Core.ChangeTracking;
using Nodal.Core.Execution;
using Nodal.Core.Metadata;
using Nodal.Core.Mutations;
using Nodal.Core.Query;

namespace Nodal.Core;

/// <summary>
/// Coordinates a strongly typed graph model with the selected database provider.
/// </summary>
public abstract class NodalContext
{
    private readonly Lazy<NodalModel> model;
    private readonly IGraphQueryExecutor queryExecutor;
    private readonly IGraphProvider provider;
    private readonly Lazy<GraphStateManager> stateManager;
    private readonly Lazy<GraphChangeTracker> changeTracker;

    /// <summary>
    /// Initializes a context with a graph database provider.
    /// </summary>
    protected NodalContext(IGraphProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        this.provider = provider;
        model = new Lazy<NodalModel>(BuildModel, LazyThreadSafetyMode.ExecutionAndPublication);
        stateManager = new Lazy<GraphStateManager>(
            () => new GraphStateManager(),
            LazyThreadSafetyMode.ExecutionAndPublication);
        queryExecutor = new ProviderGraphQueryExecutor(provider, stateManager.Value, () => Model);
        changeTracker = new Lazy<GraphChangeTracker>(
            () => new GraphChangeTracker(stateManager.Value),
            LazyThreadSafetyMode.ExecutionAndPublication);
        Database = new NodalDatabaseFacade(provider);
    }

    /// <summary>
    /// Gets the immutable graph model configured by this context.
    /// </summary>
    public NodalModel Model => model.Value;

    /// <summary>Gets the node and relationship entries managed by this context.</summary>
    public GraphChangeTracker ChangeTracker => changeTracker.Value;

    /// <summary>Gets database-wide services such as migration planning and execution.</summary>
    public NodalDatabaseFacade Database { get; }

    /// <summary>
    /// Gets a query root for a configured node type.
    /// </summary>
    protected GraphSet<TNode> Set<TNode>()
    {
        var metadata = Model.GetNode<TNode>();
        var properties = metadata.Properties.ToDictionary(
            property => property.Key,
            property => property.Value.Name);
        return new GraphSet<TNode>(metadata.Name, queryExecutor, properties, stateManager.Value, metadata);
    }

    /// <summary>
    /// Gets a strongly typed relationship root for configured source, relationship, and target types.
    /// </summary>
    protected RelationSet<TSource, TRelation, TTarget> Relations<TSource, TRelation, TTarget>()
        where TRelation : notnull => new(
            Model.GetRelation<TSource, TRelation, TTarget>(),
            Model.GetNode<TSource>(),
            Model.GetNode<TTarget>(),
            stateManager.Value);

    /// <summary>
    /// Plans and commits all pending node and relationship mutations through the selected provider.
    /// Entry states are accepted only after the provider confirms success.
    /// </summary>
    public async ValueTask<GraphSaveResult> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plan = GraphMutationPlanner.Create(stateManager.Value.Entries);
        if (plan.IsEmpty)
        {
            return new GraphSaveResult(0, 0, true, new GraphChangeSet([]));
        }

        if (provider is not IGraphMutationProvider mutationProvider)
        {
            throw new NotSupportedException(
                $"Graph provider '{provider.GetType().Name}' does not support mutation execution.");
        }

        var result = await mutationProvider.MutationExecutor
            .ExecuteAsync(plan, cancellationToken)
            .ConfigureAwait(false);
        stateManager.Value.AcceptAllChanges();
        return new GraphSaveResult(
            result.AffectedNodes,
            result.AffectedRelations,
            result.IsAtomic,
            new GraphChangeSet(plan.Operations.ToArray()));
    }

    /// <summary>
    /// Configures domain node and relationship mappings.
    /// </summary>
    protected virtual void OnModelCreating(NodalModelBuilder modelBuilder)
    {
    }

    private NodalModel BuildModel()
    {
        var builder = new NodalModelBuilder();
        builder.DiscoverContext(GetType());
        OnModelCreating(builder);
        return builder.Build();
    }
}
