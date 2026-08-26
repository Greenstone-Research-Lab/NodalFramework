using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Nodal.Migrations;

/// <summary>Creates, serializes, and verifies deterministic migration bundles.</summary>
public static class NodalMigrationBundleSerializer
{
    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static readonly JsonSerializerOptions DocumentOptions = new(CanonicalOptions)
    {
        WriteIndented = true,
    };

    /// <summary>Creates an immutable bundle and computes its canonical checksum.</summary>
    public static NodalMigrationBundle Create(NodalMigrationBundleManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var normalized = manifest.Normalize();
        return new NodalMigrationBundle(
            NodalMigrationBundle.CurrentFormatVersion,
            normalized.MigrationId,
            normalized.ProviderName,
            normalized.ProviderVersion,
            normalized.FrameworkVersion,
            normalized.Requirements,
            normalized.Commands,
            ComputeChecksum(normalized));
    }

    /// <summary>Loads a bundle manifest used as input to the bundle command.</summary>
    public static NodalMigrationBundleManifest DeserializeManifest(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return (JsonSerializer.Deserialize<NodalMigrationBundleManifest>(json, DocumentOptions)
            ?? throw new JsonException("The migration bundle manifest is empty."))
            .Normalize();
    }

    /// <summary>Serializes a verified bundle into stable, reviewable JSON.</summary>
    public static string Serialize(NodalMigrationBundle bundle)
    {
        var normalized = Verify(bundle);
        return JsonSerializer.Serialize(normalized, DocumentOptions);
    }

    /// <summary>Deserializes a bundle and rejects unsupported formats or checksum drift.</summary>
    public static NodalMigrationBundle Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var bundle = JsonSerializer.Deserialize<NodalMigrationBundle>(json, DocumentOptions)
            ?? throw new JsonException("The migration bundle is empty.");
        return Verify(bundle);
    }

    /// <summary>Verifies format, content safety, and canonical checksum without serialization.</summary>
    public static NodalMigrationBundle Verify(NodalMigrationBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        if (bundle.FormatVersion != NodalMigrationBundle.CurrentFormatVersion)
        {
            throw new NotSupportedException(
                $"Migration bundle format version '{bundle.FormatVersion}' is not supported. " +
                $"This package supports version '{NodalMigrationBundle.CurrentFormatVersion}'.");
        }

        var manifest = bundle.ToManifest().Normalize();
        var expected = ComputeChecksum(manifest);
        if (!string.Equals(bundle.Checksum, expected, StringComparison.Ordinal))
        {
            throw new NodalMigrationBundleChecksumException(manifest.MigrationId);
        }

        return bundle with
        {
            MigrationId = manifest.MigrationId,
            ProviderName = manifest.ProviderName,
            ProviderVersion = manifest.ProviderVersion,
            FrameworkVersion = manifest.FrameworkVersion,
            Requirements = manifest.Requirements,
            Commands = manifest.Commands,
            Checksum = expected,
        };
    }

    private static string ComputeChecksum(NodalMigrationBundleManifest manifest)
    {
        var canonical = JsonSerializer.Serialize(manifest, CanonicalOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }
}

internal static class NodalMigrationBundleSecretGuard
{
    private static readonly Regex CredentialPattern = new(
        @"(?:password|passwd|access[_-]?token|client[_-]?secret|authorization)\s*[:=]\s*\S+|bearer\s+\S+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    public static void ThrowIfSensitive(string value)
    {
        if (CredentialPattern.IsMatch(value))
        {
            throw new NodalMigrationBundleSecretException();
        }
    }
}
