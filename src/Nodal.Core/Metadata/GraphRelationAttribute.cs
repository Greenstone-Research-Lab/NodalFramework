namespace Nodal.Core.Metadata;

/// <summary>
/// Maps a POCO type to a provider-neutral graph relationship name.
/// </summary>
/// <param name="name">The relationship type exposed to providers.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class GraphRelationAttribute(string name) : Attribute
{
    /// <summary>Gets the provider-neutral relationship name.</summary>
    public string Name { get; } = string.IsNullOrWhiteSpace(name)
        ? throw new ArgumentException("A graph relationship name is required.", nameof(name))
        : name;

    /// <summary>Gets or sets whether the relationship is directed.</summary>
    public bool Directed { get; init; } = true;
}
