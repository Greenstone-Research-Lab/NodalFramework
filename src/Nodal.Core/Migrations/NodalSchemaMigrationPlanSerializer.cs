using System.Text;
using System.Text.Json;

namespace Nodal.Core.Migrations;

/// <summary>Renders migration plans for code review and machine processing.</summary>
public static class NodalSchemaMigrationPlanSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>Creates deterministic JSON containing operation and review descriptors.</summary>
    public static string Serialize(NodalSchemaMigrationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return JsonSerializer.Serialize(ToDocument(plan), Options);
    }

    /// <summary>Creates a compact Markdown summary suitable for pull-request review.</summary>
    public static string ToMarkdown(NodalSchemaMigrationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var document = ToDocument(plan);
        var output = new StringBuilder()
            .AppendLine("# Nodal schema migration plan")
            .AppendLine()
            .AppendLine("## Operations");

        AppendItems(output, document.Operations);
        output.AppendLine().AppendLine("## Manual review");
        AppendItems(output, document.ManualReview);
        return output.ToString();
    }

    private static NodalSchemaMigrationPlanDocument ToDocument(NodalSchemaMigrationPlan plan) =>
        new(
            plan.Operations.Select(DescribeOperation).ToArray(),
            plan.ManualReview.Select(change =>
                $"{change.Kind}: {change.ObjectName}" +
                (change.PropertyName is null ? string.Empty : $".{change.PropertyName}")).ToArray());

    private static string DescribeOperation(MigrationOperation operation) => operation switch
    {
        CreateNodeTypeOperation value => $"Create node {value.NodeType}",
        CreateRelationTypeOperation value => $"Create relation {value.RelationType}",
        CreateUniqueConstraintOperation value => $"Create unique constraint {value.NodeType}.{value.PropertyName}",
        CreateIndexOperation value => $"Create index {value.NodeType}.{value.PropertyName}",
        DropIndexOperation value => $"Drop index {value.NodeType}.{value.PropertyName}",
        DropUniqueConstraintOperation value => $"Drop unique constraint {value.NodeType}.{value.PropertyName}",
        DropNodeTypeOperation value => $"Drop node {value.NodeType}",
        DropRelationTypeOperation value => $"Drop relation {value.RelationType}",
        DropSchemaObjectOperation value => $"Drop {value.Kind} {value.Name}",
        AddNodePropertyOperation value => $"Add node property {value.NodeType}.{value.Property.Name}",
        AddRelationPropertyOperation value => $"Add relation property {value.RelationType}.{value.Property.Name}",
        DropNodePropertyOperation value => $"Drop node property {value.NodeType}.{value.PropertyName}",
        DropRelationPropertyOperation value => $"Drop relation property {value.RelationType}.{value.PropertyName}",
        RenameNodePropertyOperation value => $"Rename node property {value.NodeType}.{value.OldPropertyName} to {value.NewPropertyName}",
        RenameRelationPropertyOperation value => $"Rename relation property {value.RelationType}.{value.OldPropertyName} to {value.NewPropertyName}",
        AlterNodePropertyTypeOperation value => $"Alter node property {value.NodeType}.{value.PropertyName}",
        AlterRelationPropertyTypeOperation value => $"Alter relation property {value.RelationType}.{value.PropertyName}",
        _ => operation.GetType().Name,
    };

    private static void AppendItems(StringBuilder output, IReadOnlyList<string> items)
    {
        if (items.Count == 0)
        {
            output.AppendLine("- None");
            return;
        }

        foreach (var item in items)
        {
            output.Append("- ").AppendLine(item);
        }
    }

    private sealed record NodalSchemaMigrationPlanDocument(
        IReadOnlyList<string> Operations,
        IReadOnlyList<string> ManualReview);
}
