namespace Nodal.Core.Modeling;

/// <summary>Classifies the compatibility effect of a graph model change.</summary>
public enum GraphModelChangeImpact
{
    /// <summary>The change can be consumed without recompiling existing model contracts.</summary>
    NonBreaking,

    /// <summary>The change can invalidate generated types, stored data, or consumers.</summary>
    Breaking,
}

/// <summary>Identifies graph model change kinds.</summary>
public enum GraphModelChangeKind
{
    /// <summary>A node type was added.</summary>
    NodeAdded,
    /// <summary>A node type was removed.</summary>
    NodeRemoved,
    /// <summary>A relation type was added.</summary>
    RelationAdded,
    /// <summary>A relation type was removed.</summary>
    RelationRemoved,
    /// <summary>A property was added.</summary>
    PropertyAdded,
    /// <summary>A property was removed.</summary>
    PropertyRemoved,
    /// <summary>A property contract changed.</summary>
    PropertyChanged,
    /// <summary>A node key changed.</summary>
    KeyChanged,
    /// <summary>A relation endpoint or direction changed.</summary>
    RelationShapeChanged,
    /// <summary>A suggested CLR name changed.</summary>
    ClrNameChanged,
}

/// <summary>Describes one deterministic canonical model change.</summary>
/// <param name="Kind">Change kind.</param>
/// <param name="Impact">Compatibility impact.</param>
/// <param name="Path">Logical descriptor path.</param>
/// <param name="Message">Human-readable explanation.</param>
public sealed record GraphModelChange(
    GraphModelChangeKind Kind,
    GraphModelChangeImpact Impact,
    string Path,
    string Message);

/// <summary>Contains ordered canonical graph model changes.</summary>
/// <param name="Changes">Changes ordered by path and kind.</param>
public sealed record GraphModelDiff(IReadOnlyList<GraphModelChange> Changes)
{
    /// <summary>Gets whether the model is semantically unchanged.</summary>
    public bool IsEmpty => Changes.Count == 0;

    /// <summary>Gets whether at least one breaking change exists.</summary>
    public bool HasBreakingChanges => Changes.Any(change => change.Impact == GraphModelChangeImpact.Breaking);
}

/// <summary>Compares canonical graph descriptors without provider-specific assumptions.</summary>
public static class GraphModelDiffer
{
    /// <summary>Returns deterministic compatibility changes between two descriptors.</summary>
    public static GraphModelDiff Compare(GraphModelDescriptor before, GraphModelDescriptor after)
    {
        var left = GraphModelDescriptorJson.Canonicalize(before);
        var right = GraphModelDescriptorJson.Canonicalize(after);
        var changes = new List<GraphModelChange>();
        CompareNodes(left.Nodes, right.Nodes, changes);
        CompareRelations(left.Relations, right.Relations, changes);
        return new GraphModelDiff(changes
            .OrderBy(change => change.Path, StringComparer.Ordinal)
            .ThenBy(change => change.Kind)
            .ToArray());
    }

    private static void CompareNodes(
        IReadOnlyList<NodeTypeDescriptor> before,
        IReadOnlyList<NodeTypeDescriptor> after,
        List<GraphModelChange> changes)
    {
        var left = before.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var right = after.ToDictionary(node => node.Id, StringComparer.Ordinal);
        AddTypePresence(left.Keys, right.Keys, "nodes", GraphModelChangeKind.NodeAdded, GraphModelChangeKind.NodeRemoved, changes);
        foreach (var id in left.Keys.Intersect(right.Keys, StringComparer.Ordinal))
        {
            var previous = left[id];
            var current = right[id];
            var path = $"$.nodes[{id}]";
            CompareClrName(previous.ClrName, current.ClrName, path, changes);
            if (!previous.Key.Properties.SequenceEqual(current.Key.Properties, StringComparer.Ordinal))
            {
                changes.Add(new GraphModelChange(
                    GraphModelChangeKind.KeyChanged,
                    GraphModelChangeImpact.Breaking,
                    $"{path}.key",
                    "The ordered node key changed."));
            }

            CompareProperties(previous.Properties, current.Properties, path, changes);
        }
    }

