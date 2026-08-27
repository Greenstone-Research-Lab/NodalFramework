using Nodal.Core.Migrations;

namespace Nodal.Core.Query;

/// <summary>
/// Identifies a portable query feature that a graph provider has explicitly verified.
/// </summary>
[Flags]
public enum GraphQueryCapability
{
    /// <summary>Does not identify a query feature.</summary>
    None = 0,

    /// <summary>Supports optional relationship traversal semantics.</summary>
    OptionalTraversal = 1 << 0,

    /// <summary>Supports repeated relationship traversal with inclusive depth bounds.</summary>
    VariableLengthTraversal = 1 << 1,

    /// <summary>Supports vertex-simple path semantics.</summary>
    SimplePath = 1 << 2,

    /// <summary>Supports multiple named graph patterns in one query.</summary>
    MultiplePatterns = 1 << 3,

    /// <summary>Supports a subquery that is correlated to an outer graph pattern.</summary>
    CorrelatedSubquery = 1 << 4,

    /// <summary>Supports provider-side row projection without materializing source nodes first.</summary>
    ServerSideProjection = 1 << 5,

    /// <summary>Supports provider-side grouping and aggregate functions.</summary>
    Aggregation = 1 << 6,

    /// <summary>Supports portable set combination operations such as union.</summary>
    SetOperations = 1 << 7,
}

/// <summary>
/// Describes the query features verified for a concrete provider installation.
/// </summary>
/// <remarks>
/// A provider must advertise only features whose generated commands and result semantics have
/// been verified against the stated provider version. Compiler potential alone is not evidence
/// that a feature is available in a deployed installation.
/// </remarks>
public sealed record GraphQueryCapabilities
{
    /// <summary>Gets the provider name shown in capability diagnostics.</summary>
    public required string ProviderName { get; init; }

    /// <summary>Gets the provider version against which the feature set was verified.</summary>
    public required string TestedProviderVersion { get; init; }

    /// <summary>Gets the complete verified feature set.</summary>
    public required GraphQueryCapability Features { get; init; }

    /// <summary>
    /// Determines whether every requested feature is available.
    /// </summary>
    /// <param name="capability">One or more query features to test.</param>
    /// <returns><see langword="true"/> when every requested feature is available; otherwise, <see langword="false"/>.</returns>
    public bool Supports(GraphQueryCapability capability) => (Features & capability) == capability;
}

/// <summary>
/// Exposes the verified portable query features available from a graph provider.
/// </summary>
public interface IGraphQueryCapabilityProvider
{
    /// <summary>Gets the feature set available to commands issued through this provider.</summary>
    GraphQueryCapabilities QueryCapabilities { get; }
}

/// <summary>
/// Validates that a provider can execute a provider-neutral query before transport execution.
/// </summary>
public static class GraphQueryPreflight
{
    /// <summary>
    /// Validates every feature required by <paramref name="query"/> against
    /// <paramref name="capabilities"/>.
    /// </summary>
    /// <param name="query">The immutable query model to validate.</param>
    /// <param name="capabilities">The verified feature set exposed by the selected provider.</param>
    /// <exception cref="NodalCapabilityNotSupportedException">
    /// Thrown when the provider cannot execute a requested query feature.
    /// </exception>
    public static void Validate(GraphQueryModel query, GraphQueryCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(capabilities);

        if (query.Traversals.Any(traversal => traversal.Optional))
        {
            Require(capabilities, GraphQueryCapability.OptionalTraversal, "NODAL-QUERY-OPTIONAL-TRAVERSAL");
        }

        if (query.Traversals.Any(traversal => traversal.MinDepth != 1 || traversal.MaxDepth != 1))
        {
            Require(capabilities, GraphQueryCapability.VariableLengthTraversal, "NODAL-QUERY-VARIABLE-LENGTH");
        }

        if (query.CycleBehavior == GraphCycleBehavior.SimplePath)
        {
            Require(capabilities, GraphQueryCapability.SimplePath, "NODAL-QUERY-SIMPLE-PATH");
        }

        if (query.EffectiveExistencePatterns.Count > 0)
        {
            Require(capabilities, GraphQueryCapability.CorrelatedSubquery, "NODAL-QUERY-CORRELATED-SUBQUERY");
        }

        if (query.EffectiveMatchPatterns.Count > 0)
        {
            Require(capabilities, GraphQueryCapability.MultiplePatterns, "NODAL-QUERY-MULTIPLE-PATTERNS");
        }

        if (query.Projection == GraphQueryProjection.Row)
        {
            Require(capabilities, GraphQueryCapability.ServerSideProjection, "NODAL-QUERY-SERVER-SIDE-PROJECTION");
            if (query.RowProjection?.Columns.Any(column => column.Kind != GraphRowColumnKind.Property) == true)
            {
                Require(capabilities, GraphQueryCapability.Aggregation, "NODAL-QUERY-AGGREGATION");
            }
        }
        if (query.SetOperation is not null)
        {
            Require(capabilities, GraphQueryCapability.SetOperations, "NODAL-QUERY-SET-OPERATIONS");
            Validate(query.SetOperation.Left, capabilities);
            Validate(query.SetOperation.Right, capabilities);
        }
    }

    private static void Require(
        GraphQueryCapabilities capabilities,
        GraphQueryCapability capability,
        string code)
    {
        if (capabilities.Supports(capability))
        {
            return;
        }

        throw new NodalCapabilityNotSupportedException(
            capabilities.ProviderName,
            code,
            $"The verified query feature set for provider version '{capabilities.TestedProviderVersion}' does not include '{capability}'.");
    }
}
