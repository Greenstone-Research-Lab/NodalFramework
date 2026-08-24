namespace Nodal.Core.Migrations;

/// <summary>Classifies one provider-neutral schema difference.</summary>
public enum NodalSchemaChangeKind
{
    /// <summary>A node type was added.</summary>
    NodeAdded,
    /// <summary>A node type was removed.</summary>
    NodeRemoved,
    /// <summary>A node property was added.</summary>
    NodePropertyAdded,
    /// <summary>A node property was removed.</summary>
    NodePropertyRemoved,
    /// <summary>A node property was explicitly renamed.</summary>
    NodePropertyRenamed,
    /// <summary>A node property persisted type changed.</summary>
    NodePropertyTypeChanged,
    /// <summary>A relation type was added.</summary>
    RelationAdded,
    /// <summary>A relation type was removed.</summary>
    RelationRemoved,
    /// <summary>A relation direction or endpoint changed.</summary>
    RelationShapeChanged,
    /// <summary>A relation property was added.</summary>
    RelationPropertyAdded,
    /// <summary>A relation property was removed.</summary>
    RelationPropertyRemoved,
    /// <summary>A relation property was explicitly renamed.</summary>
    RelationPropertyRenamed,
    /// <summary>A relation property persisted type changed.</summary>
    RelationPropertyTypeChanged,
}

/// <summary>Describes one deterministic schema change.</summary>
public sealed record NodalSchemaChange(
    NodalSchemaChangeKind Kind,
    string ObjectName,
    string? PropertyName = null,
    string? NewPropertyName = null,
    string? Detail = null);

/// <summary>Controls explicit, safe rename interpretation during schema diffing.</summary>
public sealed record NodalSchemaDiffOptions(
    IReadOnlyDictionary<string, string>? RenameHints = null)
{
    /// <summary>Gets an empty options instance.</summary>
    public static NodalSchemaDiffOptions Default { get; } = new();

    internal string? ResolveRename(string scope, string oldName) =>
        RenameHints is not null && RenameHints.TryGetValue(
            string.Concat(scope, ":", oldName), out var newName)
            ? newName
            : null;
}

/// <summary>Contains deterministic changes between two normalized snapshots.</summary>
public sealed record NodalSchemaDiffResult(
    IReadOnlyList<NodalSchemaChange> Changes)
{
    /// <summary>Gets whether no schema changes were found.</summary>
    public bool IsEmpty => Changes.Count == 0;
}

/// <summary>Compares provider-neutral schema snapshots without guessing renames.</summary>
public static class NodalSchemaDiffer
{
    /// <summary>Computes a stable, reviewable diff between two schema snapshots.</summary>
    public static NodalSchemaDiffResult Compare(
        NodalSchemaSnapshot before,
        NodalSchemaSnapshot after,
        NodalSchemaDiffOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var settings = options ?? NodalSchemaDiffOptions.Default;
        var changes = new List<NodalSchemaChange>();
        var previous = before.Normalize();
        var current = after.Normalize();

        CompareNodes(previous, current, settings, changes);
        CompareRelations(previous, current, settings, changes);

        return new NodalSchemaDiffResult(changes
            .OrderBy(change => change.ObjectName, StringComparer.Ordinal)
            .ThenBy(change => change.PropertyName, StringComparer.Ordinal)
            .ThenBy(change => change.Kind)
            .ToArray());
    }

    private static void CompareNodes(
        NodalSchemaSnapshot before,
        NodalSchemaSnapshot after,
        NodalSchemaDiffOptions options,
        List<NodalSchemaChange> changes)
    {
        var oldNodes = before.Nodes.ToDictionary(node => node.Name, StringComparer.Ordinal);
        var newNodes = after.Nodes.ToDictionary(node => node.Name, StringComparer.Ordinal);

        foreach (var node in oldNodes.Values)
        {
            if (!newNodes.ContainsKey(node.Name))
            {
                changes.Add(new(NodalSchemaChangeKind.NodeRemoved, node.Name));
            }
        }

        foreach (var node in newNodes.Values)
        {
            if (!oldNodes.TryGetValue(node.Name, out var previous))
            {
                changes.Add(new(NodalSchemaChangeKind.NodeAdded, node.Name));
                continue;
            }

            CompareProperties(
                node.Name,
                previous.Properties,
                node.Properties,
                options,
                relation: false,
                changes);
        }
    }

