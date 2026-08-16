namespace Nodal.Core.Metadata;

/// <summary>
/// Maps a POCO property to a provider-neutral graph property name.
/// </summary>
/// <param name="name">The graph property name.</param>
[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class GraphPropertyAttribute(string name) : Attribute
{
    /// <summary>Gets the provider-neutral property name.</summary>
    public string Name { get; } = string.IsNullOrWhiteSpace(name)
        ? throw new ArgumentException("A graph property name is required.", nameof(name))
        : name;
}
