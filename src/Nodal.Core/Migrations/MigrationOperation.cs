namespace Nodal.Core.Migrations;

/// <summary>
/// Represents a provider-neutral graph schema change.
/// </summary>
public abstract record MigrationOperation;

/// <summary>
/// Requests creation of a graph node type.
/// </summary>
/// <param name="NodeType">The provider-neutral node type.</param>
/// <param name="KeyProperty">The stable key property's storage name.</param>
/// <param name="KeyClrType">The key property's declared CLR type, when known.</param>
/// <param name="Properties">The properties declared for the node schema.</param>
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
/// <param name="Properties">The properties declared for the relationship schema.</param>
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

/// <summary>Requests removal of a node property index.</summary>
public sealed record DropIndexOperation(string NodeType, string PropertyName) : MigrationOperation;

/// <summary>Requests removal of a node property unique constraint.</summary>
public sealed record DropUniqueConstraintOperation(
    string NodeType,
    string PropertyName) : MigrationOperation;

/// <summary>Requests removal of a graph node type.</summary>
public sealed record DropNodeTypeOperation(string NodeType) : MigrationOperation;

/// <summary>Requests removal of a graph relationship type.</summary>
public sealed record DropRelationTypeOperation(string RelationType) : MigrationOperation;

/// <summary>Requests removal of a named schema object such as an index or constraint.</summary>
public sealed record DropSchemaObjectOperation(
    string Name,
    MigrationSchemaObjectKind Kind = MigrationSchemaObjectKind.Constraint) : MigrationOperation;

/// <summary>Requests addition of a property to a node schema.</summary>
/// <param name="NodeType">The node type receiving the property.</param>
/// <param name="Property">The property metadata.</param>
public sealed record AddNodePropertyOperation(
    string NodeType,
    GraphSchemaProperty Property) : MigrationOperation;

/// <summary>Requests addition of a property to a relationship schema.</summary>
/// <param name="RelationType">The relationship type receiving the property.</param>
/// <param name="Property">The property metadata.</param>
public sealed record AddRelationPropertyOperation(
    string RelationType,
    GraphSchemaProperty Property) : MigrationOperation;

/// <summary>Requests removal of a property from a node schema.</summary>
/// <param name="NodeType">The node type losing the property.</param>
/// <param name="PropertyName">The property storage name.</param>
public sealed record DropNodePropertyOperation(
    string NodeType,
    string PropertyName) : MigrationOperation;

/// <summary>Requests removal of a property from a relationship schema.</summary>
/// <param name="RelationType">The relationship type losing the property.</param>
/// <param name="PropertyName">The property storage name.</param>
public sealed record DropRelationPropertyOperation(
    string RelationType,
    string PropertyName) : MigrationOperation;

/// <summary>Requests an explicit node property rename.</summary>
/// <param name="NodeType">The affected node type.</param>
/// <param name="OldPropertyName">The existing property storage name.</param>
/// <param name="NewPropertyName">The new property storage name.</param>
public sealed record RenameNodePropertyOperation(
    string NodeType,
    string OldPropertyName,
    string NewPropertyName) : MigrationOperation;

/// <summary>Requests an explicit relationship property rename.</summary>
/// <param name="RelationType">The affected relationship type.</param>
/// <param name="OldPropertyName">The existing property storage name.</param>
/// <param name="NewPropertyName">The new property storage name.</param>
public sealed record RenameRelationPropertyOperation(
    string RelationType,
    string OldPropertyName,
    string NewPropertyName) : MigrationOperation;

/// <summary>Requests a node property type change with an explicit safety classification.</summary>
/// <param name="NodeType">The affected node type.</param>
/// <param name="PropertyName">The property storage name.</param>
/// <param name="OldClrType">The currently persisted CLR type.</param>
/// <param name="NewClrType">The requested CLR type.</param>
/// <param name="Compatibility">The provider-neutral compatibility classification.</param>
public sealed record AlterNodePropertyTypeOperation(
    string NodeType,
    string PropertyName,
    Type OldClrType,
    Type NewClrType,
    MigrationPropertyTypeCompatibility Compatibility) : MigrationOperation;

/// <summary>Requests a relationship property type change with an explicit safety classification.</summary>
public sealed record AlterRelationPropertyTypeOperation(
    string RelationType,
    string PropertyName,
    Type OldClrType,
    Type NewClrType,
    MigrationPropertyTypeCompatibility Compatibility) : MigrationOperation;

/// <summary>Classifies the safety of a persisted property type change.</summary>
public enum MigrationPropertyTypeCompatibility
{
    /// <summary>The provider can apply the change without rewriting existing values.</summary>
    Compatible,

    /// <summary>The change requires a controlled rewrite or backfill.</summary>
    RequiresRewrite,

    /// <summary>The change may lose data and requires explicit approval.</summary>
    Destructive,
}

/// <summary>Identifies a named schema object category.</summary>
public enum MigrationSchemaObjectKind
{
    /// <summary>Identifies a graph schema constraint.</summary>
    Constraint,

    /// <summary>Identifies a graph schema index.</summary>
    Index,
}

/// <summary>Describes one provider-neutral schema property.</summary>
public sealed record GraphSchemaProperty(string Name, Type ClrType);
