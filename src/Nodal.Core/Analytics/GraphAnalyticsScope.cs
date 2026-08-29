using System.Globalization;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using Nodal.Core.Execution;
using Nodal.Core.Query;

namespace Nodal.Core.Analytics;

/// <summary>Creates strongly typed provider-native analytics scopes.</summary>
public static class GraphAnalyticsScope
{
    /// <summary>Creates an empty analytics scope with a stable projection name.</summary>
    /// <typeparam name="TNode">The homogeneous node type ranked by the scope.</typeparam>
    /// <param name="name">The provider-neutral projection name.</param>
    /// <returns>An immutable empty scope.</returns>
    public static GraphAnalyticsScope<TNode> For<TNode>(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new GraphAnalyticsScope<TNode>(name, []);
    }
}

/// <summary>Describes one mapped relationship participating in a provider-native analytics scope.</summary>
/// <param name="RelationshipType">The mapped relationship type.</param>
/// <param name="Directed">Whether the provider must retain relationship direction.</param>
/// <param name="WeightProperty">The optional mapped numeric weight property.</param>
/// <param name="Coefficient">The positive coefficient applied to this relationship family.</param>
public sealed record GraphAnalyticsRelationshipDefinition(
    string RelationshipType,
    bool Directed,
    string? WeightProperty = null,
    double Coefficient = 1);

/// <summary>
/// Defines an immutable homogeneous graph projection containing one node type and one or more
/// same-node relationship types.
/// </summary>
/// <typeparam name="TNode">The node type ranked by provider-native analytics.</typeparam>
public sealed class GraphAnalyticsScope<TNode>
{
    /// <summary>Gets the maximum number of relationship families accepted by one scope.</summary>
    public const int MaximumRelationships = 256;

    private readonly IReadOnlyList<GraphAnalyticsRelationshipDefinition> relationships;

    internal GraphAnalyticsScope(string name, IReadOnlyList<GraphAnalyticsRelationshipDefinition> relationships)
    {
        Name = name;
        this.relationships = relationships;
    }

    /// <summary>Gets the stable provider-side projection name.</summary>
    public string Name { get; }

    /// <summary>Gets relationship descriptors in canonical ordinal order.</summary>
    public IReadOnlyList<GraphAnalyticsRelationshipDefinition> Relationships => relationships;

    /// <summary>Adds one unweighted mapped same-node relationship family.</summary>
    public GraphAnalyticsScope<TNode> Include<TRelation>(
        RelationSet<TNode, TRelation, TNode> relationSet,
        double coefficient = 1)
        where TRelation : notnull => IncludeCore(relationSet, null, coefficient);

    /// <summary>Adds one mapped same-node relationship family with a numeric weight property.</summary>
    public GraphAnalyticsScope<TNode> Include<TRelation, TNumber>(
        RelationSet<TNode, TRelation, TNode> relationSet,
        Expression<Func<TRelation, TNumber>> weight,
        double coefficient = 1)
        where TRelation : notnull
    {
        ArgumentNullException.ThrowIfNull(weight);
        ArgumentNullException.ThrowIfNull(relationSet);
        var metadata = relationSet.Metadata;
        var mapped = GraphExpressionTranslator.TranslateProperty(
            weight,
            metadata.Properties.ToDictionary(property => property.Key, property => property.Value.Name));
        var property = metadata.Properties.Values.Single(candidate => candidate.Name == mapped);
        var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
        if (!IsNumeric(type))
        {
            throw new ArgumentException("Analytics weights must use a numeric relationship property.", nameof(weight));
        }

        return IncludeCore(relationSet, mapped, coefficient);
    }

