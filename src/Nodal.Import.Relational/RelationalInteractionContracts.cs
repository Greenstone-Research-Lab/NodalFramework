using System.Text.Json.Serialization;

namespace Nodal.Import.Relational;

/// <summary>Defines the current open relational interaction model format.</summary>
public static class RelationalInteractionFormat
{
    /// <summary>Gets the current model format version.</summary>
    public const string CurrentVersion = "1.0";
}

/// <summary>Classifies a relational object using structural evidence only.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RelationalInteractionObjectRole>))]
public enum RelationalInteractionObjectRole
{
    /// <summary>A table with a stable primary key.</summary>
    Entity,

    /// <summary>A table whose primary key is composed from two or more outgoing foreign keys.</summary>
    Association,

    /// <summary>A database view or materialized view.</summary>
    View,

    /// <summary>An object referenced by a foreign key but absent from the discovered table collection.</summary>
    External,

    /// <summary>A table without enough structural evidence for a stronger classification.</summary>
    Unknown,
}

/// <summary>Identifies the relational source represented by an interaction model.</summary>
/// <param name="Provider">Provider family that produced the snapshot, when known.</param>
/// <param name="Database">Database name reported by the connection, when available.</param>
/// <param name="SchemaFingerprint">Deterministic SHA-256 fingerprint of structural metadata.</param>
public sealed record RelationalInteractionSource(string? Provider, string? Database, string SchemaFingerprint);

/// <summary>Describes one column retained as structural evidence in an interaction model.</summary>
public sealed record RelationalInteractionColumn(
    string Name,
    string DataType,
    bool IsNullable,
    int Ordinal,
    bool IsPrimaryKey);

/// <summary>Describes a table or view represented as a node in a relational interaction network.</summary>
public sealed record RelationalInteractionObject(
    string Id,
    string Schema,
    string Name,
    string Kind,
    RelationalInteractionObjectRole Role,
    IReadOnlyList<RelationalInteractionColumn> Columns);

/// <summary>Identifies one endpoint and its ordered columns in a physical foreign-key relation.</summary>
public sealed record RelationalInteractionEndpoint(string ObjectId, IReadOnlyList<string> Columns);

/// <summary>
/// Provides a deterministic display suggestion without changing or claiming semantic meaning for the physical relation.
/// </summary>
public sealed record RelationalInteractionDisplay(
    string SourceObjectId,
    string TargetObjectId,
    string SuggestedLabel,
    bool Reversed,
    bool RequiresReview);

/// <summary>Describes one lossless foreign-key interaction and its optional structural display suggestion.</summary>
public sealed record RelationalInteractionRelation(
    string Id,
    string ConstraintName,
    RelationalInteractionEndpoint Source,
    RelationalInteractionEndpoint Target,
    RelationalReferentialAction OnDelete,
    RelationalReferentialAction OnUpdate,
    RelationalInteractionDisplay Display);

/// <summary>
/// Contains a deterministic, provider-neutral interaction network derived exclusively from relational metadata.
/// </summary>
public sealed record RelationalInteractionModel(
    string FormatVersion,
    RelationalInteractionSource Source,
    IReadOnlyList<RelationalInteractionObject> Objects,
    IReadOnlyList<RelationalInteractionRelation> Relations,
    IReadOnlyList<string> Diagnostics);
