using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nodal.TigerGraph;

/// <summary>Describes the durable execution phase of one TigerGraph schema-change job.</summary>
public enum TigerGraphSchemaJobPhase
{
    /// <summary>The deterministic job is about to be created.</summary>
    CreatingJob,
    /// <summary>The job definition exists in TigerGraph.</summary>
    JobCreated,
    /// <summary>The schema-change job is being run.</summary>
    SchemaApplying,
    /// <summary>The schema succeeded and migration history must still be confirmed.</summary>
    SchemaAppliedHistoryPending,
    /// <summary>The schema outcome is unknown and requires reconciliation before replay.</summary>
    SchemaOutcomeUnknown,
    /// <summary>The temporary job still requires cleanup.</summary>
    CleanupPending,
    /// <summary>Schema, history, and cleanup completed.</summary>
    Applied,
    /// <summary>The down migration, history removal, and cleanup completed.</summary>
    Reverted,
    /// <summary>The schema job failed with a known unsuccessful outcome.</summary>
    Failed,
}

/// <summary>Identifies whether a TigerGraph schema job applies or reverts a migration.</summary>
public enum TigerGraphSchemaJobDirection
{
    /// <summary>The job applies the migration.</summary>
    Up,
    /// <summary>The job reverts the migration.</summary>
    Down,
}

/// <summary>Represents one durable TigerGraph schema-job journal entry.</summary>
public sealed record TigerGraphSchemaJobJournalEntry(
    string MigrationId,
    string Checksum,
    string JobName,
    TigerGraphSchemaJobDirection Direction,
    TigerGraphSchemaJobPhase Phase,
    bool CleanupCompleted,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    string? FailureMessage = null,
    string? FailureType = null);

/// <summary>Persists TigerGraph schema-job progress independently from migration history.</summary>
public sealed class TigerGraphSchemaJobJournalStore
{
    private readonly HttpClient httpClient;
    private readonly TigerGraphOptions options;
    private readonly string graphName;
    private readonly TigerGraphMigrationInfrastructure infrastructure;

    internal TigerGraphSchemaJobJournalStore(
        HttpClient httpClient,
        TigerGraphOptions options,
        string graphName,
        TigerGraphMigrationInfrastructure infrastructure)
    {
        this.httpClient = httpClient;
        this.options = options;
        this.graphName = graphName;
        this.infrastructure = infrastructure;
    }

    /// <summary>Reads one migration's schema-job journal entry.</summary>
    public async ValueTask<TigerGraphSchemaJobJournalEntry?> GetAsync(
        string migrationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(migrationId);
        await infrastructure.EnsureAsync(cancellationToken).ConfigureAwait(false);
        using var request = CreateRequest(
            HttpMethod.Get,
            $"restpp/graph/{Uri.EscapeDataString(graphName)}/vertices/" +
            $"{TigerGraphMigrationInfrastructure.JournalType}/{Uri.EscapeDataString(migrationId)}");
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (TigerGraphAdministrativeResponse.IsMissingVertex(payload))
        {
            return null;
        }

        TigerGraphAdministrativeResponse.EnsureSuccess(response, payload, "schema-job journal endpoint");
        using var document = JsonDocument.Parse(payload);
        return Find(document.RootElement);
    }

    /// <summary>Creates or replaces one durable schema-job journal entry.</summary>
    public async ValueTask SaveAsync(
        TigerGraphSchemaJobJournalEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await infrastructure.EnsureAsync(cancellationToken).ConfigureAwait(false);
        var attributes = new JsonObject
        {
            ["MigrationId"] = Value(entry.MigrationId),
            ["Checksum"] = Value(entry.Checksum),
            ["JobName"] = Value(entry.JobName),
            ["Direction"] = Value(entry.Direction.ToString()),
            ["Phase"] = Value(entry.Phase.ToString()),
            ["CleanupCompleted"] = new JsonObject { ["value"] = entry.CleanupCompleted },
            ["StartedAt"] = Value(Format(entry.StartedAt)),
            ["UpdatedAt"] = Value(Format(entry.UpdatedAt)),
            ["FailureMessage"] = Value(entry.FailureMessage),
            ["FailureType"] = Value(entry.FailureType),
        };
        var root = new JsonObject
        {
            ["vertices"] = new JsonObject
            {
                [TigerGraphMigrationInfrastructure.JournalType] = new JsonObject
                {
                    [entry.MigrationId] = attributes,
                },
            },
        };
        using var request = CreateRequest(
            HttpMethod.Post,
            $"restpp/graph/{Uri.EscapeDataString(graphName)}?vertex_must_exist=false");
        request.Headers.TryAddWithoutValidation("gsql-atomic-level", "atomic");
        request.Content = new StringContent(root.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        _ = await ReadSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string uri)
    {
        var request = new HttpRequestMessage(method, uri);
        TigerGraphAuthentication.Apply(request, options);
        return request;
    }

    private static async ValueTask<string> ReadSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        TigerGraphAdministrativeResponse.EnsureSuccess(response, payload, "schema-job journal endpoint");

        return payload;
    }

    private static TigerGraphSchemaJobJournalEntry? Find(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("v_id", out var id) &&
            element.TryGetProperty("attributes", out var attributes))
        {
            return new TigerGraphSchemaJobJournalEntry(
                id.GetString() ?? string.Empty,
                String(attributes, "Checksum"),
                String(attributes, "JobName"),
                Enum.TryParse<TigerGraphSchemaJobDirection>(String(attributes, "Direction"), true, out var direction)
                    ? direction
                    : TigerGraphSchemaJobDirection.Up,
                Enum.TryParse<TigerGraphSchemaJobPhase>(String(attributes, "Phase"), true, out var phase)
                    ? phase
                    : TigerGraphSchemaJobPhase.SchemaOutcomeUnknown,
                Boolean(attributes, "CleanupCompleted"),
                Timestamp(attributes, "StartedAt"),
                Timestamp(attributes, "UpdatedAt"),
                NullIfEmpty(String(attributes, "FailureMessage")),
                NullIfEmpty(String(attributes, "FailureType")));
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var found = Find(property.Value);
                if (found is not null) return found;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var found = Find(item);
                if (found is not null) return found;
            }
        }

        return null;
    }

    private static string String(JsonElement attributes, string name) =>
        attributes.TryGetProperty(name, out var value) ? value.GetString() ?? string.Empty : string.Empty;

    private static bool Boolean(JsonElement attributes, string name) =>
        attributes.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True;

    private static DateTimeOffset Timestamp(JsonElement attributes, string name) =>
        DateTimeOffset.TryParse(String(attributes, name), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value)
            ? value
            : DateTimeOffset.UnixEpoch;

    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static JsonObject Value(string? value) => new() { ["value"] = value ?? string.Empty };
}
