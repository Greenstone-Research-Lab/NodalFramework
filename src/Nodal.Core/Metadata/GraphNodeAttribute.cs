namespace Nodal.Core.Metadata;

/// <summary>
/// Maps a POCO type to a provider-neutral graph node name.
/// </summary>
/// <param name="name">The node type or label exposed to providers.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class GraphNodeAttribute(string name) : Attribute
{
    /// <summary>Gets the provider-neutral node name.</summary>
    public string Name { get; } = string.IsNullOrWhiteSpace(name)
        ? throw new ArgumentException("A graph node name is required.", nameof(name))
        : name;
}
