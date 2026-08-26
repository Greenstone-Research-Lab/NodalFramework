using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Nodal.Core.Migrations;

/// <summary>Serializes normalized schema snapshots into stable JSON and hashes.</summary>
public static class NodalSchemaSnapshotSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <summary>Serializes a normalized snapshot without environment-specific whitespace.</summary>
    public static string Serialize(NodalSchemaSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return JsonSerializer.Serialize(snapshot.Normalize(), Options);
    }

    /// <summary>
    /// Loads and normalizes a persisted snapshot after validating its independently
    /// versioned wire format.
    /// </summary>
    public static NodalSchemaSnapshot Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var snapshot = JsonSerializer.Deserialize<NodalSchemaSnapshot>(json, Options)
            ?? throw new JsonException("The schema snapshot payload is empty.");

        ValidateVersion(snapshot.FormatVersion);
        return snapshot.Normalize();
    }

    /// <summary>Computes a lowercase SHA-256 hash of the canonical snapshot JSON.</summary>
    public static string ComputeHash(NodalSchemaSnapshot snapshot) =>
        Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(Serialize(snapshot))))
        .ToLowerInvariant();

    private static void ValidateVersion(int formatVersion)
    {
        if (formatVersion == NodalSchemaSnapshot.CurrentFormatVersion)
        {
            return;
        }

        throw new NodalSchemaSnapshotVersionException(
            formatVersion,
            NodalSchemaSnapshot.CurrentFormatVersion);
    }
}

/// <summary>Indicates that a persisted schema snapshot uses an unsupported wire format.</summary>
public sealed class NodalSchemaSnapshotVersionException(
    int actualVersion,
    int supportedVersion)
    : NotSupportedException(
        $"Schema snapshot format version '{actualVersion}' is not supported. " +
        $"This package supports version '{supportedVersion}'.")
{
    /// <summary>Gets the version found in the persisted snapshot.</summary>
    public int ActualVersion { get; } = actualVersion;

    /// <summary>Gets the version supported by the current package.</summary>
    public int SupportedVersion { get; } = supportedVersion;
}
