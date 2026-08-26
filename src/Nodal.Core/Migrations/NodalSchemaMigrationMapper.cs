namespace Nodal.Core.Migrations;

/// <summary>Builds safe M1 operations from a schema snapshot diff.</summary>
public static class NodalSchemaMigrationMapper
{
    /// <summary>
    /// Converts diffable node/relation/property changes into provider-neutral operations.
    /// Changes requiring inference are returned as manual review items.
    /// </summary>
    public static NodalSchemaMigrationPlan Map(
        NodalSchemaSnapshot before,
        NodalSchemaSnapshot after,
        NodalSchemaDiffOptions? options = null,
        Func<string, Type?>? typeResolver = null)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var previous = before.Normalize();
        var current = after.Normalize();
        var diff = NodalSchemaDiffer.Compare(previous, current, options);
        var resolver = typeResolver ?? Type.GetType;
        var operations = new List<MigrationOperation>();
        var manualReview = new List<NodalSchemaChange>();
        var oldNodes = previous.Nodes.ToDictionary(node => node.Name, StringComparer.Ordinal);
        var newNodes = current.Nodes.ToDictionary(node => node.Name, StringComparer.Ordinal);
        var oldRelations = previous.Relations.ToDictionary(relation => relation.Name, StringComparer.Ordinal);
        var newRelations = current.Relations.ToDictionary(relation => relation.Name, StringComparer.Ordinal);
        var newIndexes = (current.Indexes ?? []).ToDictionary(item => item.Name, StringComparer.Ordinal);
        var newConstraints = (current.Constraints ?? []).ToDictionary(item => item.Name, StringComparer.Ordinal);

        foreach (var change in diff.Changes)
        {
            switch (change.Kind)
            {
                case NodalSchemaChangeKind.NodeAdded:
                    operations.Add(CreateNode(newNodes[change.ObjectName], resolver));
                    break;
                case NodalSchemaChangeKind.NodeRemoved:
                    operations.Add(new DropNodeTypeOperation(change.ObjectName));
                    break;
                case NodalSchemaChangeKind.RelationAdded:
                    operations.Add(CreateRelation(newRelations[change.ObjectName], resolver));
                    break;
                case NodalSchemaChangeKind.RelationRemoved:
                    operations.Add(new DropRelationTypeOperation(change.ObjectName));
                    break;
                case NodalSchemaChangeKind.NodePropertyAdded:
                    operations.Add(new AddNodePropertyOperation(
                        change.ObjectName,
                        Property(newNodes[change.ObjectName], change.PropertyName!, resolver)));
                    break;
                case NodalSchemaChangeKind.NodePropertyRemoved:
                    operations.Add(new DropNodePropertyOperation(change.ObjectName, change.PropertyName!));
                    break;
                case NodalSchemaChangeKind.NodePropertyRenamed:
                    operations.Add(new RenameNodePropertyOperation(
                        change.ObjectName, change.PropertyName!, change.NewPropertyName!));
                    break;
                case NodalSchemaChangeKind.RelationPropertyAdded:
                    operations.Add(new AddRelationPropertyOperation(
                        change.ObjectName,
                        Property(newRelations[change.ObjectName], change.PropertyName!, resolver)));
                    break;
                case NodalSchemaChangeKind.RelationPropertyRemoved:
                    operations.Add(new DropRelationPropertyOperation(change.ObjectName, change.PropertyName!));
                    break;
                case NodalSchemaChangeKind.RelationPropertyRenamed:
                    operations.Add(new RenameRelationPropertyOperation(
                        change.ObjectName, change.PropertyName!, change.NewPropertyName!));
                    break;
                case NodalSchemaChangeKind.NodePropertyTypeChanged:
                    operations.Add(AlterNode(
                        oldNodes[change.ObjectName],
                        newNodes[change.ObjectName],
                        change.PropertyName!,
                        resolver));
                    break;
                case NodalSchemaChangeKind.RelationPropertyTypeChanged:
                    operations.Add(AlterRelation(
                        oldRelations[change.ObjectName],
                        newRelations[change.ObjectName],
                        change.PropertyName!,
                        resolver));
                    break;
                case NodalSchemaChangeKind.RelationShapeChanged:
                    manualReview.Add(change);
                    break;
                case NodalSchemaChangeKind.IndexAdded:
                    AddSchemaObject(
                        newIndexes[change.ObjectName],
                        unique: false,
                        change,
                        operations,
                        manualReview);
                    break;
                case NodalSchemaChangeKind.IndexRemoved:
                    operations.Add(new DropSchemaObjectOperation(
                        change.ObjectName,
                        MigrationSchemaObjectKind.Index));
                    break;
                case NodalSchemaChangeKind.ConstraintAdded:
                    AddSchemaObject(
                        newConstraints[change.ObjectName],
                        unique: true,
                        change,
                        operations,
                        manualReview);
                    break;
                case NodalSchemaChangeKind.ConstraintRemoved:
                    operations.Add(new DropSchemaObjectOperation(
                        change.ObjectName,
                        MigrationSchemaObjectKind.Constraint));
                    break;
                case NodalSchemaChangeKind.IndexChanged:
                case NodalSchemaChangeKind.ConstraintChanged:
                    manualReview.Add(change);
                    break;
            }
        }

