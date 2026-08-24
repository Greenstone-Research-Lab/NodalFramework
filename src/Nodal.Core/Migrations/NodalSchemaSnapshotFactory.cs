using System.Reflection;
using Nodal.Core.Metadata;

namespace Nodal.Core.Migrations;

/// <summary>Builds deterministic schema snapshots from a registered Nodal model.</summary>
public static class NodalSchemaSnapshotFactory
{
    /// <summary>Creates a provider-neutral snapshot without connecting to a database.</summary>
    public static NodalSchemaSnapshot FromModel(
        NodalModel model,
        string? providerName = null,
        string? providerVersion = null)
    {
        ArgumentNullException.ThrowIfNull(model);

        var nodes = model.Nodes.Select(node => new NodalNodeSnapshot(
            node.Name,
            TypeName(node.ClrType),
            node.KeyProperty,
            node.Properties.Values.Select(ToProperty).ToArray())).ToArray();

        var relations = model.Relations.Select(relation => new NodalRelationSnapshot(
            relation.Name,
            TypeName(relation.ClrType),
            TypeName(relation.SourceType),
            TypeName(relation.TargetType),
            relation.Directed,
            relation.Properties.Values.Select(ToProperty).ToArray())).ToArray();

        return new NodalSchemaSnapshot(
            NodalSchemaSnapshot.CurrentFormatVersion,
            nodes,
            relations,
            providerName,
            providerVersion).Normalize();
    }

    private static NodalPropertySnapshot ToProperty(GraphPropertyMetadata property)
    {
        var underlying = Nullable.GetUnderlyingType(property.ClrType);
        var effectiveType = underlying ?? property.ClrType;
        return new NodalPropertySnapshot(
            property.Name,
            property.ClrName,
            TypeName(effectiveType),
            underlying is not null || !effectiveType.IsValueType,
            effectiveType.IsEnum,
            effectiveType.IsEnum
                ? Enum.GetNames(effectiveType).Order(StringComparer.Ordinal).ToArray()
                : [],
            null);
    }

    private static string TypeName(Type type) =>
        type.AssemblyQualifiedName ?? type.FullName ?? type.Name;
}
