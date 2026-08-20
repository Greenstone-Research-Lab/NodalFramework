using Nodal.Core.Metadata;

namespace Nodal.Samples.SocialGraph;

/// <summary>
/// Describes when one person began knowing another person.
/// </summary>
/// <param name="sinceYear">The year in which the relationship began.</param>
[GraphRelation("KNOWS", Directed = true)]
public sealed class Knows(int sinceYear)
{
    /// <summary>Gets or sets the year in which the relationship began.</summary>
    public int SinceYear { get; set; } = sinceYear;
}
