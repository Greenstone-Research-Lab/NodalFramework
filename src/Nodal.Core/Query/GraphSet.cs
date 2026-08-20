using System.Linq.Expressions;
using Nodal.Core.ChangeTracking;
using Nodal.Core.Execution;
using Nodal.Core.Metadata;

namespace Nodal.Core.Query;

/// <summary>
/// Exposes the query root for a graph node type.
/// </summary>
/// <typeparam name="TNode">The node type represented by this set.</typeparam>
public sealed class GraphSet<TNode>
{
    private readonly string nodeType;
    private readonly IGraphQueryExecutor? executor;
    private readonly IReadOnlyDictionary<string, string>? propertyMappings;
    private readonly GraphStateManager? stateManager;
    private readonly GraphNodeMetadata? metadata;

    /// <summary>
    /// Initializes a set using the CLR type name as its graph label.
    /// </summary>
    public GraphSet()
        : this(typeof(TNode).Name, null, null, null, null)
    {
    }

    /// <summary>
    /// Initializes a set with an explicit graph node type.
    /// </summary>
    /// <param name="nodeType">The provider-independent graph node type.</param>
    public GraphSet(string nodeType)
        : this(nodeType, null, null, null, null)
    {
    }

    internal GraphSet(
        string nodeType,
        IGraphQueryExecutor? executor,
        IReadOnlyDictionary<string, string>? propertyMappings,
        GraphStateManager? stateManager,
        GraphNodeMetadata? metadata)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);
        this.nodeType = nodeType;
        this.executor = executor;
        this.propertyMappings = propertyMappings;
        this.stateManager = stateManager;
        this.metadata = metadata;
    }

    /// <summary>
    /// Starts an unfiltered graph query.
    /// </summary>
    public GraphQuery<TNode> Query() => new(
        new GraphQueryModel(nodeType, "node", null, [], null, []),
        executor,
        propertyMappings);

    /// <summary>
    /// Starts a graph query with a strongly typed predicate.
    /// </summary>
    /// <param name="predicate">The initial node predicate.</param>
    public GraphQuery<TNode> Match(Expression<Func<TNode, bool>> predicate) => Query().Where(predicate);

    /// <summary>Adds a node to the current unit of work.</summary>
    public GraphNodeEntry<TNode> Add(TNode node) => GetStateManager().Add(node, GetMetadata());

    /// <summary>Marks a node for a complete mapped-property update.</summary>
    public GraphNodeEntry<TNode> Update(TNode node) => GetStateManager().Update(node, GetMetadata());

    /// <summary>Begins tracking an existing node without scheduling a write.</summary>
    public GraphNodeEntry<TNode> Attach(TNode node) => GetStateManager().Attach(node, GetMetadata());

    /// <summary>Marks a node for deletion, or detaches it when it was newly added.</summary>
    public GraphNodeEntry<TNode> Remove(TNode node) => GetStateManager().Remove(node, GetMetadata());

    /// <summary>Reloads every writable mapped property from the provider and refreshes its snapshot.</summary>
    public async ValueTask ReloadAsync(TNode node, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        var mapped = GetMetadata();
        var runtimeExecutor = executor ?? throw new InvalidOperationException(
            "Reload requires a GraphSet obtained from a NodalContext.");
        var key = mapped.ClrType.GetProperty(mapped.KeyProperty)!.GetValue(node) ??
            throw new InvalidOperationException($"Key property '{mapped.KeyProperty}' cannot be null.");
        var query = new GraphQueryModel(
            nodeType,
            "node",
            new GraphComparisonPredicate(
                mapped.Properties[mapped.KeyProperty].Name,
                GraphComparisonOperator.Equal,
                "p0"),
            [new GraphQueryParameter("p0", key, key.GetType())],
            2,
            [],
            TrackingBehavior: GraphTrackingBehavior.NoTracking);
        var values = await runtimeExecutor.ExecuteAsync<TNode>(query, cancellationToken).ConfigureAwait(false);
        var fresh = values.Single();
        foreach (var property in mapped.Properties.Values)
        {
            var clrProperty = mapped.ClrType.GetProperty(property.ClrName)!;
            if (clrProperty.SetMethod is not null)
            {
                clrProperty.SetValue(node, clrProperty.GetValue(fresh));
            }
        }

        GetStateManager().AcceptReload(node);
    }

    private GraphStateManager GetStateManager() => stateManager ?? throw new InvalidOperationException(
        "Mutation operations require a GraphSet obtained from a NodalContext.");

    private GraphNodeMetadata GetMetadata() => metadata ?? throw new InvalidOperationException(
        "Mutation operations require mapped node metadata from a NodalContext.");
}
