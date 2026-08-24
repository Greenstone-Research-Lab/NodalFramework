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

    /// <summary>Computes a lowercase SHA-256 hash of the canonical snapshot JSON.</summary>
    public static string ComputeHash(NodalSchemaSnapshot snapshot) =>
        Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(Serialize(snapshot))))
        .ToLowerInvariant();
}
