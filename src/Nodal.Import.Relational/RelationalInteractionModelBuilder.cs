using System.Security.Cryptography;
using System.Text;

namespace Nodal.Import.Relational;

/// <summary>Builds a deterministic relational interaction network from discovered database metadata.</summary>
public static class RelationalInteractionModelBuilder
{
    /// <summary>
    /// Builds a lossless structural model without inferring domain verbs or mutating the source database.
    /// </summary>
    /// <param name="schema">Discovered relational metadata.</param>
    /// <param name="providerName">Optional provider family recorded as source evidence.</param>
    /// <returns>A deterministic interaction model suitable for review, versioning, and visualization export.</returns>
    /// <example>
    /// <code>
    /// var snapshot = await adapter.ReadAsync(connection, cancellationToken);
    /// var model = RelationalInteractionModelBuilder.Build(snapshot, adapter.ProviderName);
    /// string json = RelationalInteractionModelJson.Serialize(model);
    /// </code>
    /// </example>
    public static RelationalInteractionModel Build(RelationalSchemaSnapshot schema, string? providerName = null)
    {
        ArgumentNullException.ThrowIfNull(schema);
        var diagnostics = schema.Diagnostics.ToList();
        var tables = schema.Tables.OrderBy(table => ObjectId(table.Schema, table.Name), StringComparer.Ordinal).ToArray();
        var tableMap = tables.ToDictionary(table => ObjectId(table.Schema, table.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var foreignKey in schema.ForeignKeys)
        {
            AddExternal(tableMap, foreignKey.SourceSchema, foreignKey.SourceTable, diagnostics);
            AddExternal(tableMap, foreignKey.TargetSchema, foreignKey.TargetTable, diagnostics);
        }

        var outgoing = schema.ForeignKeys
            .GroupBy(foreignKey => ObjectId(foreignKey.SourceSchema, foreignKey.SourceTable), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var objects = tableMap.Values
            .OrderBy(table => ObjectId(table.Schema, table.Name), StringComparer.Ordinal)
            .Select(table => BuildObject(table, outgoing.GetValueOrDefault(ObjectId(table.Schema, table.Name), [])))
            .ToArray();
        var objectMap = objects.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var relations = schema.ForeignKeys
            .OrderBy(ForeignKeySortKey, StringComparer.Ordinal)
            .Select(foreignKey => BuildRelation(foreignKey, objectMap))
            .ToArray();

        return new RelationalInteractionModel(
            RelationalInteractionFormat.CurrentVersion,
            new RelationalInteractionSource(providerName, schema.DatabaseName, Fingerprint(tables, schema.ForeignKeys)),
            objects,
            relations,
            diagnostics.Order(StringComparer.Ordinal).ToArray());
    }

    private static RelationalInteractionObject BuildObject(
        RelationalTable table,
        IReadOnlyList<RelationalForeignKey> outgoing)
    {
        var columns = table.Columns.OrderBy(column => column.Ordinal).Select(column => new RelationalInteractionColumn(
            column.Name,
            column.DataType,
            column.IsNullable,
            column.Ordinal,
            column.IsPrimaryKey)).ToArray();
        return new RelationalInteractionObject(
            ObjectId(table.Schema, table.Name),
            table.Schema,
            table.Name,
            table.Kind,
            Classify(table, outgoing),
            columns);
    }

    private static RelationalInteractionObjectRole Classify(
        RelationalTable table,
        IReadOnlyList<RelationalForeignKey> outgoing)
    {
        if (string.Equals(table.Kind, "EXTERNAL", StringComparison.OrdinalIgnoreCase))
        {
            return RelationalInteractionObjectRole.External;
        }

        if (table.Kind.Contains("VIEW", StringComparison.OrdinalIgnoreCase))
        {
            return RelationalInteractionObjectRole.View;
        }

        var primaryKeys = table.Columns.Where(column => column.IsPrimaryKey).Select(column => column.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var foreignKeyColumns = outgoing.SelectMany(foreignKey => foreignKey.Columns).Select(column => column.SourceColumn)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (outgoing.Count >= 2 && primaryKeys.Count > 0 && primaryKeys.IsSubsetOf(foreignKeyColumns))
        {
            return RelationalInteractionObjectRole.Association;
        }

        return primaryKeys.Count > 0 ? RelationalInteractionObjectRole.Entity : RelationalInteractionObjectRole.Unknown;
    }

    private static RelationalInteractionRelation BuildRelation(
        RelationalForeignKey foreignKey,
        Dictionary<string, RelationalInteractionObject> objects)
    {
        var sourceId = ObjectId(foreignKey.SourceSchema, foreignKey.SourceTable);
        var targetId = ObjectId(foreignKey.TargetSchema, foreignKey.TargetTable);
        var source = objects[sourceId];
        var target = objects[targetId];
        var sourceName = TypeName(source.Name);
        var targetName = TypeName(target.Name);
        var reverse = source.Role == RelationalInteractionObjectRole.Association &&
            sourceName.StartsWith(targetName, StringComparison.OrdinalIgnoreCase);
        var display = reverse
            ? new RelationalInteractionDisplay(targetId, sourceId, $"HAS_{UpperSnake(sourceName)}", true, true)
            : new RelationalInteractionDisplay(sourceId, targetId, $"REFERENCES_{UpperSnake(targetName)}", false, true);

        return new RelationalInteractionRelation(
            $"{sourceId}:{foreignKey.Name}",
            foreignKey.Name,
            new RelationalInteractionEndpoint(sourceId, foreignKey.Columns.OrderBy(column => column.Ordinal).Select(column => column.SourceColumn).ToArray()),
            new RelationalInteractionEndpoint(targetId, foreignKey.Columns.OrderBy(column => column.Ordinal).Select(column => column.TargetColumn).ToArray()),
            foreignKey.OnDelete,
            foreignKey.OnUpdate,
            display);
    }

    private static void AddExternal(
        Dictionary<string, RelationalTable> tables,
        string schema,
        string name,
        List<string> diagnostics)
    {
        var id = ObjectId(schema, name);
        if (tables.ContainsKey(id))
        {
            return;
        }

        tables.Add(id, new RelationalTable(schema, name, "EXTERNAL", []));
        diagnostics.Add($"Foreign-key endpoint '{id}' was not present in the discovered object collection and was retained as external evidence.");
    }

    private static string Fingerprint(
        IEnumerable<RelationalTable> tables,
        IEnumerable<RelationalForeignKey> foreignKeys)
    {
        var canonical = new StringBuilder();
        foreach (var table in tables.OrderBy(table => ObjectId(table.Schema, table.Name), StringComparer.Ordinal))
        {
            canonical.Append("O|").Append(table.Schema).Append('|').Append(table.Name).Append('|').Append(table.Kind).AppendLine();
            foreach (var column in table.Columns.OrderBy(column => column.Ordinal))
            {
                canonical.Append("C|").Append(column.Ordinal).Append('|').Append(column.Name).Append('|').Append(column.DataType)
                    .Append('|').Append(column.IsNullable ? '1' : '0').Append('|').Append(column.IsPrimaryKey ? '1' : '0').AppendLine();
            }
        }

        foreach (var foreignKey in foreignKeys.OrderBy(ForeignKeySortKey, StringComparer.Ordinal))
        {
            canonical.Append("F|").Append(ForeignKeySortKey(foreignKey)).Append('|').Append(foreignKey.OnDelete)
                .Append('|').Append(foreignKey.OnUpdate).AppendLine();
            foreach (var pair in foreignKey.Columns.OrderBy(column => column.Ordinal))
            {
                canonical.Append("P|").Append(pair.Ordinal).Append('|').Append(pair.SourceColumn).Append('|').Append(pair.TargetColumn).AppendLine();
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
    }

    private static string ForeignKeySortKey(RelationalForeignKey foreignKey) =>
        $"{ObjectId(foreignKey.SourceSchema, foreignKey.SourceTable)}|{foreignKey.Name}|{ObjectId(foreignKey.TargetSchema, foreignKey.TargetTable)}";

    private static string ObjectId(string schema, string name) => $"{schema}.{name}";

    private static string TypeName(string name)
    {
        var words = SplitWords(name);
        if (words.Count == 0)
        {
            return "Object";
        }

        words[^1] = Singularize(words[^1]);
        return string.Concat(words.Select(word => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()));
    }

    private static string UpperSnake(string value) => string.Join('_', SplitWords(value).Select(word => word.ToUpperInvariant()));

    private static List<string> SplitWords(string value)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        foreach (var character in value)
        {
            if (!char.IsLetterOrDigit(character))
            {
                Flush(words, current);
                continue;
            }

            if (current.Length > 0 && char.IsUpper(character) && char.IsLower(current[^1]))
            {
                Flush(words, current);
            }

            current.Append(character);
        }

        Flush(words, current);
        return words;
    }

    private static void Flush(List<string> words, StringBuilder current)
    {
        if (current.Length == 0)
        {
            return;
        }

        words.Add(current.ToString());
        current.Clear();
    }

    private static string Singularize(string word)
    {
        if (word.EndsWith("ies", StringComparison.OrdinalIgnoreCase) && word.Length > 3)
        {
            return word[..^3] + "y";
        }

        return word.EndsWith('s') && !word.EndsWith("ss", StringComparison.OrdinalIgnoreCase) &&
            !word.EndsWith("us", StringComparison.OrdinalIgnoreCase)
            ? word[..^1]
            : word;
    }
}