        return new NodalSchemaMigrationPlan(operations, manualReview);
    }

    private static void AddSchemaObject(
        NodalSchemaObjectSnapshot schemaObject,
        bool unique,
        NodalSchemaChange change,
        List<MigrationOperation> operations,
        List<NodalSchemaChange> manualReview)
    {
        if (schemaObject.Properties.Count != 1 || (unique && !schemaObject.IsUnique))
        {
            manualReview.Add(change);
            return;
        }

        operations.Add(unique
            ? new CreateUniqueConstraintOperation(schemaObject.EntityName, schemaObject.Properties[0])
            : new CreateIndexOperation(schemaObject.EntityName, schemaObject.Properties[0]));
    }

    private static CreateNodeTypeOperation CreateNode(
        NodalNodeSnapshot node,
        Func<string, Type?> resolver) =>
        new(
            node.Name,
            node.KeyProperty,
            Resolve(resolver, node.ClrTypeName),
            node.Properties.Select(property => new GraphSchemaProperty(
                property.Name,
                Resolve(resolver, property.ClrTypeName))).ToArray());

    private static CreateRelationTypeOperation CreateRelation(
        NodalRelationSnapshot relation,
        Func<string, Type?> resolver) =>
        new(
            relation.Name,
            relation.SourceNode,
            relation.TargetNode,
            relation.Directed,
            relation.Properties.Select(property => new GraphSchemaProperty(
                property.Name,
                Resolve(resolver, property.ClrTypeName))).ToArray());

    private static GraphSchemaProperty Property(
        NodalNodeSnapshot node,
        string name,
        Func<string, Type?> resolver) =>
        new(name, Resolve(resolver, node.Properties.Single(property => property.Name == name).ClrTypeName));

    private static GraphSchemaProperty Property(
        NodalRelationSnapshot relation,
        string name,
        Func<string, Type?> resolver) =>
        new(name, Resolve(resolver, relation.Properties.Single(property => property.Name == name).ClrTypeName));

    private static AlterNodePropertyTypeOperation AlterNode(
        NodalNodeSnapshot before,
        NodalNodeSnapshot after,
        string propertyName,
        Func<string, Type?> resolver)
    {
        var oldProperty = before.Properties.Single(property => property.Name == propertyName);
        var newProperty = after.Properties.Single(property => property.Name == propertyName);
        return new(
            before.Name,
            propertyName,
            Resolve(resolver, oldProperty.ClrTypeName),
            Resolve(resolver, newProperty.ClrTypeName),
            MigrationPropertyTypeCompatibility.RequiresRewrite);
    }

    private static AlterRelationPropertyTypeOperation AlterRelation(
        NodalRelationSnapshot before,
        NodalRelationSnapshot after,
        string propertyName,
        Func<string, Type?> resolver)
    {
        var oldProperty = before.Properties.Single(property => property.Name == propertyName);
        var newProperty = after.Properties.Single(property => property.Name == propertyName);
        return new(
            before.Name,
            propertyName,
            Resolve(resolver, oldProperty.ClrTypeName),
            Resolve(resolver, newProperty.ClrTypeName),
            MigrationPropertyTypeCompatibility.RequiresRewrite);
    }

    private static Type Resolve(Func<string, Type?> resolver, string name) =>
        resolver(name)
        ?? throw new InvalidOperationException($"CLR type '{name}' could not be resolved for migration mapping.");
}

/// <summary>Contains safe migration operations and changes requiring manual review.</summary>
public sealed record NodalSchemaMigrationPlan(
    IReadOnlyList<MigrationOperation> Operations,
    IReadOnlyList<NodalSchemaChange> ManualReview)
{
    /// <summary>Gets whether any change still requires an explicit human decision.</summary>
    public bool RequiresManualReview => ManualReview.Count > 0;
}
