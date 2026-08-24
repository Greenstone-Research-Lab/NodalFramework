using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nodal.Core.Migrations;

namespace Nodal.TigerGraph;

/// <summary>
/// Persists stateful migration history in TigerGraph through REST++.
/// </summary>
public sealed class TigerGraphMigrationHistoryStore :
    IGraphMigrationHistoryStore
{
    private const string HistoryType = "__NodalMigration";

    private readonly HttpClient httpClient;
    private readonly TigerGraphOptions options;
    private readonly string graphName;
    private readonly ITigerGraphAdministrativeTransport administrativeTransport;
    private bool infrastructureReady;

    /// <summary>
    /// Initializes a TigerGraph migration history store.
    /// </summary>
    public TigerGraphMigrationHistoryStore(
        HttpClient httpClient,
        TigerGraphOptions options,
        string graphName,
        ITigerGraphAdministrativeTransport administrativeTransport)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(graphName);
        ArgumentNullException.ThrowIfNull(administrativeTransport);

        httpClient.BaseAddress ??= options.Endpoint;

        this.httpClient = httpClient;
        this.options = options;
        this.graphName = graphName;
        this.administrativeTransport = administrativeTransport;
    }

    /// <inheritdoc />
    public async ValueTask<
        IReadOnlyDictionary<string, MigrationHistoryEntry>>
        GetMigrationHistoryAsync(
            CancellationToken cancellationToken = default)
    {
        await EnsureInfrastructureAsync(cancellationToken)
            .ConfigureAwait(false);

        using var request = CreateRequest(
            HttpMethod.Get,
            $"restpp/graph/{Uri.EscapeDataString(graphName)}" +
            $"/vertices/{HistoryType}");

        using var response = await httpClient
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        var payload = await ReadSuccessAsync(
            response,
            cancellationToken).ConfigureAwait(false);

        using var document = JsonDocument.Parse(payload);
        var entries = new Dictionary<string, MigrationHistoryEntry>(
            StringComparer.Ordinal);

        CollectHistory(document.RootElement, entries);

        return entries;
    }

    /// <inheritdoc />
    public async ValueTask SaveMigrationHistoryAsync(
        MigrationHistoryEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await EnsureInfrastructureAsync(cancellationToken)
            .ConfigureAwait(false);

        var root = new JsonObject
        {
            ["vertices"] = new JsonObject
            {
                [HistoryType] = new JsonObject
                {
                    [entry.Id] = new JsonObject
                    {
                        ["Checksum"] = Value(entry.Checksum),
                        ["State"] = Value(entry.State.ToString()),
                        ["StartedAt"] = Value(Format(entry.StartedAt)),
                        ["CompletedAt"] = Value(Format(entry.CompletedAt)),
                        ["FailureMessage"] = Value(
                            entry.Failure?.Message),
                        ["FailureType"] = Value(
                            entry.Failure?.ErrorType),
                        ["FailureAt"] = Value(
                            Format(entry.Failure?.OccurredAt))
                    }
                }
            }
        };

        using var request = CreateRequest(
            HttpMethod.Post,
            $"restpp/graph/{Uri.EscapeDataString(graphName)}" +
            "?vertex_must_exist=false");

        request.Headers.TryAddWithoutValidation(
            "gsql-atomic-level",
            "atomic");

        request.Content = new StringContent(
            root.ToJsonString(),
            Encoding.UTF8,
            "application/json");

        using var response = await httpClient
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        _ = await ReadSuccessAsync(
            response,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask RemoveMigrationHistoryAsync(
        string migrationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(migrationId);

        await EnsureInfrastructureAsync(cancellationToken)
            .ConfigureAwait(false);

        using var request = CreateRequest(
            HttpMethod.Delete,
            $"restpp/graph/{Uri.EscapeDataString(graphName)}" +
            $"/vertices/{HistoryType}/" +
            Uri.EscapeDataString(migrationId));

        using var response = await httpClient
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        _ = await ReadSuccessAsync(
            response,
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask EnsureInfrastructureAsync(
        CancellationToken cancellationToken)
    {
        if (infrastructureReady)
        {
            return;
        }

        using var request = CreateRequest(
            HttpMethod.Get,
            $"gsql/v1/schema?graph={Uri.EscapeDataString(graphName)}");

        using var response = await httpClient
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        var payload = await ReadSuccessAsync(
            response,
            cancellationToken).ConfigureAwait(false);

        using var document = JsonDocument.Parse(payload);

        if (!ContainsNamedType(document.RootElement, HistoryType))
        {
            MigrationCommand[] bootstrap =
            [
                new MigrationCommand(
                    $"CREATE SCHEMA_CHANGE JOB " +
                    $"nodal_history_bootstrap FOR GRAPH {graphName} {{ " +
                    $"ADD VERTEX {HistoryType} (" +
                    "PRIMARY_ID Id STRING, " +
                    "Checksum STRING, " +
                    "AppliedAt DATETIME, " +
                    "State STRING, " +
                    "StartedAt DATETIME, " +
                    "CompletedAt DATETIME, " +
                    "FailureMessage STRING, " +
                    "FailureType STRING, " +
                    "FailureAt DATETIME) " +
                    "WITH primary_id_as_attribute=\"true\"; }",
                    false),

                new MigrationCommand(
                    "RUN SCHEMA_CHANGE JOB nodal_history_bootstrap",
                    false),

                new MigrationCommand(
                    "DROP JOB nodal_history_bootstrap",
                    false)
            ];

            await ExecuteCommandsAsync(
                bootstrap,
                cancellationToken).ConfigureAwait(false);
        }

        infrastructureReady = true;
    }

    private async ValueTask ExecuteCommandsAsync(
        IReadOnlyList<MigrationCommand> commands,
        CancellationToken cancellationToken)
    {
        foreach (var command in commands)
        {
            await administrativeTransport
                .ExecuteAsync(command, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private HttpRequestMessage CreateRequest(
        HttpMethod method,
        string uri)
    {
        var request = new HttpRequestMessage(method, uri);
        TigerGraphAuthentication.Apply(request, options);
        return request;
    }

    private static async ValueTask<string> ReadSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var payload = await response.Content
            .ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"TigerGraph migration history endpoint returned HTTP " +
                $"{(int)response.StatusCode}: {payload}",
                null,
                response.StatusCode);
        }

        return payload;
    }

    private static void CollectHistory(
        JsonElement element,
        IDictionary<string, MigrationHistoryEntry> entries)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("v_id", out var idElement) &&
            element.TryGetProperty(
                "attributes",
                out var attributes))
        {
            var id = idElement.GetString() ?? string.Empty;

            if (attributes.TryGetProperty(
                    "Checksum",
                    out var checksumElement))
            {
                var checksum = checksumElement.GetString() ?? string.Empty;

                var state = ReadState(attributes);
                var startedAt = ReadTimestamp(attributes, "StartedAt");
                var completedAt = ReadTimestamp(attributes, "CompletedAt");

                var failureMessage = ReadString(
                    attributes,
                    "FailureMessage");

                var failureType = ReadString(
                    attributes,
                    "FailureType");

                var failureAt = ReadTimestamp(
                    attributes,
                    "FailureAt");

                MigrationExecutionFailure? failure = null;

                if (!string.IsNullOrWhiteSpace(failureMessage) &&
                    !string.IsNullOrWhiteSpace(failureType) &&
                    failureAt.HasValue)
                {
                    failure = new MigrationExecutionFailure(
                        failureMessage,
                        failureType,
                        failureAt.Value);
                }

                entries[id] = new MigrationHistoryEntry(
                    id,
                    checksum,
                    state,
                    startedAt,
                    completedAt,
                    failure);

                return;
            }
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                CollectHistory(property.Value, entries);
            }

            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                CollectHistory(item, entries);
            }
        }
    }

    private static MigrationExecutionState ReadState(
        JsonElement attributes)
    {
        var value = ReadString(attributes, "State");

        return Enum.TryParse<MigrationExecutionState>(
            value,
            ignoreCase: true,
            out var state)
            ? state
            : MigrationExecutionState.Applied;
    }

    private static string ReadString(
        JsonElement attributes,
        string name)
    {
        return attributes.TryGetProperty(name, out var value)
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static DateTimeOffset? ReadTimestamp(
        JsonElement attributes,
        string name)
    {
        var value = ReadString(attributes, name);

        return DateTimeOffset.TryParse(
            value,
            out var timestamp)
            ? timestamp
            : null;
    }

    private static string Format(DateTimeOffset? value) =>
        value?.ToUniversalTime().ToString("O")
        ?? string.Empty;

    private static JsonObject Value(string? value) =>
        new()
        {
            ["value"] = value ?? string.Empty
        };

    private static bool ContainsNamedType(
        JsonElement element,
        string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty(
                    "Name",
                    out var typeName) &&
                typeName.GetString() == name)
            {
                return true;
            }

            return element.EnumerateObject()
                .Any(property =>
                    ContainsNamedType(property.Value, name));
        }

        return element.ValueKind == JsonValueKind.Array &&
            element.EnumerateArray()
                .Any(item =>
                    ContainsNamedType(item, name));
    }
}
