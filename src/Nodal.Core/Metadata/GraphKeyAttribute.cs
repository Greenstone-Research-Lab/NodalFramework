namespace Nodal.Core.Metadata;

/// <summary>
/// Marks the stable domain property used to identify a graph node.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class GraphKeyAttribute : Attribute;
