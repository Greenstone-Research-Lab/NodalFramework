using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Globalization;
using Nodal.Core.Migrations;

namespace Nodal.TigerGraph;

/// <summary>
/// Persists stateful migration history in TigerGraph through REST++.
/// </summary>
public sealed class TigerGraphMigrationHistoryStore :
    IGraphMigrationHistoryStore
{
    private readonly HttpClient httpClient;
    private readonly TigerGraphOptions options;
    private readonly string graphName;
    private readonly TigerGraphMigrationInfrastructure infrastructure;

    /// <summary>
    /// Initializes a TigerGraph migration history store.
    /// </summary>
    public TigerGraphMigrationHistoryStore(
        HttpClient httpClient,
        TigerGraphOptions options,
        string graphName,
        ITigerGraphAdministrativeControlPlane administrativeTransport)
        : this(
            httpClient,
            options,
            graphName,
            administrativeTransport,
            new TigerGraphMigrationInfrastructure(
                httpClient,
                options,
                graphName,
                administrativeTransport))
    {
    }

    internal TigerGraphMigrationHistoryStore(
        HttpClient httpClient,
        TigerGraphOptions options,
        string graphName,
        ITigerGraphAdministrativeControlPlane administrativeTransport,
        TigerGraphMigrationInfrastructure infrastructure)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(graphName);
        ArgumentNullException.ThrowIfNull(administrativeTransport);

        httpClient.BaseAddress ??= options.Endpoint;

        this.httpClient = httpClient;
        this.options = options;
        this.graphName = graphName;
        this.infrastructure = infrastructure;
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
            $"/vertices/{TigerGraphMigrationInfrastructure.HistoryType}");

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

        var attributes = new JsonObject
        {
            ["Checksum"] = Value(entry.Checksum),
            ["State"] = Value(entry.State.ToString()),
            ["StartedAt"] = Value(Format(entry.StartedAt)),
            ["FailureMessage"] = Value(entry.Failure?.Message),
            ["FailureType"] = Value(entry.Failure?.ErrorType)
        };
        if (entry.CompletedAt.HasValue)
        {
            attributes["CompletedAt"] = Value(Format(entry.CompletedAt));
        }

        if (entry.Failure is not null)
        {
            attributes["FailureAt"] = Value(Format(entry.Failure.OccurredAt));
        }

        var root = new JsonObject
        {
            ["vertices"] = new JsonObject
            {
                [TigerGraphMigrationInfrastructure.HistoryType] = new JsonObject
                {
                    [entry.Id] = attributes
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
            $"/vertices/{TigerGraphMigrationInfrastructure.HistoryType}/" +
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
        await infrastructure.EnsureAsync(cancellationToken).ConfigureAwait(false);
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

        TigerGraphAdministrativeResponse.EnsureSuccess(response, payload, "migration history endpoint");

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
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
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

}
