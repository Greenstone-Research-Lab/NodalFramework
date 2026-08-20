using Nodal.Core.Metadata;

namespace Nodal.Samples.SocialGraph;

/// <summary>
/// Represents a person stored as a provider-neutral graph node.
/// </summary>
/// <param name="id">The stable domain identifier.</param>
/// <param name="name">The display name.</param>
[GraphNode("Person")]
public sealed class Person(string id, string name)
{
    /// <summary>Gets the stable domain identifier.</summary>
    [GraphKey]
    public string Id { get; } = id;

    /// <summary>Gets or sets the display name.</summary>
    public string Name { get; set; } = name;
}
