namespace Nodal.Neo4j;

using Nodal.Core.Analytics;

/// <summary>
/// Defines the connection settings used to create a pooled Neo4j driver.
/// </summary>
public sealed class Neo4jOptions
{
    /// <summary>Gets or initializes the Bolt endpoint, for example <c>neo4j://localhost:7687</c>.</summary>
    public required Uri Endpoint { get; init; }

    /// <summary>Gets or initializes the Neo4j user name.</summary>
    public required string Username { get; init; }

    /// <summary>Gets or initializes the Neo4j password.</summary>
    public required string Password { get; init; }

    /// <summary>Gets or initializes the optional target database name.</summary>
    public string? Database { get; init; }

    /// <summary>
    /// Gets or initializes whether Enterprise property-existence and property-type
    /// constraints may be emitted. The Community-safe default is <see langword="false"/>.
    /// </summary>
    public bool EnterpriseSchemaConstraintsEnabled { get; init; }

    /// <summary>
    /// Gets or initializes whether the target server has the Neo4j Graph Data Science library available.
    /// Analytics capability checks remain disabled unless this is explicitly enabled.
    /// </summary>
    public bool GraphDataScienceEnabled { get; init; }

    /// <summary>
    /// Gets an optional deployment-specific allow-list obtained from the installed GDS procedures.
    /// When omitted, every compiler-supported GDS algorithm is advertised after GDS is enabled.
    /// </summary>
    public IReadOnlySet<GraphAnalyticsAlgorithm>? AnalyticsAlgorithms { get; init; }

    /// <summary>Gets the lifetime of cached runtime GDS discovery results.</summary>
    public TimeSpan AnalyticsDiscoveryCacheDuration { get; init; } = TimeSpan.FromMinutes(5);
}
