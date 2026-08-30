namespace Nodal.Core.Modeling;

/// <summary>Validates structural invariants of canonical graph model descriptors.</summary>
public static class GraphModelDescriptorValidator
{
    /// <summary>Throws when a descriptor violates a canonical structural invariant.</summary>
    public static void ThrowIfInvalid(GraphModelDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (!string.Equals(descriptor.FormatVersion, GraphModelFormat.CurrentVersion, StringComparison.Ordinal))
        {
            throw new NotSupportedException($"Graph model format '{descriptor.FormatVersion}' is not supported.");
        }

        ArgumentNullException.ThrowIfNull(descriptor.Nodes);
        ArgumentNullException.ThrowIfNull(descriptor.Relations);
        RequireUnique(descriptor.Nodes, node => node.Id, "node");
        RequireUnique(descriptor.Relations, relation => relation.Id, "relation");
        var nodeIds = descriptor.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var node in descriptor.Nodes)
        {
            RequireName(node.Id, "Node ID");
            RequireName(node.Name, "Node name");
            RequireName(node.ClrName, "Node CLR name");
            ArgumentNullException.ThrowIfNull(node.Key);
            ArgumentNullException.ThrowIfNull(node.Properties);
            RequireUnique(node.Properties, property => property.Name, $"property on node '{node.Id}'");
            ValidateProperties(node.Properties);
            if (node.Key.Properties is null || node.Key.Properties.Count == 0)
            {
                throw new ArgumentException($"Node '{node.Id}' must declare at least one key property.");
            }

            var properties = node.Properties.Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
            if (node.Key.Properties.Any(property => !properties.Contains(property)))
            {
                throw new ArgumentException($"Node '{node.Id}' key references an undeclared property.");
            }
        }

        foreach (var relation in descriptor.Relations)
        {
            RequireName(relation.Id, "Relation ID");
            RequireName(relation.Name, "Relation name");
            RequireName(relation.ClrName, "Relation CLR name");
            if (!nodeIds.Contains(relation.SourceNodeId) || !nodeIds.Contains(relation.TargetNodeId))
            {
                throw new ArgumentException($"Relation '{relation.Id}' references an undeclared endpoint.");
            }

            ArgumentNullException.ThrowIfNull(relation.Properties);
            RequireUnique(relation.Properties, property => property.Name, $"property on relation '{relation.Id}'");
            ValidateProperties(relation.Properties);
        }
    }

    private static void ValidateProperties(IEnumerable<GraphPropertyDescriptor> properties)
    {
        foreach (var property in properties)
        {
            RequireName(property.Name, "Property name");
            RequireName(property.ClrName, "Property CLR name");
            if (property.IsCollection != property.ItemKind.HasValue)
            {
                throw new ArgumentException($"Collection property '{property.Name}' must declare exactly one item kind.");
            }

            if (property.IsCollection != (property.ValueKind == GraphValueKind.Collection))
            {
                throw new ArgumentException($"Collection property '{property.Name}' must use the Collection value kind.");
            }

            if (property.ItemKind is GraphValueKind.Collection or GraphValueKind.Null)
            {
                throw new ArgumentException($"Collection property '{property.Name}' has an unsupported item kind.");
            }
        }
    }

    private static void RequireUnique<T>(IEnumerable<T> values, Func<T, string> selector, string description)
    {
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (!identities.Add(selector(value)))
            {
                throw new ArgumentException($"Duplicate {description} identity '{selector(value)}'.");
            }
        }
    }

    private static void RequireName(string value, string description)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{description} cannot be empty.");
        }
    }
}