    private GraphAnalyticsScope<TNode> IncludeCore<TRelation>(
        RelationSet<TNode, TRelation, TNode> relationSet,
        string? weightProperty,
        double coefficient)
        where TRelation : notnull
    {
        ArgumentNullException.ThrowIfNull(relationSet);
        if (!double.IsFinite(coefficient) || coefficient <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(coefficient), "A relationship coefficient must be finite and greater than zero.");
        }
        if (relationships.Count >= MaximumRelationships)
        {
            throw new InvalidOperationException($"An analytics scope cannot contain more than {MaximumRelationships} relationship types.");
        }
        if (relationships.Any(item => string.Equals(item.RelationshipType, relationSet.Metadata.Name, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Relationship type '{relationSet.Metadata.Name}' is already included in this analytics scope.");
        }

        var next = relationships
            .Append(new GraphAnalyticsRelationshipDefinition(
                relationSet.Metadata.Name,
                relationSet.Metadata.Directed,
                weightProperty,
                coefficient))
            .OrderBy(item => item.RelationshipType, StringComparer.Ordinal)
            .ToArray();
        return new GraphAnalyticsScope<TNode>(Name, next);
    }

    private static bool IsNumeric(Type type) => type == typeof(byte) || type == typeof(sbyte) ||
        type == typeof(short) || type == typeof(ushort) || type == typeof(int) || type == typeof(uint) ||
        type == typeof(long) || type == typeof(ulong) || type == typeof(float) || type == typeof(double) ||
        type == typeof(decimal);
}

/// <summary>Identifies one provider-native analytics deployment binding.</summary>
public sealed record GraphAnalyticsBindingKey
{
    private GraphAnalyticsBindingKey(
        GraphAnalyticsAlgorithm algorithm,
        string nodeType,
        IReadOnlyList<GraphAnalyticsRelationshipDefinition> relationships,
        string contractVersion,
        string fingerprint)
    {
        Algorithm = algorithm;
        NodeType = nodeType;
        Relationships = relationships;
        ContractVersion = contractVersion;
        Fingerprint = fingerprint;
    }

    /// <summary>Gets the provider-neutral algorithm.</summary>
    public GraphAnalyticsAlgorithm Algorithm { get; }

    /// <summary>Gets the mapped node type.</summary>
    public string NodeType { get; }

    /// <summary>Gets the canonical relationship descriptors.</summary>
    public IReadOnlyList<GraphAnalyticsRelationshipDefinition> Relationships { get; }

    /// <summary>Gets the analytics response-contract version.</summary>
    public string ContractVersion { get; }

    /// <summary>Gets the deterministic lowercase SHA-256 prefix for this binding shape.</summary>
    public string Fingerprint { get; }

    /// <summary>Creates a stable binding identity from an immutable analytics request.</summary>
    public static GraphAnalyticsBindingKey Create(GraphAnalyticsQueryModel query, string contractVersion = "1")
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(contractVersion);
        var relationships = query.EffectiveRelationships
            .OrderBy(item => item.RelationshipType, StringComparer.Ordinal)
            .ToArray();
        if (relationships.Length == 0)
        {
            throw new ArgumentException("An analytics binding requires at least one relationship type.", nameof(query));
        }
        var shape = string.Join('|',
            query.Algorithm,
            query.Nodes.NodeType,
            string.Join(';', relationships.Select(Describe)),
            contractVersion);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(shape)))
            .ToLowerInvariant()[..16];
        return new GraphAnalyticsBindingKey(query.Algorithm, query.Nodes.NodeType, relationships, contractVersion, fingerprint);
    }

    private static string Describe(GraphAnalyticsRelationshipDefinition value) => string.Join(':',
        value.RelationshipType,
        value.Directed ? "directed" : "undirected",
        value.WeightProperty ?? "unweighted",
        value.Coefficient.ToString("R", CultureInfo.InvariantCulture));
}

/// <summary>Validates provider-native analytics scopes before transport execution.</summary>
public interface IGraphAnalyticsScopeCapabilityProvider
{
    /// <summary>Rejects a scope that the active provider deployment cannot execute faithfully.</summary>
    void ValidateAnalyticsScope(GraphAnalyticsQueryModel query);
}

/// <summary>Builds provider-native analytics operations over a multi-relation scope.</summary>
public sealed class GraphAnalyticsScopeBuilder<TNode>
{
    private readonly GraphQueryModel nodes;
    private readonly IGraphQueryExecutor? executor;
    private readonly GraphAnalyticsScope<TNode> scope;

    internal GraphAnalyticsScopeBuilder(
        GraphQueryModel nodes,
        IGraphQueryExecutor? executor,
        GraphAnalyticsScope<TNode> scope)
    {
        this.nodes = nodes;
        this.executor = executor;
        this.scope = scope;
    }

