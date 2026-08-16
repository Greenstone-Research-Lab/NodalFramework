using System.Text.Json;

namespace Nodal.Core.Execution;

/// <summary>
/// Materializes normalized graph node properties through the platform JSON contract.
/// </summary>
public sealed class JsonGraphResultMaterializer : IGraphResultMaterializer
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <inheritdoc />
    public IReadOnlyList<TNode> Materialize<TNode>(GraphQueryResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.Nodes.Select(node =>
        {
            var properties = new Dictionary<string, object?>(node.Properties, StringComparer.OrdinalIgnoreCase);
            properties.TryAdd("Id", node.Id);
            var json = JsonSerializer.Serialize(properties, SerializerOptions);
            return JsonSerializer.Deserialize<TNode>(json, SerializerOptions)
                ?? throw new InvalidOperationException($"Node '{node.Id}' could not be materialized as '{typeof(TNode)}'.");
        }).ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyList<Model.GraphPath<TSource, TRelation, TTarget>> MaterializePaths<TSource, TRelation, TTarget>(
        GraphQueryResult result)
        where TRelation : notnull
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.PathRecords.Select(path => new Model.GraphPath<TSource, TRelation, TTarget>(
            Materialize<TSource>(path.Source),
            Materialize<TRelation>(path.Relation.Properties, path.Relation.Id),
            Materialize<TTarget>(path.Target))).ToArray();
    }

    private static T Materialize<T>(GraphNodeRecord node) => Materialize<T>(node.Properties, node.Id);

    private static T Materialize<T>(IReadOnlyDictionary<string, object?> source, object id)
    {
        var properties = new Dictionary<string, object?>(source, StringComparer.OrdinalIgnoreCase);
        properties.TryAdd("Id", id);
        var json = JsonSerializer.Serialize(properties, SerializerOptions);
        return JsonSerializer.Deserialize<T>(json, SerializerOptions)
            ?? throw new InvalidOperationException($"Graph value '{id}' could not be materialized as '{typeof(T)}'.");
    }
}
