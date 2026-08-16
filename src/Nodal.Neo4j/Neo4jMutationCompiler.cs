using Nodal.Core.Mutations;
using Nodal.Core.Providers;

namespace Nodal.Neo4j;

/// <summary>Compiles provider-neutral graph mutations into parameterized Cypher commands.</summary>
public static class Neo4jMutationCompiler
{
    /// <summary>Compiles operations without embedding domain values in Cypher text.</summary>
    public static IReadOnlyList<GraphCommand> Compile(GraphMutationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.Operations.Select(CompileOperation).ToArray();
    }

    private static GraphCommand CompileOperation(GraphMutationOperation operation) => operation switch
    {
        CreateNodeOperation create => CompileCreateNode(create),
        UpdateNodeOperation update => CompileUpdateNode(update),
        DeleteNodeOperation delete => CompileDeleteNode(delete),
        CreateRelationOperation create => CompileCreateRelation(create),
        UpdateRelationOperation update => CompileUpdateRelation(update),
        DeleteRelationOperation delete => CompileDeleteRelation(delete),
        _ => throw new NotSupportedException(
            $"Mutation operation '{operation.GetType().Name}' is not supported by Neo4j."),
    };

    private static GraphCommand CompileCreateNode(CreateNodeOperation operation) => new(
        $"MERGE (`node`:{Escape(operation.Identity.NodeType)} {{{Escape(operation.Identity.KeyProperty)}: $key}}) " +
        "SET `node` += $properties",
        Parameters(
            ("key", operation.Identity.Value),
            ("properties", WithoutKey(operation.Properties, operation.Identity.KeyProperty))));

    private static GraphCommand CompileUpdateNode(UpdateNodeOperation operation) => new(
        $"MATCH (`node`:{Escape(operation.Identity.NodeType)} {{{Escape(operation.Identity.KeyProperty)}: $key}}) " +
        "SET `node` += $properties",
        Parameters(
            ("key", operation.Identity.Value),
            ("properties", WithoutKey(operation.Properties, operation.Identity.KeyProperty))));

    private static GraphCommand CompileDeleteNode(DeleteNodeOperation operation) => new(
        $"MATCH (`node`:{Escape(operation.Identity.NodeType)} {{{Escape(operation.Identity.KeyProperty)}: $key}}) " +
        "DETACH DELETE `node`",
        Parameters(("key", operation.Identity.Value)));

    private static GraphCommand CompileCreateRelation(CreateRelationOperation operation) => new(
        $"MATCH (`source`:{Escape(operation.Source.NodeType)} {{{Escape(operation.Source.KeyProperty)}: $sourceKey}}), " +
        $"(`target`:{Escape(operation.Target.NodeType)} {{{Escape(operation.Target.KeyProperty)}: $targetKey}}) " +
        $"MERGE (`source`)-[`relation`:{Escape(operation.RelationType)}]->(`target`) " +
        "SET `relation` += $properties",
        Parameters(
            ("sourceKey", operation.Source.Value),
            ("targetKey", operation.Target.Value),
            ("properties", operation.Properties)));

    private static GraphCommand CompileUpdateRelation(UpdateRelationOperation operation)
    {
        var relation = operation.Directed
            ? $"-[`relation`:{Escape(operation.RelationType)}]->"
            : $"-[`relation`:{Escape(operation.RelationType)}]-";
        var identityFilter = operation.ProviderId is null
            ? string.Empty
            : "WHERE elementId(`relation`) = $relationId ";
        var parameters = Parameters(
            ("sourceKey", operation.Source.Value),
            ("targetKey", operation.Target.Value),
            ("properties", operation.Properties));
        if (operation.ProviderId is not null)
        {
            parameters["relationId"] = operation.ProviderId;
        }

        return new GraphCommand(
            $"MATCH (`source`:{Escape(operation.Source.NodeType)} {{{Escape(operation.Source.KeyProperty)}: $sourceKey}})" +
            relation +
            $"(`target`:{Escape(operation.Target.NodeType)} {{{Escape(operation.Target.KeyProperty)}: $targetKey}}) " +
            identityFilter +
            "SET `relation` += $properties",
            parameters);
    }

    private static GraphCommand CompileDeleteRelation(DeleteRelationOperation operation)
    {
        var pattern = operation.Directed
            ? $"(`source`)-[`relation`:{Escape(operation.RelationType)}]->(`target`)"
            : $"(`source`)-[`relation`:{Escape(operation.RelationType)}]-(`target`)";
        return new GraphCommand(
            $"MATCH (`source`:{Escape(operation.Source.NodeType)} {{{Escape(operation.Source.KeyProperty)}: $sourceKey}}), " +
            $"(`target`:{Escape(operation.Target.NodeType)} {{{Escape(operation.Target.KeyProperty)}: $targetKey}}) " +
            $"MATCH {pattern} DELETE `relation`",
            Parameters(
                ("sourceKey", operation.Source.Value),
                ("targetKey", operation.Target.Value)));
    }

    private static Dictionary<string, object?> WithoutKey(
        IReadOnlyDictionary<string, object?> properties,
        string keyProperty) => properties
        .Where(property => !string.Equals(property.Key, keyProperty, StringComparison.Ordinal))
        .ToDictionary(property => property.Key, property => property.Value);

    private static Dictionary<string, object?> Parameters(
        params (string Name, object? Value)[] values) => values.ToDictionary(value => value.Name, value => value.Value);

    private static string Escape(string identifier) =>
        $"`{identifier.Replace("`", "``", StringComparison.Ordinal)}`";
}
