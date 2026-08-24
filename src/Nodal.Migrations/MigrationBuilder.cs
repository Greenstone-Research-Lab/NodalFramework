using System.Linq.Expressions;
using System.Reflection;
using Nodal.Core.Metadata;
using Nodal.Core.Migrations;

namespace Nodal.Migrations;

/// <summary>
/// Collects provider-neutral graph schema operations for a migration.
/// </summary>
public sealed class MigrationBuilder
{
    private readonly List<MigrationOperation> operations = [];

    /// <summary>
    /// Gets the ordered operations declared by the migration.
    /// </summary>
    public IReadOnlyList<MigrationOperation> Operations => operations;

    /// <summary>
    /// Declares a graph node type.
    /// </summary>
    public MigrationBuilder CreateNode<TNode>()
    {
        var type = typeof(TNode);
        var properties = DiscoverProperties(type);
        var key = type.GetProperties().SingleOrDefault(property => property.IsDefined(typeof(GraphKeyAttribute), true))
            ?? type.GetProperty("Id")
            ?? type.GetProperty($"{type.Name}Id")
            ?? throw new InvalidOperationException($"Node type '{type}' does not define a graph key.");
        operations.Add(new CreateNodeTypeOperation(
            type.GetCustomAttribute<GraphNodeAttribute>()?.Name ?? type.Name,
            GetGraphName(key),
            key.PropertyType,
            properties));
        return this;
    }

    /// <summary>
    /// Declares a directed graph relationship type.
    /// </summary>
    public MigrationBuilder CreateRelation<TRelation, TSource, TTarget>(bool directed = true)
    {
        var relationType = typeof(TRelation);
        var relationName = relationType
            .GetCustomAttribute<GraphRelationAttribute>()?.Name
            ?? relationType.Name;
        var sourceName = typeof(TSource)
            .GetCustomAttribute<GraphNodeAttribute>()?.Name
            ?? typeof(TSource).Name;
        var targetName = typeof(TTarget)
            .GetCustomAttribute<GraphNodeAttribute>()?.Name
            ?? typeof(TTarget).Name;

        ArgumentException.ThrowIfNullOrWhiteSpace(relationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);

        operations.Add(new CreateRelationTypeOperation(
            relationName,
            sourceName,
            targetName,
            directed,
            DiscoverProperties(relationType)));
        return this;
    }

    /// <summary>
    /// Declares a unique constraint for a node property.
    /// </summary>
    public MigrationBuilder CreateUniqueConstraint<TNode, TProperty>(
        Expression<Func<TNode, TProperty>> property)
    {
        var member = GetDirectMember(property);
        operations.Add(new CreateUniqueConstraintOperation(GetGraphTypeName<TNode>(), GetGraphName(member)));
        return this;
    }

    /// <summary>Declares a non-unique node property index.</summary>
    public MigrationBuilder CreateIndex<TNode, TProperty>(Expression<Func<TNode, TProperty>> property)
    {
        var member = GetDirectMember(property);
        operations.Add(new CreateIndexOperation(GetGraphTypeName<TNode>(), GetGraphName(member)));
        return this;
    }

    /// <summary>Declares removal of a node property index.</summary>
    public MigrationBuilder DropIndex<TNode, TProperty>(
        Expression<Func<TNode, TProperty>> property)
    {
        var member = GetDirectMember(property);
        operations.Add(new DropIndexOperation(
            GetGraphTypeName<TNode>(),
            GetGraphName(member)));
        return this;
    }

    /// <summary>Declares removal of a node property unique constraint.</summary>
    public MigrationBuilder DropUniqueConstraint<TNode, TProperty>(
        Expression<Func<TNode, TProperty>> property)
    {
        var member = GetDirectMember(property);
        operations.Add(new DropUniqueConstraintOperation(
            GetGraphTypeName<TNode>(),
            GetGraphName(member)));
        return this;
    }

    /// <summary>Declares addition of a node property.</summary>
    public MigrationBuilder AddNodeProperty<TNode, TProperty>(
        Expression<Func<TNode, TProperty>> property)
    {
        var member = GetDirectMember(property);
        operations.Add(new AddNodePropertyOperation(
            GetGraphTypeName<TNode>(),
            new GraphSchemaProperty(GetGraphName(member), typeof(TProperty))));
        return this;
    }

    /// <summary>Declares addition of a relationship property.</summary>
    public MigrationBuilder AddRelationProperty<TRelation, TProperty>(
        Expression<Func<TRelation, TProperty>> property)
    {
        var member = GetDirectMember(property);
        var relationType = typeof(TRelation)
            .GetCustomAttribute<GraphRelationAttribute>()?.Name
            ?? typeof(TRelation).Name;
        operations.Add(new AddRelationPropertyOperation(
            relationType,
            new GraphSchemaProperty(GetGraphName(member), typeof(TProperty))));
        return this;
    }

