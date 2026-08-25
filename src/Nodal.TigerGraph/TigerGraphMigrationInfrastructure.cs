using System.Text.Json;
using Nodal.Core.Migrations;

namespace Nodal.TigerGraph;

/// <summary>Bootstraps the provider-owned TigerGraph migration metadata schema exactly once.</summary>
internal sealed class TigerGraphMigrationInfrastructure
{
    public const string HistoryType = "__NodalMigration";
    public const string JournalType = "__NodalSchemaJob";

    private readonly HttpClient httpClient;
    private readonly TigerGraphOptions options;
    private readonly string graphName;
    private readonly ITigerGraphAdministrativeControlPlane controlPlane;
    private readonly object gate = new();
    private Task? initialization;

    public TigerGraphMigrationInfrastructure(
        HttpClient httpClient,
        TigerGraphOptions options,
        string graphName,
        ITigerGraphAdministrativeControlPlane controlPlane)
    {
        this.httpClient = httpClient;
        this.options = options;
        this.graphName = graphName;
        this.controlPlane = controlPlane;
    }

    public ValueTask EnsureAsync(CancellationToken cancellationToken)
    {
        Task task;
        lock (gate)
        {
            initialization ??= InitializeAsync();
            task = initialization;
        }

        return new ValueTask(task.WaitAsync(cancellationToken));
    }

    private async Task InitializeAsync()
    {
        try
        {
            var capabilities = await controlPlane
                .DiscoverCapabilitiesAsync(graphName, CancellationToken.None)
                .ConfigureAwait(false);
            capabilities.EnsureMigrationSupport();

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"gsql/v1/schema?graph={Uri.EscapeDataString(graphName)}");
            TigerGraphAuthentication.Apply(request, options);
            using var response = await httpClient.SendAsync(request, CancellationToken.None).ConfigureAwait(false);
            var payload = await response.Content.ReadAsStringAsync(CancellationToken.None).ConfigureAwait(false);
            TigerGraphAdministrativeResponse.EnsureSuccess(response, payload, "migration schema endpoint");

            using var document = JsonDocument.Parse(payload);
            var statements = new List<string>();
            if (!ContainsNamedType(document.RootElement, HistoryType))
            {
                statements.Add(
                    $"ADD VERTEX {HistoryType} (PRIMARY_ID Id STRING, Checksum STRING, " +
                    "AppliedAt DATETIME, State STRING, StartedAt DATETIME, CompletedAt DATETIME, " +
                    "FailureMessage STRING, FailureType STRING, FailureAt DATETIME) " +
                    "WITH primary_id_as_attribute=\"true\";");
            }

            if (!ContainsNamedType(document.RootElement, JournalType))
            {
                statements.Add(
                    $"ADD VERTEX {JournalType} (PRIMARY_ID Id STRING, MigrationId STRING, Checksum STRING, " +
                    "JobName STRING, Direction STRING, Phase STRING, CleanupCompleted BOOL, StartedAt DATETIME, UpdatedAt DATETIME, " +
                    "FailureMessage STRING, FailureType STRING) " +
                    "WITH primary_id_as_attribute=\"true\";");
            }

            if (statements.Count > 0)
            {
                await RunBootstrapAsync(statements, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch
        {
            lock (gate)
            {
                initialization = null;
            }

            throw;
        }
    }

    private async ValueTask RunBootstrapAsync(
        IReadOnlyList<string> statements,
        CancellationToken cancellationToken)
    {
        const string jobName = "nodal_migration_bootstrap";
        if (await controlPlane.SchemaJobExistsAsync(graphName, jobName, cancellationToken).ConfigureAwait(false))
        {
            await controlPlane.ExecuteAsync(
                new MigrationCommand($"DROP JOB {jobName}", false),
                cancellationToken).ConfigureAwait(false);
        }

        var created = false;
        try
        {
            await controlPlane.ExecuteAsync(
                new MigrationCommand(
                    $"CREATE SCHEMA_CHANGE JOB {jobName} FOR GRAPH {graphName} {{ {string.Join(' ', statements)} }}",
                    false),
                cancellationToken).ConfigureAwait(false);
            created = true;
            await controlPlane.ExecuteAsync(
                new MigrationCommand($"RUN SCHEMA_CHANGE JOB {jobName}", false),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (created)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await controlPlane.ExecuteAsync(
                    new MigrationCommand($"DROP JOB {jobName}", false),
                    cleanup.Token).ConfigureAwait(false);
            }
        }
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
}
