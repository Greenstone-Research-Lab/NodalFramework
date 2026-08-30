using System.Security.Cryptography;
using System.Text;
using Nodal.Core.Modeling;

namespace Nodal.Import.Relational;

/// <summary>Converts relational interaction evidence into a canonical graph model descriptor.</summary>
public static class RelationalGraphModelDescriptorBuilder
{
    /// <summary>Builds a deterministic descriptor while retaining physical evidence as annotations.</summary>
    /// <param name="interaction">The reviewed or convention-generated relational interaction model.</param>
    /// <returns>A validated canonical graph model descriptor.</returns>
    public static GraphModelDescriptor Build(RelationalInteractionModel interaction)
    {
        ArgumentNullException.ThrowIfNull(interaction);
        var nodes = interaction.Objects.Select(BuildNode).ToArray();
        var relations = interaction.Relations.Select(BuildRelation).ToArray();
        (nodes, relations) = EnsureUniqueClrTypeNames(nodes, relations);
        var annotations = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["source.kind"] = "relational",
            ["source.provider"] = interaction.Source.Provider ?? string.Empty,
            ["source.database"] = interaction.Source.Database ?? string.Empty,
        };
        var descriptor = GraphModelDescriptorJson.Canonicalize(new GraphModelDescriptor(
            GraphModelFormat.CurrentVersion,
            nodes,
            relations,
            interaction.Source.SchemaFingerprint,
            annotations));
        GraphModelValidation.Validate(descriptor).ThrowIfInvalid();
        return descriptor;
    }

    private static NodeTypeDescriptor BuildNode(RelationalInteractionObject item)
    {
        var properties = EnsureUniquePropertyClrNames(item.Columns.Select(column => new GraphPropertyDescriptor(
            column.Name,
            Identifier(column.Name, "Property"),
            MapType(column.DataType),
            column.IsNullable,
            ProviderAnnotations: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["source.dataType"] = column.DataType,
                ["source.ordinal"] = column.Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["source.primaryKey"] = column.IsPrimaryKey ? "true" : "false",
            })).ToArray()).ToList();
        var keys = item.Columns.Where(column => column.IsPrimaryKey).OrderBy(column => column.Ordinal)
            .Select(column => column.Name).ToArray();
        var synthetic = keys.Length == 0;
        if (synthetic)
        {
            const string syntheticName = "__nodal_source_identity";
            properties.Add(new GraphPropertyDescriptor(
                syntheticName,
                "NodalSourceIdentity",
                GraphValueKind.Text,
                false,
                ProviderAnnotations: new Dictionary<string, string> { ["nodal.synthetic"] = "true" }));
            keys = [syntheticName];
        }

        return new NodeTypeDescriptor(
            item.Id,
            Identifier(item.Name, "Node"),
            Identifier(item.Name, "Node"),
            new GraphKeyDescriptor(keys),
            properties,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["source.schema"] = item.Schema,
                ["source.name"] = item.Name,
                ["source.kind"] = item.Kind,
                ["source.role"] = item.Role.ToString(),
                ["review.syntheticKey"] = synthetic ? "true" : "false",
            });
    }

    private static RelationTypeDescriptor BuildRelation(RelationalInteractionRelation relation)
    {
        var display = relation.Display;
        return new RelationTypeDescriptor(
            relation.Id,
            display.SuggestedLabel,
            Identifier(display.SuggestedLabel, "Relation"),
            display.SourceObjectId,
            display.TargetObjectId,
            true,
            [],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["source.constraint"] = relation.ConstraintName,
                ["source.sourceObject"] = relation.Source.ObjectId,
                ["source.sourceColumns"] = string.Join(",", relation.Source.Columns),
                ["source.targetObject"] = relation.Target.ObjectId,
                ["source.targetColumns"] = string.Join(",", relation.Target.Columns),
                ["source.onDelete"] = relation.OnDelete.ToString(),
                ["source.onUpdate"] = relation.OnUpdate.ToString(),
                ["display.reversed"] = display.Reversed ? "true" : "false",
                ["review.required"] = display.RequiresReview ? "true" : "false",
            });
    }

    private static (NodeTypeDescriptor[] Nodes, RelationTypeDescriptor[] Relations) EnsureUniqueClrTypeNames(
        NodeTypeDescriptor[] nodes,
        RelationTypeDescriptor[] relations)
    {
        var candidates = nodes.Select(node => new ClrTypeCandidate($"node:{node.Id}", node.Id, node.ClrName))
            .Concat(relations.Select(relation => new ClrTypeCandidate($"relation:{relation.Id}", relation.Id, relation.ClrName)))
            .ToArray();
        var resolved = candidates
            .GroupBy(candidate => candidate.ClrName, StringComparer.Ordinal)
            .SelectMany(group => group.Count() == 1
                ? group.Select(candidate => (candidate.Key, candidate.ClrName))
                : group.Select(candidate => (candidate.Key, ClrName: $"{candidate.ClrName}_{Fingerprint(candidate.Identity)}")))
            .ToDictionary(item => item.Key, item => item.ClrName, StringComparer.Ordinal);
        return (
            nodes.Select(node => node with { ClrName = resolved[$"node:{node.Id}"] }).ToArray(),
            relations.Select(relation => relation with { ClrName = resolved[$"relation:{relation.Id}"] }).ToArray());
    }

    private static GraphPropertyDescriptor[] EnsureUniquePropertyClrNames(
        IReadOnlyList<GraphPropertyDescriptor> properties)
    {
        var duplicateNames = properties.GroupBy(property => property.ClrName, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        return properties.Select(property => duplicateNames.Contains(property.ClrName)
            ? property with { ClrName = $"{property.ClrName}_{Fingerprint(property.Name)}" }
            : property).ToArray();
    }

    private static string Fingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..8];

    private static GraphValueKind MapType(string nativeType)
    {
        var normalized = nativeType.Trim().ToLowerInvariant();
        if (normalized.Contains("uuid", StringComparison.Ordinal) || normalized.Contains("uniqueidentifier", StringComparison.Ordinal)) return GraphValueKind.Identifier;
        if (normalized.Contains("bigint", StringComparison.Ordinal) || normalized is "int" or "integer" or "smallint" or "tinyint" or "serial" or "bigserial") return GraphValueKind.SignedInteger;
        if (normalized.Contains("decimal", StringComparison.Ordinal) || normalized.Contains("numeric", StringComparison.Ordinal) || normalized.Contains("money", StringComparison.Ordinal)) return GraphValueKind.DecimalNumber;
        if (normalized.Contains("float", StringComparison.Ordinal) || normalized.Contains("double", StringComparison.Ordinal) || normalized is "real") return GraphValueKind.FloatingPoint;
        if (normalized is "bit" or "bool" or "boolean") return GraphValueKind.Boolean;
        if (normalized.Contains("timestamp", StringComparison.Ordinal) || normalized.Contains("datetimeoffset", StringComparison.Ordinal)) return GraphValueKind.DateTimeOffset;
        if (normalized.Contains("datetime", StringComparison.Ordinal)) return GraphValueKind.DateTime;
        if (normalized == "date") return GraphValueKind.Date;
        if (normalized.StartsWith("time", StringComparison.Ordinal)) return GraphValueKind.Time;
        if (normalized.Contains("geography", StringComparison.Ordinal) || normalized.Contains("point", StringComparison.Ordinal)) return GraphValueKind.GeoPoint;
        if (normalized.Contains("vector", StringComparison.Ordinal)) return GraphValueKind.Vector;
        return GraphValueKind.Text;
    }

    private static string Identifier(string value, string fallback)
    {
        var result = new StringBuilder();
        var capitalize = true;
        foreach (var character in value)
        {
            if (!char.IsLetterOrDigit(character))
            {
                capitalize = true;
                continue;
            }

            result.Append(capitalize ? char.ToUpperInvariant(character) : character);
            capitalize = false;
        }

        if (result.Length == 0)
        {
            return fallback;
        }

        if (char.IsDigit(result[0]))
        {
            result.Insert(0, fallback);
        }

        return result.ToString();
    }

    private sealed record ClrTypeCandidate(string Key, string Identity, string ClrName);
}
