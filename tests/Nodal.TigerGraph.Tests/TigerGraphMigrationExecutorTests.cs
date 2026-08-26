using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Nodal.Core.Migrations;

namespace Nodal.TigerGraph.Tests;

public sealed class TigerGraphMigrationExecutorTests
{
    [Fact]
    public void ProviderRequiresVerifiedControlPlaneForMigrations()
    {
        using var client = Client(out _);
        var plain = new TigerGraphProvider(client, Options(), "SocialGraph");
        var executeOnly = new TigerGraphProvider(client, Options(), "SocialGraph", new ExecuteOnlyTransport());
        var configured = new TigerGraphProvider(client, Options(), "SocialGraph", new RecordingControlPlane());

        Assert.False(plain.SupportsMigrationExecution);
        Assert.False(executeOnly.SupportsMigrationExecution);
        Assert.Throws<NotSupportedException>(() => plain.MigrationExecutor);
        Assert.Throws<NotSupportedException>(() => executeOnly.MigrationHistory);
        Assert.Throws<NotSupportedException>(() => plain.MigrationRecovery);
        Assert.True(configured.SupportsMigrationExecution);
        Assert.IsType<TigerGraphMigrationExecutor>(configured.MigrationExecutor);
        Assert.IsType<TigerGraphMigrationHistoryStore>(configured.MigrationHistory);
        Assert.NotNull(configured.MigrationRecovery);
        Assert.IsType<TigerGraphMigrationLock>(configured.MigrationLock);
        Assert.Equal("tigergraph:SocialGraph", configured.MigrationLockScope);
        Assert.Equal("tigergraph:SocialGraph", configured.MigrationHistoryScope);
    }

    [Fact]
    public async Task ApplyPersistsPhasesCleansJobAndWritesHistory()
    {
        using var client = Client(out var handler);
        var controlPlane = new RecordingControlPlane();
        var executor = new TigerGraphMigrationExecutor(client, Options(), "SocialGraph", controlPlane);

        await executor.ApplyAsync(Execution());

        Assert.Equal(TigerGraphSchemaJobPhase.Applied, handler.Journal("001_initial")!.Phase);
        Assert.True(handler.Journal("001_initial")!.CleanupCompleted);
        Assert.Equal("checksum", handler.HistoryChecksum("001_initial"));
        Assert.Collection(
            controlPlane.Commands,
            command => Assert.StartsWith("CREATE SCHEMA_CHANGE JOB nodal_job", command.Text, StringComparison.Ordinal),
            command => Assert.Equal("RUN SCHEMA_CHANGE JOB nodal_job", command.Text),
            command => Assert.Equal("DROP JOB nodal_job", command.Text));
        Assert.Contains(handler.Requests, request => request.AtomicLevel == "atomic");
    }

    [Fact]
    public async Task RevertRemovesHistoryAndEndsReverted()
    {
        using var client = Client(out var handler);
        handler.AddHistory("001_initial", "checksum", "Applied");
        var executor = new TigerGraphMigrationExecutor(
            client, Options(), "SocialGraph", new RecordingControlPlane());

        await executor.RevertAsync(Execution());

        Assert.Null(handler.HistoryChecksum("001_initial"));
        Assert.Equal(TigerGraphSchemaJobPhase.Reverted, handler.Journal("001_initial")!.Phase);
    }

    [Fact]
    public async Task AppliedMigrationCanTransitionToDownWithADifferentDeterministicJob()
    {
        using var client = Client(out var handler);
        var controlPlane = new RecordingControlPlane();
        var executor = new TigerGraphMigrationExecutor(client, Options(), "SocialGraph", controlPlane);
        await executor.ApplyAsync(Execution());
        var down = new MigrationExecution(
            "001_initial", "checksum",
            [
                new MigrationCommand("CREATE SCHEMA_CHANGE JOB nodal_down FOR GRAPH SocialGraph { DROP VERTEX Demo; }", true),
                new MigrationCommand("RUN SCHEMA_CHANGE JOB nodal_down", true),
                new MigrationCommand("DROP JOB nodal_down", false),
            ]);

        await executor.RevertAsync(down);

        Assert.Equal(TigerGraphSchemaJobPhase.Reverted, handler.Journal("001_initial")!.Phase);
        Assert.Equal("nodal_down", handler.Journal("001_initial")!.JobName);
        Assert.Null(handler.HistoryChecksum("001_initial"));
    }

