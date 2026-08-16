namespace Nodal.Core.Metadata;

/// <summary>
/// Excludes a POCO property from graph persistence and query translation.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class GraphIgnoreAttribute : Attribute;
