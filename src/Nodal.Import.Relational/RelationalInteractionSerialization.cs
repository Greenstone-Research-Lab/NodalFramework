using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nodal.Import.Relational;

/// <summary>Serializes and restores the open canonical relational interaction model format.</summary>
public static class RelationalInteractionModelJson
{
    /// <summary>Serializes a model as deterministic, human-readable JSON.</summary>
    /// <param name="model">Model to serialize.</param>
    /// <returns>Canonical JSON suitable for review and version control.</returns>
    public static string Serialize(RelationalInteractionModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return JsonSerializer.Serialize(model, RelationalInteractionJsonContext.Default.RelationalInteractionModel);
    }

    /// <summary>Restores a supported canonical interaction model from JSON.</summary>
    /// <param name="json">Canonical model JSON.</param>
    /// <returns>Validated interaction model.</returns>
    /// <exception cref="NotSupportedException">The document uses an unsupported format version.</exception>
    public static RelationalInteractionModel Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var model = JsonSerializer.Deserialize(json, RelationalInteractionJsonContext.Default.RelationalInteractionModel)
            ?? throw new JsonException("The relational interaction model document is empty.");
        if (!string.Equals(model.FormatVersion, RelationalInteractionFormat.CurrentVersion, StringComparison.Ordinal))
        {
            throw new NotSupportedException($"Relational interaction model format '{model.FormatVersion}' is not supported.");
        }

        return model;
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(RelationalInteractionModel))]
internal sealed partial class RelationalInteractionJsonContext : JsonSerializerContext;
