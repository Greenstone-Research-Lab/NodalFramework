using Neo4j.Driver;
using Nodal.Core.Migrations;
using System.Collections;
using System.Globalization;

namespace Nodal.Neo4j;

/// <summary>Reads Neo4j's documented schema procedures into a Nodal snapshot.</summary>
public sealed class Neo4jSchemaIntrospector : IGraphSchemaIntrospector
{
    private readonly IDriver driver;
    private readonly string? database;

    /// <summary>Initializes an introspector over an externally managed Neo4j driver.</summary>
    public Neo4jSchemaIntrospector(IDriver driver, string? database = null)
    {
        this.driver = driver ?? throw new ArgumentNullException(nameof(driver));
        this.database = database;
    }

    /// <inheritdoc />
    public async ValueTask<NodalSchemaSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var session = driver.AsyncSession(ConfigureSession);
        var nodes = await ReadAsync(session, "CALL db.schema.nodeTypeProperties() YIELD nodeLabels, propertyName, propertyTypes, mandatory RETURN nodeLabels, propertyName, propertyTypes, mandatory", cancellationToken).ConfigureAwait(false);
        var relations = await ReadAsync(session, "CALL db.schema.relTypeProperties() YIELD relType, propertyName, propertyTypes, mandatory RETURN relType, propertyName, propertyTypes, mandatory", cancellationToken).ConfigureAwait(false);
        var indexes = await ReadAsync(session, "SHOW INDEXES YIELD name, type, entityType, labelsOrTypes, properties, owningConstraint RETURN name, type, entityType, labelsOrTypes, properties, owningConstraint", cancellationToken).ConfigureAwait(false);
        var constraints = await ReadAsync(session, "SHOW CONSTRAINTS YIELD name, type, entityType, labelsOrTypes, properties RETURN name, type, entityType, labelsOrTypes, properties", cancellationToken).ConfigureAwait(false);

        return new NodalSchemaSnapshot(
            NodalSchemaSnapshot.CurrentFormatVersion,
            ParseNodes(nodes),
            ParseRelations(relations),
            "Neo4j",
            typeof(IDriver).Assembly.GetName().Version?.ToString(),
            ParseObjects(indexes),
            ParseObjects(constraints)).Normalize();
    }

    private void ConfigureSession(SessionConfigBuilder builder)
    {
        if (!string.IsNullOrWhiteSpace(database)) builder.WithDatabase(database);
    }

    private static async Task<IReadOnlyList<IRecord>> ReadAsync(IAsyncSession session, string text, CancellationToken cancellationToken)
    {
        return await session.ExecuteReadAsync(async transaction =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cursor = await transaction.RunAsync(text).ConfigureAwait(false);
            return await cursor.ToListAsync(cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    private static NodalNodeSnapshot[] ParseNodes(IEnumerable<IRecord> records)
    {
        return records.SelectMany(record => Strings(record, "nodeLabels").Select(label =>
            new NodalNodeSnapshot(label, typeof(object).FullName!, "", Property(record)))).ToArray();
    }

    private static NodalRelationSnapshot[] ParseRelations(IEnumerable<IRecord> records)
    {
        return records.Select(record =>
        {
            var name = String(record, "relType");
            return new NodalRelationSnapshot(name, typeof(object).FullName!, "", "", true, Property(record));
        }).ToArray();
    }

    private static NodalPropertySnapshot[] Property(IRecord record)
    {
        var name = String(record, "propertyName");
        if (name.Length == 0) return [];
        var types = Strings(record, "propertyTypes");
        return [new NodalPropertySnapshot(name, name, typeof(object).FullName!, true, false, types, types.Length > 0 ? types[0] : null)];
    }

    private static NodalSchemaObjectSnapshot[] ParseObjects(IEnumerable<IRecord> records) => records
        .Where(record => String(record, "name").Length > 0)
        .Select(record => new NodalSchemaObjectSnapshot(
            String(record, "name"), String(record, "type"), String(record, "entityType"),
            Strings(record, "properties"), String(record, "type").Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)))
        .ToArray();

    private static string String(IRecord record, string key) => record.Values.TryGetValue(key, out var value) && value is not null
        ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty : string.Empty;

    private static string[] Strings(IRecord record, string key)
    {
        if (!record.Values.TryGetValue(key, out var value) || value is null) return [];
        if (value is string text) return [text];
        return value is IEnumerable values ? values.Cast<object?>().Select(item => Convert.ToString(item, CultureInfo.InvariantCulture) ?? string.Empty).Where(item => item.Length > 0).ToArray() : [String(record, key)];
    }
}