    private static void CompareRelations(
        NodalSchemaSnapshot before,
        NodalSchemaSnapshot after,
        NodalSchemaDiffOptions options,
        List<NodalSchemaChange> changes)
    {
        var oldRelations = before.Relations.ToDictionary(relation => relation.Name, StringComparer.Ordinal);
        var newRelations = after.Relations.ToDictionary(relation => relation.Name, StringComparer.Ordinal);

        foreach (var relation in oldRelations.Values)
        {
            if (!newRelations.ContainsKey(relation.Name))
            {
                changes.Add(new(NodalSchemaChangeKind.RelationRemoved, relation.Name));
            }
        }

        foreach (var relation in newRelations.Values)
        {
            if (!oldRelations.TryGetValue(relation.Name, out var previous))
            {
                changes.Add(new(NodalSchemaChangeKind.RelationAdded, relation.Name));
                continue;
            }

            if (previous.Directed != relation.Directed ||
                !string.Equals(previous.SourceNode, relation.SourceNode, StringComparison.Ordinal) ||
                !string.Equals(previous.TargetNode, relation.TargetNode, StringComparison.Ordinal))
            {
                changes.Add(new(
                    NodalSchemaChangeKind.RelationShapeChanged,
                    relation.Name,
                    Detail: "Relation endpoints or direction changed."));
            }

            CompareProperties(
                relation.Name,
                previous.Properties,
                relation.Properties,
                options,
                relation: true,
                changes);
        }
    }

    private static void CompareProperties(
        string objectName,
        IReadOnlyList<NodalPropertySnapshot> before,
        IReadOnlyList<NodalPropertySnapshot> after,
        NodalSchemaDiffOptions options,
        bool relation,
        List<NodalSchemaChange> changes)
    {
        var oldProperties = before.ToDictionary(property => property.Name, StringComparer.Ordinal);
        var newProperties = after.ToDictionary(property => property.Name, StringComparer.Ordinal);
        var scope = relation ? string.Concat("relation:", objectName) : string.Concat("node:", objectName);

        foreach (var property in oldProperties.Values)
        {
            if (newProperties.ContainsKey(property.Name))
            {
                continue;
            }

            var rename = options.ResolveRename(scope, property.Name);
            if (rename is not null && newProperties.ContainsKey(rename))
            {
                changes.Add(new(
                    relation ? NodalSchemaChangeKind.RelationPropertyRenamed : NodalSchemaChangeKind.NodePropertyRenamed,
                    objectName,
                    property.Name,
                    rename));
            }
            else
            {
                changes.Add(new(
                    relation ? NodalSchemaChangeKind.RelationPropertyRemoved : NodalSchemaChangeKind.NodePropertyRemoved,
                    objectName,
                    property.Name));
            }
        }

        foreach (var property in newProperties.Values)
        {
            if (!oldProperties.TryGetValue(property.Name, out var previous))
            {
                if (!oldProperties.Values.Any(old =>
                    options.ResolveRename(scope, old.Name) == property.Name))
                {
                    changes.Add(new(
                        relation ? NodalSchemaChangeKind.RelationPropertyAdded : NodalSchemaChangeKind.NodePropertyAdded,
                        objectName,
                        property.Name));
                }

                continue;
            }

            if (!string.Equals(previous.ClrTypeName, property.ClrTypeName, StringComparison.Ordinal) ||
                previous.IsNullable != property.IsNullable ||
                previous.IsEnum != property.IsEnum ||
                !previous.EnumValues.SequenceEqual(property.EnumValues, StringComparer.Ordinal))
            {
                changes.Add(new(
                    relation ? NodalSchemaChangeKind.RelationPropertyTypeChanged : NodalSchemaChangeKind.NodePropertyTypeChanged,
                    objectName,
                    property.Name,
                    Detail: string.Concat(previous.ClrTypeName, " -> ", property.ClrTypeName)));
            }
        }
    }
}