    /// <summary>Creates a PageRank operation over every relationship in the scope.</summary>
    public GraphAnalyticsScopeQuery<TNode> PageRank() => Using(GraphAnalyticsAlgorithm.PageRank);

    /// <summary>Creates a PageRank operation with validated provider-neutral settings.</summary>
    public GraphAnalyticsScopeQuery<TNode> PageRank(PageRankOptions options) => PageRank().Configure(options);

    /// <summary>Creates a provider-native non-path algorithm over every relationship in the scope.</summary>
    public GraphAnalyticsScopeQuery<TNode> Using(GraphAnalyticsAlgorithm algorithm)
    {
        if (scope.Relationships.Count == 0)
        {
            throw new InvalidOperationException("An analytics scope must include at least one relationship type.");
        }
        var family = algorithm switch
        {
            >= GraphAnalyticsAlgorithm.ArticleRank and <= GraphAnalyticsAlgorithm.PageRank => GraphAnalyticsFamily.Centrality,
            >= GraphAnalyticsAlgorithm.CliqueCounting and <= GraphAnalyticsAlgorithm.SpeakerListenerLabelPropagation => GraphAnalyticsFamily.CommunityDetection,
            _ => throw new ArgumentException("Path algorithms require typed source and target selectors.", nameof(algorithm)),
        };
        var first = scope.Relationships[0];
        var model = new GraphAnalyticsQueryModel(
            algorithm,
            family,
            nodes,
            first.RelationshipType,
            first.Directed,
            scope.Name,
            first.WeightProperty,
            Relationships: scope.Relationships);
        var binding = GraphAnalyticsBindingKey.Create(model);
        model = model with { ProjectionName = $"{scope.Name}-{binding.Fingerprint}" };
        return new GraphAnalyticsScopeQuery<TNode>(model, executor);
    }
}

/// <summary>Represents an immutable executable provider-native multi-relation analytics operation.</summary>
public sealed class GraphAnalyticsScopeQuery<TNode>
{
    private readonly GraphAnalyticsQueryModel model;
    private readonly IGraphQueryExecutor? executor;

    internal GraphAnalyticsScopeQuery(GraphAnalyticsQueryModel model, IGraphQueryExecutor? executor)
    {
        this.model = model;
        this.executor = executor;
    }

    /// <summary>Limits rows returned after provider-native ranking.</summary>
    public GraphAnalyticsScopeQuery<TNode> Top(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        return Copy(model with { Limit = count });
    }

    /// <summary>Applies strongly typed PageRank settings.</summary>
    public GraphAnalyticsScopeQuery<TNode> Configure(PageRankOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (model.Algorithm != GraphAnalyticsAlgorithm.PageRank)
        {
            throw new InvalidOperationException("PageRank options cannot configure another algorithm.");
        }
        if (options.DampingFactor is <= 0 or >= 1 || options.MaximumIterations <= 0 ||
            options.Tolerance <= 0 || options.Concurrency is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "PageRank options contain an invalid positive range.");
        }
        return Copy(model with
        {
            Configuration = new Dictionary<string, object?>
            {
                ["dampingFactor"] = options.DampingFactor,
                ["maxIterations"] = options.MaximumIterations,
                ["tolerance"] = options.Tolerance,
                ["concurrency"] = options.Concurrency,
            },
        });
    }

    /// <summary>Produces the immutable provider-neutral request model.</summary>
    public GraphAnalyticsQueryModel ToQueryModel() => model;

    /// <summary>Executes the provider-native algorithm and materializes canonical result rows.</summary>
    public ValueTask<IReadOnlyList<GraphAnalyticsRecord<TNode>>> ToListAsync(CancellationToken cancellationToken = default)
    {
        if (executor is null)
        {
            throw new InvalidOperationException("This analytics query is not attached to a NodalContext.");
        }
        return executor.ExecuteAnalyticsAsync<TNode>(model, cancellationToken);
    }

    private GraphAnalyticsScopeQuery<TNode> Copy(GraphAnalyticsQueryModel next) => new(next, executor);
}
