using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nodal.Import.Csv;

/// <summary>Describes a versioned, provider-neutral CSV-to-graph mapping document.</summary>
/// <param name="FormatVersion">Mapping document format version.</param>
/// <param name="Nodes">Node mappings evaluated for every CSV record.</param>
/// <param name="Relations">Relation mappings evaluated after their endpoint nodes.</param>
public sealed record CsvGraphImportDefinition(
    int FormatVersion,
    IReadOnlyList<CsvGraphNodeDefinition> Nodes,
    IReadOnlyList<CsvGraphRelationDefinition> Relations)
{
    /// <summary>Gets the mapping format understood by this release.</summary>
    public const int CurrentFormatVersion = 1;
}

/// <summary>Maps CSV columns to one graph node.</summary>
/// <param name="Name">Stable mapping name used by relations.</param>
/// <param name="Type">Provider-neutral graph node type.</param>
/// <param name="KeyColumn">CSV column containing the stable identity.</param>
/// <param name="KeyProperty">Graph property representing the stable identity.</param>
/// <param name="Properties">Additional column-to-property mappings.</param>
public sealed record CsvGraphNodeDefinition(
    string Name,
    string Type,
    string KeyColumn,
    string KeyProperty,
    IReadOnlyList<CsvGraphPropertyDefinition> Properties);

/// <summary>Maps two node mappings and optional CSV properties to one graph relation.</summary>
/// <param name="Name">Stable relation mapping name.</param>
/// <param name="Source">Source node mapping name.</param>
/// <param name="Target">Target node mapping name.</param>
/// <param name="Type">Provider-neutral graph relation type.</param>
/// <param name="Directed">Whether direction is semantically significant.</param>
/// <param name="Properties">Additional column-to-property mappings.</param>
public sealed record CsvGraphRelationDefinition(
    string Name,
    string Source,
    string Target,
    string Type,
    bool Directed,
    IReadOnlyList<CsvGraphPropertyDefinition> Properties);

/// <summary>Maps one normalized CSV column to a graph property.</summary>
/// <param name="Column">CSV source column.</param>
/// <param name="Property">Provider-neutral graph property name.</param>
public sealed record CsvGraphPropertyDefinition(string Column, string Property);

/// <summary>Reads and validates versioned CSV graph mapping documents.</summary>
public static class CsvGraphImportDefinitionSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Deserializes and validates a JSON mapping document.</summary>
    /// <param name="json">Mapping JSON.</param>
    /// <returns>The validated mapping definition.</returns>
    public static CsvGraphImportDefinition Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        CsvGraphImportDefinition definition;
        try
        {
            definition = JsonSerializer.Deserialize<CsvGraphImportDefinition>(json, Options)
                ?? throw new CsvGraphImportDefinitionException("The CSV import mapping is empty.");
        }
        catch (JsonException exception)
        {
            throw new CsvGraphImportDefinitionException("The CSV import mapping contains invalid JSON.", exception);
        }

        Validate(definition);
        return definition;
    }

    private static void Validate(CsvGraphImportDefinition definition)
    {
        if (definition.FormatVersion != CsvGraphImportDefinition.CurrentFormatVersion)
        {
            throw new CsvGraphImportDefinitionException(
                $"CSV import mapping format version '{definition.FormatVersion}' is not supported.");
        }

        if (definition.Nodes is null || definition.Nodes.Count == 0)
        {
            throw new CsvGraphImportDefinitionException("A CSV import mapping must define at least one node.");
        }

        if (definition.Relations is null)
        {
            throw new CsvGraphImportDefinitionException("CSV import mapping relations cannot be null.");
        }

        foreach (var node in definition.Nodes)
        {
            if (node is null)
            {
                throw new CsvGraphImportDefinitionException("CSV import node mappings cannot be null.");
            }

            Require(node.Name, "node name");
            Require(node.Type, "node type");
            Require(node.KeyColumn, "node key column");
            Require(node.KeyProperty, "node key property");
            ValidateProperties(node.Properties, node.Name);
        }

        foreach (var relation in definition.Relations)
        {
            if (relation is null)
            {
                throw new CsvGraphImportDefinitionException("CSV import relation mappings cannot be null.");
            }

            Require(relation.Name, "relation name");
            Require(relation.Source, "relation source");
            Require(relation.Target, "relation target");
            Require(relation.Type, "relation type");
            ValidateProperties(relation.Properties, relation.Name);
        }
    }

    private static void ValidateProperties(
        IReadOnlyList<CsvGraphPropertyDefinition>? properties,
        string mappingName)
    {
        if (properties is null)
        {
            throw new CsvGraphImportDefinitionException(
                $"CSV import mapping '{mappingName}' properties cannot be null.");
        }

        foreach (var property in properties)
        {
            if (property is null)
            {
                throw new CsvGraphImportDefinitionException(
                    $"CSV import mapping '{mappingName}' properties cannot contain null values.");
            }

            Require(property.Column, "property column");
            Require(property.Property, "graph property name");
        }
    }

    private static void Require(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new CsvGraphImportDefinitionException($"A CSV import {label} is required.");
        }
    }
}

/// <summary>Compiles declarative CSV mapping documents into the common import mapping model.</summary>
public static class CsvGraphImportDefinitionCompiler
{
    /// <summary>Compiles a validated definition for normalized CSV records.</summary>
    /// <param name="definition">CSV graph mapping definition.</param>
    /// <returns>A provider-neutral import mapping.</returns>
    public static GraphImportMapping<CsvImportRecord> Compile(CsvGraphImportDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var builder = GraphImportMapping.For<CsvImportRecord>();
        foreach (var node in definition.Nodes)
        {
            builder.Node<CsvGraphNode>(
                node.Name,
                node.Type,
                node.KeyProperty,
                record => Read(record, node.KeyColumn),
                properties => AddProperties(properties, node.Properties));
        }

        foreach (var relation in definition.Relations)
        {
            builder.Relation(
                relation.Name,
                relation.Source,
                relation.Target,
                relation.Type,
                relation.Directed,
                properties => AddProperties(properties, relation.Properties));
        }

        return builder.Build();
    }

    private static void AddProperties(
        GraphImportPropertyBuilder<CsvImportRecord> builder,
        IEnumerable<CsvGraphPropertyDefinition> properties)
    {
        foreach (var property in properties)
        {
            builder.Property(property.Property, record => Read(record, property.Column));
        }
    }

    private static string? Read(CsvImportRecord record, string column) =>
        record.TryGetValue(column, out var value) ? value : null;

    private sealed class CsvGraphNode;
}

/// <summary>Reports an invalid or unsupported CSV graph mapping document.</summary>
public sealed class CsvGraphImportDefinitionException : InvalidOperationException
{
    /// <summary>Initializes an import definition error.</summary>
    /// <param name="message">Payload-safe validation message.</param>
    public CsvGraphImportDefinitionException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes an import definition error with its parsing cause.</summary>
    /// <param name="message">Payload-safe validation message.</param>
    /// <param name="innerException">Underlying JSON parsing failure.</param>
    public CsvGraphImportDefinitionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
