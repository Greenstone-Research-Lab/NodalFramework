using System.Text.Json;

namespace Nodal.Samples.MigrationHost;

internal sealed class MigrationHostSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public required Uri Endpoint { get; init; }

    public required string ProviderVersion { get; init; }

    public required string[] Capabilities { get; init; }

    public string? Username { get; init; }

    public string? Password { get; init; }

    public string? AccessToken { get; init; }

    public string? Database { get; init; }

    public string? GraphName { get; init; }

    public string? GsqlFile { get; init; }

    public string[] GsqlPrefixArguments { get; init; } = [];

    public static MigrationHostSettings Load()
    {
        var json = Environment.GetEnvironmentVariable("NODAL_MIGRATION_HOST_CONFIGURATION");
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException(
                "The migration host configuration secret is not available.");
        }

        var settings = JsonSerializer.Deserialize<MigrationHostSettings>(json, JsonOptions)
            ?? throw new InvalidOperationException("The migration host configuration is empty.");
        ArgumentNullException.ThrowIfNull(settings.Endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.ProviderVersion);
        ArgumentNullException.ThrowIfNull(settings.Capabilities);
        if (settings.Capabilities.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException("Migration host capabilities contain an empty value.");
        }

        return settings;
    }
}
