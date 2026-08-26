using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Nodal.Core.Migrations;

/// <summary>Represents a versioned, provider-neutral persisted graph schema.</summary>
[ExcludeFromCodeCoverage]
public sealed record NodalSchemaSnapshot(
    int FormatVersion,
    IReadOnlyList<NodalNodeSnapshot> Nodes,
    IReadOnlyList<NodalRelationSnapshot> Relations,
    string? ProviderName = null,
    string? ProviderVersion = null,
    IReadOnlyList<NodalSchemaObjectSnapshot>? Indexes = null,
    IReadOnlyList<NodalSchemaObjectSnapshot>? Constraints = null)
{
    /// <summary>Current snapshot format supported by this package.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>Creates a normalized snapshot with stable ordering and immutable collections.</summary>
    public NodalSchemaSnapshot Normalize()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(FormatVersion);
        ArgumentNullException.ThrowIfNull(Nodes);
        ArgumentNullException.ThrowIfNull(Relations);

        return this with
        {
            Nodes = Nodes.OrderBy(node => node.Name, StringComparer.Ordinal)
                .ThenBy(node => node.ClrTypeName, StringComparer.Ordinal)
                .Select(node => node.Normalize())
                .ToImmutableArray(),
            Relations = Relations.OrderBy(relation => relation.Name, StringComparer.Ordinal)
                .ThenBy(relation => relation.SourceNode, StringComparer.Ordinal)
                .ThenBy(relation => relation.TargetNode, StringComparer.Ordinal)
                .Select(relation => relation.Normalize())
                .ToImmutableArray(),
            Indexes = NormalizeObjects(Indexes),
            Constraints = NormalizeObjects(Constraints),
        };
    }

    private static ImmutableArray<NodalSchemaObjectSnapshot> NormalizeObjects(
        IReadOnlyList<NodalSchemaObjectSnapshot>? objects) =>
        (objects ?? [])
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .ThenBy(item => item.ObjectType, StringComparer.Ordinal)
            .Select(item => item.Normalize())
            .ToImmutableArray();
}

/// <summary>Describes one graph node in a schema snapshot.</summary>
[ExcludeFromCodeCoverage]
public sealed record NodalNodeSnapshot(
    string Name,
    string ClrTypeName,
    string KeyProperty,
    IReadOnlyList<NodalPropertySnapshot> Properties)
{
    /// <summary>Returns this node with deterministic property ordering.</summary>
    public NodalNodeSnapshot Normalize() => this with
    {
        Properties = Properties.OrderBy(property => property.Name, StringComparer.Ordinal)
            .ThenBy(property => property.ClrTypeName, StringComparer.Ordinal)
            .ToImmutableArray(),
    };
}

/// <summary>Describes one graph relationship in a schema snapshot.</summary>
[ExcludeFromCodeCoverage]
public sealed record NodalRelationSnapshot(
    string Name,
    string ClrTypeName,
    string SourceNode,
    string TargetNode,
    bool Directed,
    IReadOnlyList<NodalPropertySnapshot> Properties)
{
    /// <summary>Returns this relationship with deterministic property ordering.</summary>
    public NodalRelationSnapshot Normalize() => this with
    {
        Properties = Properties.OrderBy(property => property.Name, StringComparer.Ordinal)
            .ThenBy(property => property.ClrTypeName, StringComparer.Ordinal)
            .ToImmutableArray(),
    };
}

/// <summary>Describes a persisted graph property and its CLR semantics.</summary>
[ExcludeFromCodeCoverage]
public sealed record NodalPropertySnapshot(
    string Name,
    string ClrName,
    string ClrTypeName,
    bool IsNullable,
    bool IsEnum,
    IReadOnlyList<string> EnumValues,
    string? ProviderStorageType = null);

/// <summary>Describes a provider schema index or constraint.</summary>
[ExcludeFromCodeCoverage]
public sealed record NodalSchemaObjectSnapshot(
    string Name,
    string ObjectType,
    string EntityName,
    IReadOnlyList<string> Properties,
    bool IsUnique = false)
{
    /// <summary>Returns this schema object with deterministic property ordering.</summary>
    public NodalSchemaObjectSnapshot Normalize()
    {
        ArgumentNullException.ThrowIfNull(Properties);
        return this with
        {
            Properties = Properties.Order(StringComparer.Ordinal).ToImmutableArray(),
        };
    }
}
