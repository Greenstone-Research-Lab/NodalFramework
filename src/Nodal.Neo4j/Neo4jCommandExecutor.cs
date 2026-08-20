using Neo4j.Driver;
using Nodal.Core.Execution;
using Nodal.Core.Providers;

namespace Nodal.Neo4j;

/// <summary>
/// Executes Cypher commands through a long-lived Neo4j driver and converts Bolt values
/// into Nodal's provider-neutral result representation.
/// </summary>
public sealed class Neo4jCommandExecutor : IGraphCommandExecutor
{
    private readonly IDriver driver;
    private readonly string? database;

    /// <summary>
    /// Initializes an executor using an externally managed driver. A single driver should
    /// normally be shared for the lifetime of the application so its connection pool can be reused.
    /// </summary>
    public Neo4jCommandExecutor(IDriver driver, string? database = null)
    {
        ArgumentNullException.ThrowIfNull(driver);
        this.driver = driver;
        this.database = database;
    }

    /// <inheritdoc />
    public async ValueTask<GraphQueryResult> ExecuteAsync(
        GraphCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        await using var session = driver.AsyncSession(ConfigureSession);
        var parameters = command.Parameters.ToDictionary(
            parameter => parameter.Key,
            parameter => parameter.Value!);

        var records = await session.ExecuteReadAsync(async transaction =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cursor = await transaction.RunAsync(command.Text, parameters).ConfigureAwait(false);
            return await cursor.ToListAsync(cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);

        return Normalize(records);
    }

    private void ConfigureSession(SessionConfigBuilder builder)
    {
        if (!string.IsNullOrWhiteSpace(database))
        {
            builder.WithDatabase(database);
        }
    }

    private static GraphQueryResult Normalize(IEnumerable<IRecord> records)
    {
        var nodes = new List<GraphNodeRecord>();
        var relations = new List<GraphRelationRecord>();
        var paths = new List<GraphPathRecord>();
        var scalars = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            var recordNodes = record.Values.Values.OfType<INode>().Select(NormalizeNode).ToArray();
            var recordRelations = record.Values.Values.OfType<IRelationship>().Select(NormalizeRelation).ToArray();
            nodes.AddRange(recordNodes);
            relations.AddRange(recordRelations);
            if (recordNodes.Length == 2 && recordRelations.Length == 1)
            {
                paths.Add(new GraphPathRecord(recordNodes[0], recordRelations[0], recordNodes[1]));
            }

            foreach (var value in record.Values.Values)
            {
                if (value is not INode && value is not IRelationship)
                {
                    CollectNodes(value, nodes);
                }
            }
            foreach (var scalar in record.Values.Where(value =>
                         value.Value is not INode && value.Value is not IRelationship &&
                         value.Value is not System.Collections.IEnumerable))
            {
                scalars[scalar.Key] = scalar.Value;
            }
        }

        return new GraphQueryResult(nodes, relations, paths, scalars);
    }

    private static void CollectNodes(object? value, ICollection<GraphNodeRecord> nodes)
    {
        switch (value)
        {
            case INode node:
                nodes.Add(NormalizeNode(node));
                break;
            case IReadOnlyDictionary<string, object> map:
                foreach (var item in map.Values)
                {
                    CollectNodes(item, nodes);
                }

                break;
            case IEnumerable<object> sequence when value is not string:
                foreach (var item in sequence)
                {
                    CollectNodes(item, nodes);
                }

                break;
        }
    }

    private static GraphNodeRecord NormalizeNode(INode node) => new(
        node.Labels.Count > 0 ? node.Labels[0] : string.Empty,
        node.ElementId,
        node.Properties.ToDictionary(property => property.Key, property => (object?)property.Value));

    private static GraphRelationRecord NormalizeRelation(IRelationship relation) => new(
        relation.Type,
        relation.ElementId,
        relation.StartNodeElementId,
        relation.EndNodeElementId,
        relation.Properties.ToDictionary(property => property.Key, property => (object?)property.Value));
}
