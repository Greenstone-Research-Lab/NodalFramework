using Nodal.Core.Query;

namespace Nodal.Analytics.Observations;

/// <summary>Describes one bounded provider query that produces a canonical observation.</summary>
/// <param name="Query">The provider-neutral subgraph query.</param>
/// <param name="Options">The observation bounds and explicit property projections.</param>
/// <param name="Timeout">An optional positive execution timeout.</param>
public sealed record GraphObservationRequest(
    GraphQueryModel Query,
    GraphObservationOptions Options,
    TimeSpan? Timeout = null);