    private static void CompareRelations(
        IReadOnlyList<RelationTypeDescriptor> before,
        IReadOnlyList<RelationTypeDescriptor> after,
        List<GraphModelChange> changes)
    {
        var left = before.ToDictionary(relation => relation.Id, StringComparer.Ordinal);
        var right = after.ToDictionary(relation => relation.Id, StringComparer.Ordinal);
        AddTypePresence(left.Keys, right.Keys, "relations", GraphModelChangeKind.RelationAdded, GraphModelChangeKind.RelationRemoved, changes);
        foreach (var id in left.Keys.Intersect(right.Keys, StringComparer.Ordinal))
        {
            var previous = left[id];
            var current = right[id];
            var path = $"$.relations[{id}]";
            CompareClrName(previous.ClrName, current.ClrName, path, changes);
            if (previous.SourceNodeId != current.SourceNodeId || previous.TargetNodeId != current.TargetNodeId ||
                previous.Directed != current.Directed)
            {
                changes.Add(new GraphModelChange(
                    GraphModelChangeKind.RelationShapeChanged,
                    GraphModelChangeImpact.Breaking,
                    path,
                    "The relation endpoints or direction changed."));
            }

            CompareProperties(previous.Properties, current.Properties, path, changes);
        }
    }

    private static void CompareProperties(
        IReadOnlyList<GraphPropertyDescriptor> before,
        IReadOnlyList<GraphPropertyDescriptor> after,
        string ownerPath,
        List<GraphModelChange> changes)
    {
        var left = before.ToDictionary(property => property.Name, StringComparer.Ordinal);
        var right = after.ToDictionary(property => property.Name, StringComparer.Ordinal);
        foreach (var name in right.Keys.Except(left.Keys, StringComparer.Ordinal))
        {
            var property = right[name];
            changes.Add(new GraphModelChange(
                GraphModelChangeKind.PropertyAdded,
                property.IsNullable ? GraphModelChangeImpact.NonBreaking : GraphModelChangeImpact.Breaking,
                $"{ownerPath}.properties[{name}]",
                property.IsNullable ? "An optional property was added." : "A required property was added."));
        }

        foreach (var name in left.Keys.Except(right.Keys, StringComparer.Ordinal))
        {
            changes.Add(new GraphModelChange(
                GraphModelChangeKind.PropertyRemoved,
                GraphModelChangeImpact.Breaking,
                $"{ownerPath}.properties[{name}]",
                "A property was removed."));
        }

        foreach (var name in left.Keys.Intersect(right.Keys, StringComparer.Ordinal))
        {
            var previous = left[name];
            var current = right[name];
            var path = $"{ownerPath}.properties[{name}]";
            if (previous.ClrName != current.ClrName)
            {
                CompareClrName(previous.ClrName, current.ClrName, path, changes);
            }

            if (previous.ValueKind != current.ValueKind || previous.IsCollection != current.IsCollection ||
                previous.ItemKind != current.ItemKind || previous.IsNullable != current.IsNullable)
            {
                var impact = previous.ValueKind == current.ValueKind &&
                    previous.IsCollection == current.IsCollection &&
                    previous.ItemKind == current.ItemKind && !previous.IsNullable && current.IsNullable
                        ? GraphModelChangeImpact.NonBreaking
                        : GraphModelChangeImpact.Breaking;
                changes.Add(new GraphModelChange(
                    GraphModelChangeKind.PropertyChanged,
                    impact,
                    path,
                    "The property type, collection shape, or nullability changed."));
            }
        }
    }

    private static void CompareClrName(string before, string after, string path, List<GraphModelChange> changes)
    {
        if (before != after)
        {
            changes.Add(new GraphModelChange(
                GraphModelChangeKind.ClrNameChanged,
                GraphModelChangeImpact.Breaking,
                $"{path}.clrName",
                $"The generated CLR name changed from '{before}' to '{after}'."));
        }
    }

    private static void AddTypePresence(
        IEnumerable<string> before,
        IEnumerable<string> after,
        string segment,
        GraphModelChangeKind addedKind,
        GraphModelChangeKind removedKind,
        List<GraphModelChange> changes)
    {
        foreach (var id in after.Except(before, StringComparer.Ordinal))
        {
            changes.Add(new GraphModelChange(
                addedKind,
                GraphModelChangeImpact.NonBreaking,
                $"$.{segment}[{id}]",
                "A graph type was added."));
        }

        foreach (var id in before.Except(after, StringComparer.Ordinal))
        {
            changes.Add(new GraphModelChange(
                removedKind,
                GraphModelChangeImpact.Breaking,
                $"$.{segment}[{id}]",
                "A graph type was removed."));
        }
    }
}
