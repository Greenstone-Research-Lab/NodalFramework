namespace Nodal.Analytics.Observations;

/// <summary>Represents rejection of a normalized result that exceeds an observation bound.</summary>
public sealed class GraphObservationLimitExceededException : InvalidOperationException
{
    /// <summary>Initializes a new limit exception without exposing graph property values.</summary>
    /// <param name="elementKind">The bounded element kind.</param>
    /// <param name="actualCount">The normalized result count.</param>
    /// <param name="maximumCount">The configured maximum count.</param>
    public GraphObservationLimitExceededException(string elementKind, int actualCount, int maximumCount)
        : base($"Observation {elementKind} count {actualCount} exceeds the configured maximum {maximumCount}.")
    {
        ElementKind = elementKind;
        ActualCount = actualCount;
        MaximumCount = maximumCount;
    }

    /// <summary>Gets the bounded element kind.</summary>
    public string ElementKind { get; }

    /// <summary>Gets the rejected element count.</summary>
    public int ActualCount { get; }

    /// <summary>Gets the configured maximum count.</summary>
    public int MaximumCount { get; }
}
