namespace Nodal.Core.ChangeTracking;

/// <summary>
/// Identifies a graph node using its portable model name and stable domain key.
/// </summary>
/// <param name="ClrType">The mapped CLR node type.</param>
/// <param name="NodeType">The provider-neutral node name.</param>
/// <param name="KeyProperty">The mapped graph key property name.</param>
/// <param name="Value">The non-null domain key value.</param>
public sealed record GraphIdentity(
    Type ClrType,
    string NodeType,
    string KeyProperty,
    object Value);
