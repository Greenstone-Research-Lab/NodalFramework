using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nodal.Core.Migrations;

namespace Nodal.TigerGraph;

/// <summary>Executes recoverable TigerGraph schema jobs and journals every irreversible boundary.</summary>
public sealed class TigerGraphMigrationExecutor : IGraphMigrationExecutor
{
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(30);
    private readonly HttpClient httpClient;
    private readonly TigerGraphOptions options;
    private readonly string graphName;
    private readonly ITigerGraphAdministrativeControlPlane controlPlane;
    private readonly TigerGraphMigrationInfrastructure infrastructure;

    /// <summary>Initializes the executor for one TigerGraph graph.</summary>
    public TigerGraphMigrationExecutor(
        HttpClient httpClient,
        TigerGraphOptions options,
        string graphName,
        ITigerGraphAdministrativeControlPlane controlPlane)
        : this(httpClient, options, graphName, controlPlane,
            new TigerGraphMigrationInfrastructure(httpClient, options, graphName, controlPlane))
    {
    }

    internal TigerGraphMigrationExecutor(
        HttpClient httpClient,
        TigerGraphOptions options,
        string graphName,
        ITigerGraphAdministrativeControlPlane controlPlane,
        TigerGraphMigrationInfrastructure infrastructure)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(graphName);
        ArgumentNullException.ThrowIfNull(controlPlane);
        ArgumentNullException.ThrowIfNull(infrastructure);
        httpClient.BaseAddress ??= options.Endpoint;
        this.httpClient = httpClient;
        this.options = options;
        this.graphName = graphName;
        this.controlPlane = controlPlane;
        this.infrastructure = infrastructure;
        Journal = new TigerGraphSchemaJobJournalStore(httpClient, options, graphName, infrastructure);
    }

    /// <summary>Gets the durable journal used for inspection and recovery.</summary>
    public TigerGraphSchemaJobJournalStore Journal { get; }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyDictionary<string, string>> GetAppliedMigrationsAsync(
        CancellationToken cancellationToken = default)
    {
        await infrastructure.EnsureAsync(cancellationToken).ConfigureAwait(false);
        using var request = CreateRequest(HttpMethod.Get,
            $"restpp/graph/{Uri.EscapeDataString(graphName)}/vertices/" +
            TigerGraphMigrationInfrastructure.HistoryType);
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await ReadSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(payload);
        var history = new Dictionary<string, string>(StringComparer.Ordinal);
        CollectAppliedHistory(document.RootElement, history);
        return history;
    }

    /// <inheritdoc />
    public ValueTask ApplyAsync(MigrationExecution execution, CancellationToken cancellationToken = default) =>
        ExecuteAsync(execution, TigerGraphSchemaJobDirection.Up, cancellationToken);

    /// <inheritdoc />
    public ValueTask RevertAsync(MigrationExecution execution, CancellationToken cancellationToken = default) =>
        ExecuteAsync(execution, TigerGraphSchemaJobDirection.Down, cancellationToken);

    private async ValueTask ExecuteAsync(
        MigrationExecution execution,
        TigerGraphSchemaJobDirection direction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);
        await infrastructure.EnsureAsync(cancellationToken).ConfigureAwait(false);
        var envelope = SchemaJobEnvelope.Parse(execution.Commands);
        var existing = await Journal.GetAsync(execution.Id, cancellationToken).ConfigureAwait(false);
        if (IsOppositeTerminal(existing, direction))
        {
            if (!string.Equals(existing!.Checksum, execution.Checksum, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"TigerGraph migration journal checksum drift was detected for '{execution.Id}'.");
            }

            existing = null;
        }

        ValidateJournal(existing, execution, direction, envelope.JobName);
        if (IsComplete(existing, direction)) return;

        if (existing?.Phase is TigerGraphSchemaJobPhase.SchemaOutcomeUnknown or
            TigerGraphSchemaJobPhase.SchemaApplying)
        {
            throw new TigerGraphMigrationRecoveryRequiredException(execution.Id, envelope.JobName);
        }

        if (existing?.Phase is TigerGraphSchemaJobPhase.SchemaAppliedHistoryPending or
            TigerGraphSchemaJobPhase.CleanupPending)
        {
            await CompleteAfterSchemaAsync(execution, direction, existing, envelope, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var journal = NewEntry(execution, direction, envelope.JobName,
            existing?.StartedAt ?? DateTimeOffset.UtcNow);
        await Journal.SaveAsync(journal, cancellationToken).ConfigureAwait(false);
        if (await controlPlane.SchemaJobExistsAsync(graphName, envelope.JobName, cancellationToken)
            .ConfigureAwait(false))
        {
            await CleanupAsync(envelope.Drop).ConfigureAwait(false);
        }

        try
        {
            await controlPlane.ExecuteAsync(envelope.Create, cancellationToken).ConfigureAwait(false);
            journal = await SavePhaseAsync(journal, TigerGraphSchemaJobPhase.JobCreated, cancellationToken)
                .ConfigureAwait(false);
            journal = await SavePhaseAsync(journal, TigerGraphSchemaJobPhase.SchemaApplying, cancellationToken)
                .ConfigureAwait(false);
            await controlPlane.ExecuteAsync(envelope.Run, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            var cleanupCompleted = false;
            try
            {
                if (await controlPlane.SchemaJobExistsAsync(
                    graphName, envelope.JobName, CancellationToken.None).ConfigureAwait(false))
                {
                    await CleanupAsync(envelope.Drop).ConfigureAwait(false);
                }

                cleanupCompleted = true;
            }
            catch (Exception cleanupFailure)
            {
                exception.Data["TigerGraphCleanupFailure"] = cleanupFailure.Message;
            }

            await SaveFailureAsync(
                journal with { CleanupCompleted = cleanupCompleted },
                TigerGraphSchemaJobPhase.SchemaOutcomeUnknown,
                exception).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            await HandleKnownFailureAsync(journal, envelope.Drop, exception).ConfigureAwait(false);
            throw;
        }

        journal = await SavePhaseAsync(
            journal, TigerGraphSchemaJobPhase.SchemaAppliedHistoryPending, CancellationToken.None)
            .ConfigureAwait(false);
        await CompleteAfterSchemaAsync(execution, direction, journal, envelope, cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask CompleteAfterSchemaAsync(
        MigrationExecution execution,
        TigerGraphSchemaJobDirection direction,
        TigerGraphSchemaJobJournalEntry journal,
        SchemaJobEnvelope envelope,
        CancellationToken cancellationToken)
    {
        if (!journal.CleanupCompleted)
        {
            try
            {
                if (await controlPlane.SchemaJobExistsAsync(graphName, envelope.JobName, cancellationToken)
                    .ConfigureAwait(false))
                {
                    await CleanupAsync(envelope.Drop).ConfigureAwait(false);
                }

                journal = journal with
                {
                    Phase = TigerGraphSchemaJobPhase.SchemaAppliedHistoryPending,
                    CleanupCompleted = true,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    FailureMessage = null,
                    FailureType = null,
                };
                await Journal.SaveAsync(journal, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                await SaveFailureAsync(journal, TigerGraphSchemaJobPhase.CleanupPending, exception)
                    .ConfigureAwait(false);
                throw;
            }
        }

        if (direction is TigerGraphSchemaJobDirection.Up)
            await SaveAppliedHistoryAsync(execution, cancellationToken).ConfigureAwait(false);
        else
            await RemoveHistoryAsync(execution.Id, cancellationToken).ConfigureAwait(false);

        await Journal.SaveAsync(journal with
        {
            Phase = direction is TigerGraphSchemaJobDirection.Up
                ? TigerGraphSchemaJobPhase.Applied
                : TigerGraphSchemaJobPhase.Reverted,
            CleanupCompleted = true,
            UpdatedAt = DateTimeOffset.UtcNow,
            FailureMessage = null,
            FailureType = null,
        }, CancellationToken.None).ConfigureAwait(false);
    }

    private async ValueTask HandleKnownFailureAsync(
        TigerGraphSchemaJobJournalEntry journal,
        MigrationCommand drop,
        Exception primaryFailure)
    {
        Exception? cleanupFailure = null;
        try
        {
            await CleanupAsync(drop).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            cleanupFailure = exception;
        }

        await SaveFailureAsync(
            journal with { CleanupCompleted = cleanupFailure is null },
            TigerGraphSchemaJobPhase.Failed,
            primaryFailure).ConfigureAwait(false);
        if (cleanupFailure is not null) throw new AggregateException(primaryFailure, cleanupFailure);
    }

    private async ValueTask CleanupAsync(MigrationCommand drop)
    {
        using var cleanup = new CancellationTokenSource(CleanupTimeout);
        await controlPlane.ExecuteAsync(drop, cleanup.Token).ConfigureAwait(false);
    }

    private async ValueTask<TigerGraphSchemaJobJournalEntry> SavePhaseAsync(
        TigerGraphSchemaJobJournalEntry journal,
        TigerGraphSchemaJobPhase phase,
        CancellationToken cancellationToken)
    {
        var updated = journal with { Phase = phase, UpdatedAt = DateTimeOffset.UtcNow };
        await Journal.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    private async ValueTask SaveFailureAsync(
        TigerGraphSchemaJobJournalEntry journal,
        TigerGraphSchemaJobPhase phase,
        Exception exception)
    {
        await Journal.SaveAsync(journal with
        {
            Phase = phase,
            UpdatedAt = DateTimeOffset.UtcNow,
            FailureMessage = Truncate(exception.Message, 1024),
            FailureType = exception.GetType().FullName,
        }, CancellationToken.None).ConfigureAwait(false);
    }

    private async ValueTask SaveAppliedHistoryAsync(
        MigrationExecution execution,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.ToUniversalTime().ToString("O");
        var attributes = new JsonObject
        {
            ["Checksum"] = Value(execution.Checksum),
            ["AppliedAt"] = Value(now),
            ["State"] = Value(MigrationExecutionState.Applied.ToString()),
            ["StartedAt"] = Value(now),
            ["CompletedAt"] = Value(now),
            ["FailureMessage"] = Value(string.Empty),
            ["FailureType"] = Value(string.Empty),
        };
        var root = new JsonObject
        {
            ["vertices"] = new JsonObject
            {
                [TigerGraphMigrationInfrastructure.HistoryType] = new JsonObject
                {
                    [execution.Id] = attributes,
                },
            },
        };
        using var request = CreateRequest(HttpMethod.Post,
            $"restpp/graph/{Uri.EscapeDataString(graphName)}?vertex_must_exist=false");
        request.Headers.TryAddWithoutValidation("gsql-atomic-level", "atomic");
        request.Content = new StringContent(root.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        _ = await ReadSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask RemoveHistoryAsync(string migrationId, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Delete,
            $"restpp/graph/{Uri.EscapeDataString(graphName)}/vertices/" +
            $"{TigerGraphMigrationInfrastructure.HistoryType}/{Uri.EscapeDataString(migrationId)}");
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
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        TigerGraphAdministrativeResponse.EnsureSuccess(response, payload, "migration endpoint");
        return payload;
    }

    private static void ValidateJournal(
        TigerGraphSchemaJobJournalEntry? journal,
        MigrationExecution execution,
        TigerGraphSchemaJobDirection direction,
        string jobName)
    {
        if (journal is null) return;
        if (!string.Equals(journal.Checksum, execution.Checksum, StringComparison.Ordinal) ||
            journal.Direction != direction ||
            !string.Equals(journal.JobName, jobName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"TigerGraph migration journal drift was detected for '{execution.Id}'.");
        }
    }

    private static bool IsComplete(
        TigerGraphSchemaJobJournalEntry? journal, TigerGraphSchemaJobDirection direction) =>
        journal?.Phase == (direction is TigerGraphSchemaJobDirection.Up
            ? TigerGraphSchemaJobPhase.Applied : TigerGraphSchemaJobPhase.Reverted);

    private static bool IsOppositeTerminal(
        TigerGraphSchemaJobJournalEntry? journal,
        TigerGraphSchemaJobDirection direction) =>
        (direction is TigerGraphSchemaJobDirection.Down &&
            journal?.Phase is TigerGraphSchemaJobPhase.Applied) ||
        (direction is TigerGraphSchemaJobDirection.Up &&
            journal?.Phase is TigerGraphSchemaJobPhase.Reverted);

    private static TigerGraphSchemaJobJournalEntry NewEntry(
        MigrationExecution execution,
        TigerGraphSchemaJobDirection direction,
        string jobName,
        DateTimeOffset startedAt) =>
        new(execution.Id, execution.Checksum, jobName, direction,
            TigerGraphSchemaJobPhase.CreatingJob, false, startedAt, DateTimeOffset.UtcNow);

    private static void CollectAppliedHistory(JsonElement element, IDictionary<string, string> history)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("v_id", out var id) &&
            element.TryGetProperty("attributes", out var attributes) &&
            attributes.TryGetProperty("Checksum", out var checksum))
        {
            if (!attributes.TryGetProperty("State", out var state) ||
                string.IsNullOrWhiteSpace(state.GetString()) ||
                string.Equals(state.GetString(), "Applied", StringComparison.OrdinalIgnoreCase))
                history[id.GetString() ?? string.Empty] = checksum.GetString() ?? string.Empty;
            return;
        }

        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject()) CollectAppliedHistory(property.Value, history);
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) CollectAppliedHistory(item, history);
    }

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];

    private static JsonObject Value(string value) => new() { ["value"] = value };

    private sealed record SchemaJobEnvelope(
        string JobName, MigrationCommand Create, MigrationCommand Run, MigrationCommand Drop)
    {
        public static SchemaJobEnvelope Parse(IReadOnlyList<MigrationCommand> commands)
        {
            if (commands.Count != 3 ||
                !commands[0].Text.StartsWith("CREATE SCHEMA_CHANGE JOB ", StringComparison.OrdinalIgnoreCase) ||
                !commands[1].Text.StartsWith("RUN SCHEMA_CHANGE JOB ", StringComparison.OrdinalIgnoreCase) ||
                !commands[2].Text.StartsWith("DROP JOB ", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "TigerGraph migrations must compile to one create, run, and drop schema-job envelope.");

            var createTokens = commands[0].Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var jobName = createTokens[3];
            var runName = commands[1].Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            var dropName = commands[2].Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            if (!string.Equals(jobName, runName, StringComparison.Ordinal) ||
                !string.Equals(jobName, dropName, StringComparison.Ordinal))
                throw new InvalidOperationException("TigerGraph schema-job envelope names do not match.");
            return new SchemaJobEnvelope(jobName, commands[0], commands[1], commands[2]);
        }
    }
}

/// <summary>Indicates that a cancelled schema operation has an unknown outcome requiring reconciliation.</summary>
public sealed class TigerGraphMigrationRecoveryRequiredException : InvalidOperationException
{
    /// <summary>Initializes a recovery-required exception.</summary>
    public TigerGraphMigrationRecoveryRequiredException(string migrationId, string jobName)
        : base($"TigerGraph migration '{migrationId}' has an unknown schema outcome for job '{jobName}'. " +
            "Inspect the graph and use TigerGraphMigrationRecovery before attempting replay.")
    {
        MigrationId = migrationId;
        JobName = jobName;
    }

    /// <summary>Gets the migration requiring reconciliation.</summary>
    public string MigrationId { get; }

    /// <summary>Gets the deterministic schema-job name.</summary>
    public string JobName { get; }
}
