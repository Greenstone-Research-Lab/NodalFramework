using System.Text.RegularExpressions;
using Nodal.Core.Analytics;

namespace Nodal.TigerGraph;

/// <summary>Controls whether missing Nodal-managed analytics bindings may change TigerGraph state.</summary>
public enum TigerGraphAnalyticsProvisioningMode
{
    /// <summary>Validate configured bindings and fail before transport when one is missing.</summary>
    ValidateOnly,

    /// <summary>Install a missing Nodal-managed binding when a supported generator and administrative transport exist.</summary>
    InstallMissing,
}

/// <summary>Describes one verified installed TigerGraph analytics query.</summary>
/// <param name="Algorithm">The provider-neutral algorithm implemented by the binding.</param>
/// <param name="Fingerprint">The canonical Nodal analytics binding fingerprint.</param>
/// <param name="QueryName">The installed GSQL query name.</param>
/// <param name="ContractVersion">The canonical response-contract version.</param>
/// <param name="DefinitionChecksum">The optional complete generated-definition checksum.</param>
/// <param name="SupportsWeights">Whether this binding accepts relationship weight metadata.</param>
/// <param name="IsNodalManaged">Whether Nodal may replace the installed definition.</param>
public sealed record TigerGraphAnalyticsBinding(
    GraphAnalyticsAlgorithm Algorithm,
    string Fingerprint,
    string QueryName,
    string ContractVersion = "1",
    string? DefinitionChecksum = null,
    bool SupportsWeights = false,
    bool IsNodalManaged = false);

/// <summary>Contains verified scope-level installed-query bindings for one TigerGraph deployment.</summary>
public sealed class TigerGraphAnalyticsBindingManifest
{
    private readonly Dictionary<string, TigerGraphAnalyticsBinding> bindings;

    /// <summary>Initializes a manifest and rejects duplicate fingerprints.</summary>
    public TigerGraphAnalyticsBindingManifest(IEnumerable<TigerGraphAnalyticsBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        var values = new Dictionary<string, TigerGraphAnalyticsBinding>(StringComparer.Ordinal);
        foreach (var binding in bindings)
        {
            ArgumentNullException.ThrowIfNull(binding);
            ArgumentException.ThrowIfNullOrWhiteSpace(binding.Fingerprint);
            TigerGraphAnalyticsNaming.ValidateIdentifier(binding.QueryName, nameof(bindings));
            ArgumentException.ThrowIfNullOrWhiteSpace(binding.ContractVersion);
            if (!values.TryAdd(binding.Fingerprint, binding))
            {
                throw new ArgumentException($"Analytics binding fingerprint '{binding.Fingerprint}' is duplicated.", nameof(bindings));
            }
        }
        this.bindings = values;
    }

    /// <summary>Gets every verified binding keyed by canonical fingerprint.</summary>
    public IReadOnlyDictionary<string, TigerGraphAnalyticsBinding> Bindings => bindings;

    internal bool TryGet(string fingerprint, out TigerGraphAnalyticsBinding binding) =>
        bindings.TryGetValue(fingerprint, out binding!);
}

/// <summary>Applies deterministic installed-query naming for Nodal-managed analytics bindings.</summary>
public static partial class TigerGraphAnalyticsNaming
{
    /// <summary>Creates a deterministic GSQL-safe name from one binding key.</summary>
    public static string CreateQueryName(GraphAnalyticsBindingKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var node = Slug(key.NodeType, 20);
        var relation = Slug(key.Relationships[0].RelationshipType, 20);
        var algorithm = Slug(key.Algorithm.ToString(), 24);
        var version = Slug(key.ContractVersion, 8);
        return $"nodal_{node}_{relation}_{algorithm}_v{version}_{key.Fingerprint[..8]}";
    }

    internal static void ValidateIdentifier(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!IdentifierPattern().IsMatch(value))
        {
            throw new ArgumentException("TigerGraph identifiers may contain only letters, numbers, and underscores.", parameterName);
        }
    }

    private static string Slug(string value, int maximumLength)
    {
        var normalized = InvalidCharacters().Replace(value, "_").Trim('_').ToLowerInvariant();
        if (normalized.Length == 0)
        {
            normalized = "scope";
        }
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex("[^A-Za-z0-9_]+", RegexOptions.CultureInvariant)]
    private static partial Regex InvalidCharacters();
}
