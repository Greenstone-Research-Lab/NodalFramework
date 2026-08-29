namespace Nodal.Analytics.Observations;

/// <summary>
/// Defines bounds and explicit property projections for observation materialization.
/// </summary>
/// <example>
/// <code>
/// var options = new GraphObservationOptions
/// {
///     MaxNodes = 1_000,
///     NodeProperties = new HashSet&lt;string&gt;(StringComparer.Ordinal) { "name" },
/// };
/// </code>
/// </example>
public sealed record GraphObservationOptions
{
    /// <summary>Gets the default maximum number of nodes.</summary>
    public const int DefaultMaxNodes = 10_000;

    /// <summary>Gets the default maximum number of relationships.</summary>
    public const int DefaultMaxRelations = 50_000;

    /// <summary>Gets the default maximum number of items in one projected collection.</summary>
    public const int DefaultMaxPropertyCollectionItems = 10_000;

    /// <summary>Gets the default maximum depth of a projected property value.</summary>
    public const int DefaultMaxPropertyDepth = 16;

    /// <summary>Gets the maximum number of nodes accepted from one normalized result.</summary>
    public int MaxNodes { get; init; } = DefaultMaxNodes;

    /// <summary>Gets the maximum number of relationships accepted from one normalized result.</summary>
    public int MaxRelations { get; init; } = DefaultMaxRelations;

    /// <summary>Gets the maximum number of items copied from one projected collection.</summary>
    public int MaxPropertyCollectionItems { get; init; } = DefaultMaxPropertyCollectionItems;

    /// <summary>Gets the maximum recursive depth of a projected property value.</summary>
    public int MaxPropertyDepth { get; init; } = DefaultMaxPropertyDepth;

    /// <summary>Gets the node property names permitted to enter the observation.</summary>
    public IReadOnlySet<string> NodeProperties { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Gets the relationship property names permitted to enter the observation.</summary>
    public IReadOnlySet<string> RelationProperties { get; init; } = new HashSet<string>(StringComparer.Ordinal);
}
