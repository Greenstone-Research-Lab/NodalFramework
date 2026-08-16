using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nodal.Core.Migrations;

namespace Nodal.TigerGraph;

/// <summary>
/// Executes TigerGraph schema jobs through an explicit administrative transport and stores
/// migration history through documented schema and REST++ endpoints.
/// </summary>
public sealed class TigerGraphMigrationExecutor : IGraphMigrationExecutor
{
    private const string HistoryType = "__NodalMigration";
    private readonly HttpClient httpClient;
    private readonly TigerGraphOptions options;
    private readonly string graphName;
    private readonly ITigerGraphAdministrativeTransport administrativeTransport;
    private bool infrastructureReady;

    /// <summary>Initializes the executor for one TigerGraph graph.</summary>
    public TigerGraphMigrationExecutor(
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
    public async ValueTask<IReadOnlyDictionary<string, string>> GetAppliedMigrationsAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureInfrastructureAsync(cancellationToken).ConfigureAwait(false);
        using var request = CreateRequest(
            HttpMethod.Get,
            $"restpp/graph/{Uri.EscapeDataString(graphName)}/vertices/{HistoryType}");
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await ReadSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(payload);
        var history = new Dictionary<string, string>(StringComparer.Ordinal);
        CollectHistory(document.RootElement, history);
        return history;
    }

    /// <inheritdoc />
    public async ValueTask ApplyAsync(
        MigrationExecution execution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);
        await EnsureInfrastructureAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteCommandsAsync(execution.Commands, cancellationToken).ConfigureAwait(false);

        var root = new JsonObject
        {
            ["vertices"] = new JsonObject
            {
                [HistoryType] = new JsonObject
                {
                    [execution.Id] = new JsonObject
                    {
                        ["Checksum"] = Value(execution.Checksum),
                        ["AppliedAt"] = Value(DateTimeOffset.UtcNow.UtcDateTime.ToString("O")),
                    },
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

    /// <inheritdoc />
    public async ValueTask RevertAsync(
        MigrationExecution execution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);
        await EnsureInfrastructureAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteCommandsAsync(execution.Commands, cancellationToken).ConfigureAwait(false);
        using var request = CreateRequest(
            HttpMethod.Delete,
            $"restpp/graph/{Uri.EscapeDataString(graphName)}/vertices/{HistoryType}/" +
            Uri.EscapeDataString(execution.Id));
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        _ = await ReadSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask EnsureInfrastructureAsync(CancellationToken cancellationToken)
    {
        if (infrastructureReady)
        {
            return;
        }

        using var request = CreateRequest(
            HttpMethod.Get,
            $"gsql/v1/schema?graph={Uri.EscapeDataString(graphName)}");
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await ReadSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(payload);
        if (!ContainsNamedType(document.RootElement, HistoryType))
        {
            MigrationCommand[] bootstrap =
            [
                new MigrationCommand(
                    $"CREATE SCHEMA_CHANGE JOB nodal_history_bootstrap FOR GRAPH {graphName} {{ " +
                    $"ADD VERTEX {HistoryType} (PRIMARY_ID Id STRING, Checksum STRING, AppliedAt DATETIME) " +
                    "WITH primary_id_as_attribute=\"true\"; }",
                    false),
                new MigrationCommand("RUN SCHEMA_CHANGE JOB nodal_history_bootstrap", false),
                new MigrationCommand("DROP JOB nodal_history_bootstrap", false),
            ];
            await ExecuteCommandsAsync(bootstrap, cancellationToken).ConfigureAwait(false);
        }

        infrastructureReady = true;
    }

    private async ValueTask ExecuteCommandsAsync(
        IReadOnlyList<MigrationCommand> commands,
        CancellationToken cancellationToken)
    {
        if (commands.Count == 0)
        {
            return;
        }

        if (commands.Count < 3)
        {
            foreach (var command in commands)
            {
                await administrativeTransport.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        Exception? failure = null;
        var created = false;
        try
        {
            await administrativeTransport.ExecuteAsync(commands[0], cancellationToken).ConfigureAwait(false);
            created = true;
            for (var index = 1; index < commands.Count - 1; index++)
            {
                await administrativeTransport.ExecuteAsync(commands[index], cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        if (created)
        {
            try
            {
                await administrativeTransport.ExecuteAsync(commands[^1], cancellationToken).ConfigureAwait(false);
            }
            catch (Exception cleanupException) when (failure is not null)
            {
                throw new AggregateException(failure, cleanupException);
            }
        }

        if (failure is not null)
        {
            throw failure;
        }
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
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"TigerGraph migration endpoint returned HTTP {(int)response.StatusCode}: {payload}",
                null,
                response.StatusCode);
        }

        return payload;
    }

    private static bool ContainsNamedType(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("Name", out var typeName) && typeName.GetString() == name)
            {
                return true;
            }

            return element.EnumerateObject().Any(property => ContainsNamedType(property.Value, name));
        }

        return element.ValueKind == JsonValueKind.Array &&
            element.EnumerateArray().Any(item => ContainsNamedType(item, name));
    }

    private static void CollectHistory(JsonElement element, IDictionary<string, string> history)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("v_id", out var id) &&
            element.TryGetProperty("attributes", out var attributes) &&
            attributes.TryGetProperty("Checksum", out var checksum))
        {
            history[id.GetString() ?? string.Empty] = checksum.GetString() ?? string.Empty;
            return;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                CollectHistory(property.Value, history);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                CollectHistory(item, history);
            }
        }
    }

    private static JsonObject Value(string value) => new() { ["value"] = value };
}
