namespace Nodal.Core.Migrations;

/// <summary>
/// Represents a provider-neutral graph schema change.
/// </summary>
public abstract record MigrationOperation;

/// <summary>
/// Requests creation of a graph node type.
/// </summary>
/// <param name="NodeType">The provider-neutral node type.</param>
public sealed record CreateNodeTypeOperation(
    string NodeType,
    string KeyProperty = "Id",
    Type? KeyClrType = null,
    IReadOnlyList<GraphSchemaProperty>? Properties = null) : MigrationOperation;

/// <summary>
/// Requests creation of a graph relationship type.
/// </summary>
/// <param name="RelationType">The relationship type.</param>
/// <param name="SourceType">The source node type.</param>
/// <param name="TargetType">The target node type.</param>
/// <param name="Directed">Whether the relationship is directed.</param>
public sealed record CreateRelationTypeOperation(
    string RelationType,
    string SourceType,
    string TargetType,
    bool Directed,
    IReadOnlyList<GraphSchemaProperty>? Properties = null) : MigrationOperation;

/// <summary>
/// Requests a unique constraint for a node property.
/// </summary>
/// <param name="NodeType">The constrained node type.</param>
/// <param name="PropertyName">The constrained property.</param>
public sealed record CreateUniqueConstraintOperation(
    string NodeType,
    string PropertyName) : MigrationOperation;

/// <summary>Requests a non-unique index for a node property.</summary>
public sealed record CreateIndexOperation(string NodeType, string PropertyName) : MigrationOperation;

/// <summary>Requests removal of a graph node type.</summary>
public sealed record DropNodeTypeOperation(string NodeType) : MigrationOperation;

/// <summary>Requests removal of a graph relationship type.</summary>
public sealed record DropRelationTypeOperation(string RelationType) : MigrationOperation;

/// <summary>Requests removal of a named schema object such as an index or constraint.</summary>
public sealed record DropSchemaObjectOperation(
    string Name,
    MigrationSchemaObjectKind Kind = MigrationSchemaObjectKind.Constraint) : MigrationOperation;

/// <summary>Identifies a named schema object category.</summary>
public enum MigrationSchemaObjectKind
{
    Constraint,
    Index,
}

/// <summary>Describes one provider-neutral schema property.</summary>
public sealed record GraphSchemaProperty(string Name, Type ClrType);
