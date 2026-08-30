using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nodal.Core.Modeling;

/// <summary>Serializes, validates, and fingerprints canonical graph model descriptors.</summary>
public static class GraphModelDescriptorJson
{
    private static readonly JsonSerializerOptions CompactOptions = CreateOptions(false);
    private static readonly JsonSerializerOptions IndentedOptions = CreateOptions(true);

    /// <summary>Serializes a descriptor as deterministic, human-readable JSON.</summary>
    public static string Serialize(GraphModelDescriptor descriptor)
    {
        var canonical = Canonicalize(descriptor);
        return JsonSerializer.Serialize(canonical, IndentedOptions);
    }

    /// <summary>Restores and validates a descriptor from canonical JSON.</summary>
    public static GraphModelDescriptor Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var descriptor = JsonSerializer.Deserialize<GraphModelDescriptor>(json, CompactOptions)
            ?? throw new JsonException("The graph model descriptor document is empty.");
        return Canonicalize(descriptor);
    }

    /// <summary>Computes a lowercase SHA-256 fingerprint over compact canonical JSON.</summary>
    public static string ComputeFingerprint(GraphModelDescriptor descriptor)
    {
        var canonical = Canonicalize(descriptor);
        var json = JsonSerializer.Serialize(canonical, CompactOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    /// <summary>Returns a validated descriptor with deterministic member ordering.</summary>
    public static GraphModelDescriptor Canonicalize(GraphModelDescriptor descriptor)
    {
        GraphModelDescriptorValidator.ThrowIfInvalid(descriptor);
        return descriptor with
        {
            Nodes = descriptor.Nodes.OrderBy(node => node.Id, StringComparer.Ordinal)
                .Select(node => node with
                {
                    Properties = node.Properties.OrderBy(property => property.Name, StringComparer.Ordinal)
                        .Select(Canonicalize).ToArray(),
                    ProviderAnnotations = Sort(node.ProviderAnnotations),
                }).ToArray(),
            Relations = descriptor.Relations.OrderBy(relation => relation.Id, StringComparer.Ordinal)
                .Select(relation => relation with
                {
                    Properties = relation.Properties.OrderBy(property => property.Name, StringComparer.Ordinal)
                        .Select(Canonicalize).ToArray(),
                    ProviderAnnotations = Sort(relation.ProviderAnnotations),
                }).ToArray(),
            ProviderAnnotations = Sort(descriptor.ProviderAnnotations),
        };
    }

    private static GraphPropertyDescriptor Canonicalize(GraphPropertyDescriptor property) =>
        property with { ProviderAnnotations = Sort(property.ProviderAnnotations) };

    private static SortedDictionary<string, string>? Sort(IReadOnlyDictionary<string, string>? annotations) =>
        annotations is null
            ? null
            : new SortedDictionary<string, string>(
                annotations.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                StringComparer.Ordinal);

    private static JsonSerializerOptions CreateOptions(bool indented)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            WriteIndented = indented,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
