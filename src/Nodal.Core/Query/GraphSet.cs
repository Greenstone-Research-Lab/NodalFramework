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

    /// <summary>Marks a node for deletion, or detaches it when it was newly added.</summary>
    public GraphNodeEntry<TNode> Remove(TNode node) => GetStateManager().Remove(node, GetMetadata());

    private GraphStateManager GetStateManager() => stateManager ?? throw new InvalidOperationException(
        "Mutation operations require a GraphSet obtained from a NodalContext.");

    private GraphNodeMetadata GetMetadata() => metadata ?? throw new InvalidOperationException(
        "Mutation operations require mapped node metadata from a NodalContext.");
}
