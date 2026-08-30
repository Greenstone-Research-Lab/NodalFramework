namespace Nodal.Core.Modeling;

/// <summary>Defines canonical graph model format versions.</summary>
public static class GraphModelFormat
{
    /// <summary>Gets the current canonical descriptor format version.</summary>
    public const string CurrentVersion = "1.0";
}

/// <summary>Describes one portable graph property.</summary>
/// <param name="Name">The graph property name.</param>
/// <param name="ClrName">The suggested CLR property name.</param>
/// <param name="ValueKind">The portable value kind.</param>
/// <param name="IsNullable">Whether null is permitted.</param>
/// <param name="IsCollection">Whether the value is a bounded collection.</param>
/// <param name="ItemKind">The collection item kind when applicable.</param>
/// <param name="ProviderAnnotations">Optional non-semantic provider hints.</param>
public sealed record GraphPropertyDescriptor(
    string Name,
    string ClrName,
    GraphValueKind ValueKind,
    bool IsNullable,
    bool IsCollection = false,
    GraphValueKind? ItemKind = null,
    IReadOnlyDictionary<string, string>? ProviderAnnotations = null);

/// <summary>Describes the ordered properties that form a node key.</summary>
/// <param name="Properties">Ordered graph property names.</param>
public sealed record GraphKeyDescriptor(IReadOnlyList<string> Properties);

/// <summary>Describes one provider-neutral graph node type.</summary>
/// <param name="Id">Stable node identity in the descriptor.</param>
/// <param name="Name">Graph node type or label.</param>
/// <param name="ClrName">Suggested CLR type name.</param>
/// <param name="Key">The ordered node key.</param>
/// <param name="Properties">Declared node properties.</param>
/// <param name="ProviderAnnotations">Optional non-semantic provider hints.</param>
public sealed record NodeTypeDescriptor(
    string Id,
    string Name,
    string ClrName,
    GraphKeyDescriptor Key,
    IReadOnlyList<GraphPropertyDescriptor> Properties,
    IReadOnlyDictionary<string, string>? ProviderAnnotations = null);

/// <summary>Describes one provider-neutral graph relation type.</summary>
/// <param name="Id">Stable relation identity in the descriptor.</param>
/// <param name="Name">Graph relation type.</param>
/// <param name="ClrName">Suggested CLR type name.</param>
/// <param name="SourceNodeId">Source node descriptor identity.</param>
/// <param name="TargetNodeId">Target node descriptor identity.</param>
/// <param name="Directed">Whether direction is semantically significant.</param>
/// <param name="Properties">Declared relation properties.</param>
/// <param name="ProviderAnnotations">Optional non-semantic provider hints.</param>
public sealed record RelationTypeDescriptor(
    string Id,
    string Name,
    string ClrName,
    string SourceNodeId,
    string TargetNodeId,
    bool Directed,
    IReadOnlyList<GraphPropertyDescriptor> Properties,
    IReadOnlyDictionary<string, string>? ProviderAnnotations = null);

/// <summary>Contains one canonical provider-neutral graph schema.</summary>
/// <param name="FormatVersion">Descriptor format version.</param>
/// <param name="Nodes">Declared node types.</param>
/// <param name="Relations">Declared relation types.</param>
/// <param name="SourceFingerprint">Optional fingerprint of source metadata.</param>
/// <param name="ProviderAnnotations">Optional non-semantic document hints.</param>
public sealed record GraphModelDescriptor(
    string FormatVersion,
    IReadOnlyList<NodeTypeDescriptor> Nodes,
    IReadOnlyList<RelationTypeDescriptor> Relations,
    string? SourceFingerprint = null,
    IReadOnlyDictionary<string, string>? ProviderAnnotations = null);
