namespace Nodal.Core.Modeling;

/// <summary>Summarizes a canonical descriptor for CLI, CI, and audit evidence.</summary>
/// <param name="FormatVersion">Descriptor format version.</param>
/// <param name="Fingerprint">Canonical SHA-256 fingerprint.</param>
/// <param name="NodeCount">Node type count.</param>
/// <param name="RelationCount">Relation type count.</param>
/// <param name="PropertyCount">Total node and relation property count.</param>
/// <param name="CompositeKeyCount">Node types with composite keys.</param>
/// <param name="ReviewCount">Elements explicitly marked for review.</param>
public sealed record GraphModelInspection(
    string FormatVersion,
    string Fingerprint,
    int NodeCount,
    int RelationCount,
    int PropertyCount,
    int CompositeKeyCount,
    int ReviewCount);

/// <summary>Creates deterministic, provider-neutral descriptor summaries.</summary>
public static class GraphModelInspector
{
    /// <summary>Validates and summarizes one descriptor.</summary>
    public static GraphModelInspection Inspect(GraphModelDescriptor descriptor)
    {
        var canonical = GraphModelDescriptorJson.Canonicalize(descriptor);
        return new GraphModelInspection(
            canonical.FormatVersion,
            GraphModelDescriptorJson.ComputeFingerprint(canonical),
            canonical.Nodes.Count,
            canonical.Relations.Count,
            canonical.Nodes.Sum(node => node.Properties.Count) +
            canonical.Relations.Sum(relation => relation.Properties.Count),
            canonical.Nodes.Count(node => node.Key.Properties.Count > 1),
            canonical.Nodes.Count(node => GraphModelValidation.HasReview(node.ProviderAnnotations)) +
            canonical.Relations.Count(relation => GraphModelValidation.HasReview(relation.ProviderAnnotations)));
    }
}
