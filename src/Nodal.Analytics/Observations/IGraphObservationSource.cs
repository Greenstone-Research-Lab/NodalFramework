namespace Nodal.Analytics.Observations;

/// <summary>Executes bounded graph queries and returns canonical observations.</summary>
public interface IGraphObservationSource
{
    /// <summary>Executes a request and returns one complete canonical observation.</summary>
    /// <param name="request">The bounded observation request.</param>
    /// <param name="cancellationToken">A token used to cancel provider execution.</param>
    /// <returns>The complete immutable observation.</returns>
    ValueTask<GraphObservation> ObserveAsync(
        GraphObservationRequest request,
        CancellationToken cancellationToken = default);
}