    [Fact]
    public async Task KnownRunFailureCleansJobAndRecordsFailure()
    {
        using var client = Client(out var handler);
        var controlPlane = new RecordingControlPlane { RunFailure = new InvalidOperationException("run failed") };
        var executor = new TigerGraphMigrationExecutor(client, Options(), "SocialGraph", controlPlane);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await executor.ApplyAsync(Execution()));

        Assert.Equal("run failed", exception.Message);
        Assert.Equal(TigerGraphSchemaJobPhase.Failed, handler.Journal("001_initial")!.Phase);
        Assert.True(handler.Journal("001_initial")!.CleanupCompleted);
        Assert.Equal("System.InvalidOperationException", handler.Journal("001_initial")!.FailureType);
        Assert.EndsWith("DROP JOB nodal_job", controlPlane.Commands[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelledRunRequiresExplicitRecoveryAndNeverBlindlyReplays()
    {
        using var client = Client(out var handler);
        var controlPlane = new RecordingControlPlane { CancelRun = true };
        var executor = new TigerGraphMigrationExecutor(client, Options(), "SocialGraph", controlPlane);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await executor.ApplyAsync(Execution()));
        controlPlane.CancelRun = false;
        var commandCount = controlPlane.Commands.Count;

        var exception = await Assert.ThrowsAsync<TigerGraphMigrationRecoveryRequiredException>(
            async () => await executor.ApplyAsync(Execution()));

        Assert.Equal("001_initial", exception.MigrationId);
        Assert.Equal("nodal_job", exception.JobName);
        Assert.Equal(commandCount, controlPlane.Commands.Count);
        Assert.Equal(TigerGraphSchemaJobPhase.SchemaOutcomeUnknown, handler.Journal("001_initial")!.Phase);
    }

    [Fact]
    public async Task RecoveryCanConfirmAppliedAndResumeWithoutSchemaReplay()
    {
        using var client = Client(out var handler);
        handler.SaveJournal(Journal(TigerGraphSchemaJobPhase.SchemaOutcomeUnknown));
        var controlPlane = new RecordingControlPlane();
        var executor = new TigerGraphMigrationExecutor(client, Options(), "SocialGraph", controlPlane);
        var recovery = new TigerGraphMigrationRecovery(executor.Journal);

        Assert.Equal(TigerGraphSchemaJobPhase.SchemaOutcomeUnknown,
            (await recovery.InspectAsync("001_initial"))!.Phase);
        await recovery.ConfirmSchemaAppliedAsync("001_initial");
        await executor.ApplyAsync(Execution());

        Assert.Empty(controlPlane.Commands);
        Assert.Equal("checksum", handler.HistoryChecksum("001_initial"));
        Assert.Equal(TigerGraphSchemaJobPhase.Applied, handler.Journal("001_initial")!.Phase);
    }

    [Fact]
    public async Task RecoveryCanConfirmNotAppliedAndAllowSafeReplay()
    {
        using var client = Client(out var handler);
        handler.SaveJournal(Journal(TigerGraphSchemaJobPhase.SchemaOutcomeUnknown));
        var controlPlane = new RecordingControlPlane();
        var executor = new TigerGraphMigrationExecutor(client, Options(), "SocialGraph", controlPlane);
        var recovery = new TigerGraphMigrationRecovery(executor.Journal);

        await recovery.ConfirmSchemaNotAppliedAsync("001_initial");
        await executor.ApplyAsync(Execution());

        Assert.Equal(3, controlPlane.Commands.Count);
        Assert.Equal(TigerGraphSchemaJobPhase.Applied, handler.Journal("001_initial")!.Phase);
    }

    [Fact]
    public async Task RestartDuringSchemaApplyingRequiresRecovery()
    {
        using var client = Client(out var handler);
        handler.SaveJournal(Journal(TigerGraphSchemaJobPhase.SchemaApplying));
        var controlPlane = new RecordingControlPlane();
        var executor = new TigerGraphMigrationExecutor(client, Options(), "SocialGraph", controlPlane);

        await Assert.ThrowsAsync<TigerGraphMigrationRecoveryRequiredException>(
            async () => await executor.ApplyAsync(Execution()));

        Assert.Empty(controlPlane.Commands);
    }

    [Fact]
    public async Task FailedRunAndCleanupPreserveBothFailuresWithoutMarkingSchemaApplied()
    {
        using var client = Client(out var handler);
        var controlPlane = new RecordingControlPlane
        {
            RunFailure = new InvalidOperationException("run failed"),
            DropFailure = new IOException("drop failed"),
        };
        var executor = new TigerGraphMigrationExecutor(client, Options(), "SocialGraph", controlPlane);

        var exception = await Assert.ThrowsAsync<AggregateException>(
            async () => await executor.ApplyAsync(Execution()));

        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.Equal(TigerGraphSchemaJobPhase.Failed, handler.Journal("001_initial")!.Phase);
        Assert.False(handler.Journal("001_initial")!.CleanupCompleted);
        Assert.Null(handler.HistoryChecksum("001_initial"));
    }

    [Fact]
    public async Task HistoryFailureResumesWithoutReplayingSuccessfulSchema()
    {
        using var client = Client(out var handler);
        handler.FailNextHistoryWrite = true;
        var controlPlane = new RecordingControlPlane();
        var executor = new TigerGraphMigrationExecutor(client, Options(), "SocialGraph", controlPlane);

        await Assert.ThrowsAsync<HttpRequestException>(
            async () => await executor.ApplyAsync(Execution()));
        var commandsAfterFailure = controlPlane.Commands.Count;
        Assert.Equal(TigerGraphSchemaJobPhase.SchemaAppliedHistoryPending,
            handler.Journal("001_initial")!.Phase);

        await executor.ApplyAsync(Execution());

        Assert.Equal(commandsAfterFailure, controlPlane.Commands.Count);
        Assert.Equal(TigerGraphSchemaJobPhase.Applied, handler.Journal("001_initial")!.Phase);
        Assert.Equal("checksum", handler.HistoryChecksum("001_initial"));
    }

    [Fact]
    public async Task CleanupFailureAfterSchemaSuccessIsRetryableWithoutSchemaReplay()
    {
        using var client = Client(out var handler);
        var controlPlane = new RecordingControlPlane { DropFailure = new IOException("drop failed") };
        var executor = new TigerGraphMigrationExecutor(client, Options(), "SocialGraph", controlPlane);

        await Assert.ThrowsAsync<IOException>(async () => await executor.ApplyAsync(Execution()));
        Assert.Equal(TigerGraphSchemaJobPhase.CleanupPending, handler.Journal("001_initial")!.Phase);
        controlPlane.DropFailure = null;
        var schemaCommands = controlPlane.Commands.Count(command =>
            command.Text.StartsWith("RUN SCHEMA_CHANGE JOB", StringComparison.Ordinal));

        await executor.ApplyAsync(Execution());

        Assert.Equal(schemaCommands, controlPlane.Commands.Count(command =>
            command.Text.StartsWith("RUN SCHEMA_CHANGE JOB", StringComparison.Ordinal)));
        Assert.Equal(TigerGraphSchemaJobPhase.Applied, handler.Journal("001_initial")!.Phase);
    }

    [Fact]
    public async Task RecoveryRejectsMissingAndSettledEntries()
    {
        using var client = Client(out var handler);
        var executor = new TigerGraphMigrationExecutor(
            client, Options(), "SocialGraph", new RecordingControlPlane());
        var recovery = new TigerGraphMigrationRecovery(executor.Journal);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await recovery.ConfirmSchemaAppliedAsync("missing"));
        handler.SaveJournal(Journal(TigerGraphSchemaJobPhase.Applied));
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await recovery.ConfirmSchemaAppliedAsync("001_initial"));
    }

    [Fact]
    public async Task JournalDriftAndMalformedEnvelopeFailBeforeSchemaExecution()
    {
        using var client = Client(out var handler);
        handler.SaveJournal(Journal(TigerGraphSchemaJobPhase.Failed) with { Checksum = "old" });
        var controlPlane = new RecordingControlPlane();
        var executor = new TigerGraphMigrationExecutor(client, Options(), "SocialGraph", controlPlane);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await executor.ApplyAsync(Execution()));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await executor.ApplyAsync(
            new MigrationExecution("bad", "checksum", [new MigrationCommand("invalid", false)])));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await executor.ApplyAsync(
            new MigrationExecution("bad", "checksum",
            [
                new MigrationCommand("CREATE SCHEMA_CHANGE JOB first FOR GRAPH SocialGraph { ADD VERTEX Demo(PRIMARY_ID Id STRING); }", false),
                new MigrationCommand("RUN SCHEMA_CHANGE JOB second", false),
                new MigrationCommand("DROP JOB first", false),
            ])));
        Assert.Empty(controlPlane.Commands);
    }

    [Fact]
    public async Task OppositeTerminalTransitionStillRejectsChecksumDrift()
    {
        using var client = Client(out var handler);
        handler.SaveJournal(Journal(TigerGraphSchemaJobPhase.Applied) with { Checksum = "old" });
        var executor = new TigerGraphMigrationExecutor(
            client, Options(), "SocialGraph", new RecordingControlPlane());

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await executor.RevertAsync(Execution()));
    }

    [Fact]
    public async Task IncompleteControlPlaneFailsBeforeMetadataOrSchemaMutation()
    {
        using var client = Client(out var handler);
        var controlPlane = new RecordingControlPlane
        {
            Capabilities = new TigerGraphAdministrativeCapabilities(
                "4.2.4", true, true, true, false, TigerGraphMigrationLockScope.Process),
        };
        var executor = new TigerGraphMigrationExecutor(client, Options(), "SocialGraph", controlPlane);

        await Assert.ThrowsAsync<NodalCapabilityNotSupportedException>(
            async () => await executor.ApplyAsync(Execution()));

        Assert.Empty(controlPlane.Commands);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task TigerGraphErrorPayloadFailsEvenWhenHttpStatusIsSuccessful()
    {
        using var client = Client(out var handler);
        handler.ErrorNextWrite = true;
        var executor = new TigerGraphMigrationExecutor(
            client, Options(), "SocialGraph", new RecordingControlPlane());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await executor.ApplyAsync(Execution()));

        Assert.Contains("rejected", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NonJsonAdministrativeSuccessIsNotMisclassifiedAsMissingVertex()
    {
        using var client = Client(out var handler);
        handler.MissingJournalPayload = "proxy-success";
        var executor = new TigerGraphMigrationExecutor(
            client, Options(), "SocialGraph", new RecordingControlPlane());

        await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(
            async () => await executor.ApplyAsync(Execution()));
    }

    [Fact]
    public async Task AppliedHistoryExcludesNonAppliedStateButAcceptsLegacyRows()
    {
        using var client = Client(out var handler);
        handler.AddHistory("applied", "a", "Applied");
        handler.AddHistory("failed", "f", "Failed");
        handler.AddHistory("legacy", "l", null);
        var executor = new TigerGraphMigrationExecutor(
            client, Options(), "SocialGraph", new RecordingControlPlane());

        var history = await executor.GetAppliedMigrationsAsync();

        Assert.Equal(2, history.Count);
        Assert.Equal("a", history["applied"]);
        Assert.Equal("l", history["legacy"]);
        Assert.DoesNotContain("failed", history);
    }

    [Fact]
    public async Task InfrastructureBootstrapsBothMetadataTypesOnlyOnce()
    {
        using var client = Client(out var handler, includeInfrastructure: false);
        var controlPlane = new RecordingControlPlane();
        var executor = new TigerGraphMigrationExecutor(client, Options(), "SocialGraph", controlPlane);

        _ = await executor.GetAppliedMigrationsAsync();
        _ = await executor.GetAppliedMigrationsAsync();

        Assert.Collection(
            controlPlane.Commands,
            command =>
            {
                Assert.Contains("ADD VERTEX __NodalMigration", command.Text, StringComparison.Ordinal);
                Assert.Contains("ADD VERTEX __NodalSchemaJob", command.Text, StringComparison.Ordinal);
            },
            command => Assert.Equal("RUN SCHEMA_CHANGE JOB nodal_migration_bootstrap", command.Text),
            command => Assert.Equal("DROP JOB nodal_migration_bootstrap", command.Text));
        Assert.Single(handler.Requests,
            request => request.Uri.Contains("gsql/v1/schema", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StatefulHistoryStoreReadsWritesAndRemovesEntries()
    {
        using var client = Client(out var handler);
        handler.AddHistory("001_stateful", "checksum-001", "Applying");
        var store = new TigerGraphMigrationHistoryStore(
            client, Options(), "SocialGraph", new RecordingControlPlane());

        var history = await store.GetMigrationHistoryAsync();
        Assert.Equal(MigrationExecutionState.Applying, history["001_stateful"].State);
        var entry = new MigrationHistoryEntry(
            "001_stateful", "checksum-001", MigrationExecutionState.Applied,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(1));
        await store.SaveMigrationHistoryAsync(entry);
        await store.RemoveMigrationHistoryAsync("001_stateful");

        Assert.Null(handler.HistoryChecksum("001_stateful"));
        Assert.Contains(handler.Requests, request => request.Method == HttpMethod.Delete);
    }

    [Fact]
    public async Task StatefulHistoryStoreMaterializesFailureMetadata()
    {
        using var client = Client(out var handler);
        handler.AddFailedHistory("failed", "checksum-failed");
        var store = new TigerGraphMigrationHistoryStore(
            client, Options(), "SocialGraph", new RecordingControlPlane());

        var entry = (await store.GetMigrationHistoryAsync())["failed"];

        Assert.Equal(MigrationExecutionState.Failed, entry.State);
        Assert.Equal("schema failed", entry.Failure!.Message);
        Assert.Equal("System.InvalidOperationException", entry.Failure.ErrorType);
        Assert.Equal(
            DateTimeOffset.Parse("2026-08-23T10:01:00Z", System.Globalization.CultureInfo.InvariantCulture),
            entry.Failure.OccurredAt);
    }

    private static HttpClient Client(out InMemoryHandler handler, bool includeInfrastructure = true)
    {
        handler = new InMemoryHandler(includeInfrastructure);
        return new HttpClient(handler);
    }

    private static MigrationExecution Execution() => new(
        "001_initial", "checksum",
        [
            new MigrationCommand("CREATE SCHEMA_CHANGE JOB nodal_job FOR GRAPH SocialGraph { ADD VERTEX Demo(PRIMARY_ID Id STRING); }", false),
            new MigrationCommand("RUN SCHEMA_CHANGE JOB nodal_job", false),
            new MigrationCommand("DROP JOB nodal_job", false),
        ]);

    private static TigerGraphSchemaJobJournalEntry Journal(TigerGraphSchemaJobPhase phase) => new(
        "001_initial", "checksum", "nodal_job", TigerGraphSchemaJobDirection.Up,
        phase, phase is TigerGraphSchemaJobPhase.Applied, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private static TigerGraphOptions Options() => new()
    {
        Endpoint = new Uri("https://tigergraph.example/", UriKind.Absolute),
        AccessToken = "token",
    };

    private sealed class ExecuteOnlyTransport : ITigerGraphAdministrativeTransport
    {
        public ValueTask ExecuteAsync(MigrationCommand command, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class RecordingControlPlane : ITigerGraphAdministrativeControlPlane
    {
        private readonly HashSet<string> jobs = new(StringComparer.Ordinal);
        public List<MigrationCommand> Commands { get; } = [];
        public Exception? RunFailure { get; init; }
        public bool CancelRun { get; set; }
        public Exception? DropFailure { get; set; }
        public TigerGraphAdministrativeCapabilities Capabilities { get; init; } = new(
            "4.2.4", true, true, true, true, TigerGraphMigrationLockScope.Process);

        public ValueTask<TigerGraphAdministrativeCapabilities> DiscoverCapabilitiesAsync(
            string graphName, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Capabilities);

        public ValueTask<bool> SchemaJobExistsAsync(
            string graphName, string jobName, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(jobs.Contains(jobName));

        public ValueTask<IAsyncDisposable> AcquireMigrationLockAsync(
            string graphName, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IAsyncDisposable>(new Lease());

        public ValueTask ExecuteAsync(MigrationCommand command, CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            var name = command.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Last();
            if (command.Text.StartsWith("CREATE SCHEMA_CHANGE JOB", StringComparison.Ordinal))
                jobs.Add(command.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries)[3]);
            if (command.Text.StartsWith("DROP JOB", StringComparison.Ordinal))
            {
                if (DropFailure is not null) throw DropFailure;
                jobs.Remove(name);
            }
            if (command.Text.StartsWith("RUN SCHEMA_CHANGE JOB", StringComparison.Ordinal))
            {
                if (CancelRun) throw new OperationCanceledException(cancellationToken);
                if (RunFailure is not null) throw RunFailure;
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class Lease : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class InMemoryHandler(bool includeInfrastructure) : HttpMessageHandler
    {
        private readonly Dictionary<string, JsonObject> journal = new(StringComparer.Ordinal);
        private readonly Dictionary<string, JsonObject> history = new(StringComparer.Ordinal);
        public List<CapturedRequest> Requests { get; } = [];
        public bool FailNextHistoryWrite { get; set; }
        public bool ErrorNextWrite { get; set; }
        public string MissingJournalPayload { get; set; } =
            "{\"error\":true,\"message\":\"missing\",\"code\":\"601\"}";

        public TigerGraphSchemaJobJournalEntry? Journal(string id) =>
            journal.TryGetValue(id, out var value) ? ToJournal(id, value) : null;

        public string? HistoryChecksum(string id) =>
            history.TryGetValue(id, out var value) ? value["Checksum"]?.GetValue<string>() : null;

        public void SaveJournal(TigerGraphSchemaJobJournalEntry entry) => journal[entry.MigrationId] = new JsonObject
        {
            ["Checksum"] = entry.Checksum,
            ["JobName"] = entry.JobName,
            ["Direction"] = entry.Direction.ToString(),
            ["Phase"] = entry.Phase.ToString(),
            ["CleanupCompleted"] = entry.CleanupCompleted,
            ["StartedAt"] = entry.StartedAt.ToString("O"),
            ["UpdatedAt"] = entry.UpdatedAt.ToString("O"),
            ["FailureMessage"] = entry.FailureMessage ?? string.Empty,
            ["FailureType"] = entry.FailureType ?? string.Empty,
        };

        public void AddHistory(string id, string checksum, string? state) => history[id] = new JsonObject
        {
            ["Checksum"] = checksum,
            ["State"] = state,
            ["StartedAt"] = "2026-08-23T10:00:00.0000000+00:00",
            ["CompletedAt"] = string.Empty,
            ["FailureMessage"] = string.Empty,
            ["FailureType"] = string.Empty,
            ["FailureAt"] = string.Empty,
        };

        public void AddFailedHistory(string id, string checksum) => history[id] = new JsonObject
        {
            ["Checksum"] = checksum,
            ["State"] = "Failed",
            ["StartedAt"] = "2026-08-23T10:00:00Z",
            ["CompletedAt"] = "2026-08-23T10:01:00Z",
            ["FailureMessage"] = "schema failed",
            ["FailureType"] = "System.InvalidOperationException",
            ["FailureAt"] = "2026-08-23T10:01:00Z",
        };

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = request.Content is null
                ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(request.Method, request.RequestUri!.ToString(),
                request.Headers.TryGetValues("gsql-atomic-level", out var values) ? values.Single() : null, content));
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("gsql/v1/schema", StringComparison.Ordinal))
            {
                var types = includeInfrastructure
                    ? "[{\"Name\":\"__NodalMigration\"},{\"Name\":\"__NodalSchemaJob\"}]" : "[]";
                return Json($"{{\"error\":false,\"results\":{{\"VertexTypes\":{types}}}}}");
            }

            if (request.Method == HttpMethod.Get && path.Contains("/__NodalSchemaJob/", StringComparison.Ordinal))
            {
                var id = Uri.UnescapeDataString(path.Split('/').Last());
                return journal.TryGetValue(id, out var attributes)
                    ? Json(Vertex(id, attributes))
                    : Json(MissingJournalPayload);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/__NodalMigration", StringComparison.Ordinal))
                return Json(List(history));
            if (request.Method == HttpMethod.Post)
            {
                if (ErrorNextWrite)
                {
                    ErrorNextWrite = false;
                    return Json("{\"error\":true,\"message\":\"rejected\"}");
                }

                if (FailNextHistoryWrite && content!.Contains("__NodalMigration", StringComparison.Ordinal))
                {
                    FailNextHistoryWrite = false;
                    return Json("{\"error\":true,\"message\":\"history failed\"}", HttpStatusCode.InternalServerError);
                }

                Store(content!);
                return Json("{\"error\":false,\"results\":[{\"accepted_vertices\":1}]}");
            }

            if (request.Method == HttpMethod.Delete)
            {
                var id = Uri.UnescapeDataString(path.Split('/').Last());
                history.Remove(id);
                return Json("{\"error\":false,\"results\":[{\"deleted_vertices\":1}]}");
            }

            return Json("{\"error\":false,\"results\":[]}");
        }

        private void Store(string content)
        {
            var vertices = JsonNode.Parse(content)!["vertices"]!.AsObject();
            foreach (var type in vertices)
                foreach (var item in type.Value!.AsObject())
                {
                    var values = new JsonObject();
                    foreach (var attribute in item.Value!.AsObject())
                        values[attribute.Key] = attribute.Value!["value"]?.DeepClone();
                    (type.Key == "__NodalSchemaJob" ? journal : history)[item.Key] = values;
                }
        }

        private static string List(Dictionary<string, JsonObject> values) =>
            new JsonObject
            {
                ["error"] = false,
                ["results"] = new JsonArray(values.Select(item =>
                    (JsonNode)new JsonObject { ["v_id"] = item.Key, ["attributes"] = item.Value.DeepClone() }).ToArray()),
            }.ToJsonString();

        private static string Vertex(string id, JsonObject attributes) =>
            new JsonObject
            {
                ["error"] = false,
                ["results"] = new JsonArray(new JsonObject
                {
                    ["v_id"] = id,
                    ["attributes"] = attributes.DeepClone(),
                }),
            }.ToJsonString();

        private static HttpResponseMessage Json(string value, HttpStatusCode status = HttpStatusCode.OK) => new(status)
        {
            Content = new StringContent(value, Encoding.UTF8, "application/json"),
        };

        private static TigerGraphSchemaJobJournalEntry ToJournal(string id, JsonObject value) => new(
            id, value["Checksum"]!.GetValue<string>(), value["JobName"]!.GetValue<string>(),
            Enum.Parse<TigerGraphSchemaJobDirection>(value["Direction"]!.GetValue<string>()),
            Enum.Parse<TigerGraphSchemaJobPhase>(value["Phase"]!.GetValue<string>()),
            value["CleanupCompleted"]!.GetValue<bool>(),
            DateTimeOffset.Parse(value["StartedAt"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(value["UpdatedAt"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture),
            value["FailureMessage"]?.GetValue<string>(), value["FailureType"]?.GetValue<string>());
    }

    private sealed record CapturedRequest(HttpMethod Method, string Uri, string? AtomicLevel, string? Content);
}
