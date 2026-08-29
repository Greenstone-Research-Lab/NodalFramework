namespace Nodal.Core.Analytics;

/// <summary>Describes one relationship family in a server-side analytics projection.</summary>
/// <param name="RelationshipType">The mapped relationship type.</param>
/// <param name="Directed">Whether the projected relationship retains direction.</param>
/// <param name="WeightProperty">The optional mapped numeric relationship property.</param>
/// <param name="Coefficient">The positive relationship-family coefficient.</param>
public sealed record GraphProjectionRelationshipDefinition(
    string RelationshipType,
    bool Directed = true,
    string? WeightProperty = null,
    double Coefficient = 1);

/// <summary>Describes a named server-side graph projection.</summary>
/// <param name="Name">Stable projection name.</param>
/// <param name="NodeType">Mapped node label or vertex type.</param>
/// <param name="RelationshipType">Mapped relationship or edge type.</param>
/// <param name="Directed">Whether the projected relationship retains direction.</param>
/// <param name="WeightProperty">Optional mapped numeric relationship property.</param>
/// <param name="Relationships">Optional canonical multi-relation projection descriptors.</param>
public sealed record GraphProjectionDefinition(
    string Name,
    string NodeType,
    string RelationshipType,
    bool Directed = true,
    string? WeightProperty = null,
    IReadOnlyList<GraphProjectionRelationshipDefinition>? Relationships = null)
{
    /// <summary>Preserves the original single-relation constructor contract.</summary>
    public GraphProjectionDefinition(
        string Name,
        string NodeType,
        string RelationshipType,
        bool Directed,
        string? WeightProperty)
        : this(Name, NodeType, RelationshipType, Directed, WeightProperty, null)
    {
    }

    /// <summary>Preserves the original single-relation deconstruction contract.</summary>
    public void Deconstruct(
        out string Name,
        out string NodeType,
        out string RelationshipType,
        out bool Directed,
        out string? WeightProperty)
    {
        Name = this.Name;
        NodeType = this.NodeType;
        RelationshipType = this.RelationshipType;
        Directed = this.Directed;
        WeightProperty = this.WeightProperty;
    }

    /// <summary>Gets multi-relation descriptors or the legacy single relationship descriptor.</summary>
    public IReadOnlyList<GraphProjectionRelationshipDefinition> EffectiveRelationships => Relationships is { Count: > 0 }
        ? Relationships
        : [new GraphProjectionRelationshipDefinition(RelationshipType, Directed, WeightProperty)];
}

/// <summary>Reports analytics features observed or declared by the active deployment.</summary>
/// <param name="ProviderVersion">Runtime analytics component or database version, when discoverable.</param>
/// <param name="Procedures">Provider-native procedure or installed-query names.</param>
/// <param name="Projections">Existing named analytics projections.</param>
/// <param name="Algorithms">Provider-neutral algorithms available in this deployment.</param>
/// <param name="IsLiveDiscovery">Whether the snapshot was obtained from the server rather than configuration.</param>
public sealed record GraphAnalyticsRuntimeSnapshot(
    string? ProviderVersion,
    IReadOnlySet<string> Procedures,
    IReadOnlySet<string> Projections,
    IReadOnlySet<GraphAnalyticsAlgorithm> Algorithms,
    bool IsLiveDiscovery);

/// <summary>Manages and discovers deployment-specific graph analytics resources.</summary>
public interface IGraphAnalyticsRuntime
{
    /// <summary>Discovers analytics procedures and projections, using the provider's bounded cache.</summary>
    ValueTask<GraphAnalyticsRuntimeSnapshot> DiscoverAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a projection when it does not already exist and safely reuses an existing projection.</summary>
    ValueTask EnsureProjectionAsync(
        GraphProjectionDefinition projection,
        CancellationToken cancellationToken = default);

    /// <summary>Drops a named projection when present.</summary>
    ValueTask DropProjectionAsync(string name, CancellationToken cancellationToken = default);
}

/// <summary>Supplies an optional provider-specific analytics runtime.</summary>
public interface IGraphAnalyticsRuntimeProvider
{
    /// <summary>Gets deployment discovery and projection lifecycle services.</summary>
    IGraphAnalyticsRuntime AnalyticsRuntime { get; }
}