    /// <summary>Declares removal of a node property.</summary>
    public MigrationBuilder DropNodeProperty<TNode, TProperty>(
        Expression<Func<TNode, TProperty>> property)
    {
        var member = GetDirectMember(property);
        operations.Add(new DropNodePropertyOperation(
            GetGraphTypeName<TNode>(),
            GetGraphName(member)));
        return this;
    }

    /// <summary>Declares removal of a relationship property.</summary>
    public MigrationBuilder DropRelationProperty<TRelation, TProperty>(
        Expression<Func<TRelation, TProperty>> property)
    {
        var member = GetDirectMember(property);
        var relationType = typeof(TRelation)
            .GetCustomAttribute<GraphRelationAttribute>()?.Name
            ?? typeof(TRelation).Name;
        operations.Add(new DropRelationPropertyOperation(
            relationType,
            GetGraphName(member)));
        return this;
    }

    /// <summary>Declares an explicit node property rename.</summary>
    public MigrationBuilder RenameNodeProperty<TNode, TProperty>(
        Expression<Func<TNode, TProperty>> property,
        string newName)
    {
        var member = GetDirectMember(property);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        operations.Add(new RenameNodePropertyOperation(
            GetGraphTypeName<TNode>(),
            GetGraphName(member),
            newName));
        return this;
    }

    /// <summary>Declares an explicit relationship property rename.</summary>
    public MigrationBuilder RenameRelationProperty<TRelation, TProperty>(
        Expression<Func<TRelation, TProperty>> property,
        string newName)
    {
        var member = GetDirectMember(property);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        var relationType = typeof(TRelation)
            .GetCustomAttribute<GraphRelationAttribute>()?.Name
            ?? typeof(TRelation).Name;
        operations.Add(new RenameRelationPropertyOperation(
            relationType,
            GetGraphName(member),
            newName));
        return this;
    }

    /// <summary>Declares a node property type alteration.</summary>
    public MigrationBuilder AlterNodePropertyType<
        TNode,
        TOldProperty,
        TNewProperty>(
        Expression<Func<TNode, TOldProperty>> property,
        MigrationPropertyTypeCompatibility compatibility)
    {
        var member = GetDirectMember(property);
        operations.Add(new AlterNodePropertyTypeOperation(
            GetGraphTypeName<TNode>(),
            GetGraphName(member),
            typeof(TOldProperty),
            typeof(TNewProperty),
            compatibility));
        return this;
    }

    /// <summary>Declares a relationship property type alteration.</summary>
    public MigrationBuilder AlterRelationPropertyType<
        TRelation,
        TOldProperty,
        TNewProperty>(
        Expression<Func<TRelation, TOldProperty>> property,
        MigrationPropertyTypeCompatibility compatibility)
    {
        var member = GetDirectMember(property);
        var relationType = typeof(TRelation)
            .GetCustomAttribute<GraphRelationAttribute>()?.Name
            ?? typeof(TRelation).Name;
        operations.Add(new AlterRelationPropertyTypeOperation(
            relationType,
            GetGraphName(member),
            typeof(TOldProperty),
            typeof(TNewProperty),
            compatibility));
        return this;
    }

    /// <summary>Declares removal of a graph node type.</summary>
    public MigrationBuilder DropNode<TNode>()
    {
        operations.Add(new DropNodeTypeOperation(GetGraphTypeName<TNode>()));
        return this;
    }

    /// <summary>Declares removal of a graph relationship type.</summary>
    public MigrationBuilder DropRelation<TRelation>()
    {
        var type = typeof(TRelation);
        operations.Add(new DropRelationTypeOperation(
            type.GetCustomAttribute<GraphRelationAttribute>()?.Name ?? type.Name));
        return this;
    }

    /// <summary>Declares removal of a provider schema object by its stable migration name.</summary>
    public MigrationBuilder DropSchemaObject(
        string name,
        MigrationSchemaObjectKind kind = MigrationSchemaObjectKind.Constraint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        operations.Add(new DropSchemaObjectOperation(name, kind));
        return this;
    }

    private static MemberInfo GetDirectMember<TNode, TProperty>(Expression<Func<TNode, TProperty>> property)
    {
        ArgumentNullException.ThrowIfNull(property);
        var body = property.Body is UnaryExpression unary ? unary.Operand : property.Body;
        return body is MemberExpression member && member.Expression == property.Parameters[0]
            ? member.Member
            : throw new ArgumentException("A direct node property is required.", nameof(property));
    }

    private static GraphSchemaProperty[] DiscoverProperties(Type type) => type
        .GetProperties(BindingFlags.Instance | BindingFlags.Public)
        .Where(property => property.GetIndexParameters().Length == 0)
        .Where(property => !property.IsDefined(typeof(GraphIgnoreAttribute), true))
        .Select(property => new GraphSchemaProperty(GetGraphName(property), property.PropertyType))
        .ToArray();

    private static string GetGraphTypeName<TNode>() =>
        typeof(TNode).GetCustomAttribute<GraphNodeAttribute>()?.Name ?? typeof(TNode).Name;

    private static string GetGraphName(MemberInfo member) =>
        member.GetCustomAttribute<GraphPropertyAttribute>(true)?.Name ?? member.Name;
}
