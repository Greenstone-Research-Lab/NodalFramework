using System.Net;
using System.Text;
using Nodal.Core.Migrations;

namespace Nodal.TigerGraph.Tests;

public sealed class TigerGraphMigrationExecutorTests
{
    [Fact]
    public void ProviderRequiresExplicitAdministrativeTransportForMigrations()
    {
        using var client = new HttpClient(new QueueHandler([]));
        var plain = new TigerGraphProvider(client, Options(), "SocialGraph");
        var configured = new TigerGraphProvider(client, Options(), "SocialGraph", new RecordingTransport());

        Assert.False(plain.SupportsMigrationExecution);
        Assert.Throws<NotSupportedException>(() => plain.MigrationExecutor);
        Assert.True(configured.SupportsMigrationExecution);
        Assert.IsType<TigerGraphMigrationExecutor>(configured.MigrationExecutor);
    }

    [Fact]
    public async Task HistoryReadBootstrapsMissingVertexTypeAndParsesChecksums()
    {
        var handler = new QueueHandler(
        [
            """{"error":false,"results":{"VertexTypes":[]}}""",
            """{"error":false,"results":[{"v_id":"001_initial","attributes":{"Checksum":"abc"}}]}""",
        ]);
        using var client = new HttpClient(handler);
        var transport = new RecordingTransport();
        var executor = new TigerGraphMigrationExecutor(client, Options(), "SocialGraph", transport);

        var history = await executor.GetAppliedMigrationsAsync();

        Assert.Equal("abc", history["001_initial"]);
        Assert.Collection(
            transport.Commands,
            command => Assert.Contains("ADD VERTEX __NodalMigration", command.Text, StringComparison.Ordinal),
            command => Assert.Equal("RUN SCHEMA_CHANGE JOB nodal_history_bootstrap", command.Text),
            command => Assert.Equal("DROP JOB nodal_history_bootstrap", command.Text));
    }

    [Fact]
    public async Task ApplyAndRevertRunJobsWithCleanupAndMaintainHistory()
    {
        var handler = new QueueHandler(
        [
            """{"error":false,"results":{"VertexTypes":[{"Name":"__NodalMigration"}]}}""",
            """{"error":false,"results":[{"accepted_vertices":1}]}""",
            """{"error":false,"results":[{"deleted_vertices":1}]}""",
        ]);
        using var client = new HttpClient(handler);
        var transport = new RecordingTransport();
        var executor = new TigerGraphMigrationExecutor(client, Options(), "SocialGraph", transport);
        var execution = Execution();

        await executor.ApplyAsync(execution);
        await executor.RevertAsync(execution);

        Assert.Equal(6, transport.Commands.Count);
        Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);
        Assert.Equal("atomic", handler.Requests[1].AtomicLevel);
        Assert.Contains("\"001_initial\"", handler.Requests[1].Content, StringComparison.Ordinal);
        Assert.Equal(HttpMethod.Delete, handler.Requests[2].Method);
        Assert.EndsWith("/__NodalMigration/001_initial", handler.Requests[2].Uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedSchemaRunAttemptsCleanupAndDoesNotWriteHistory()
    {
        var handler = new QueueHandler(
            ["""{"error":false,"results":{"VertexTypes":[{"Name":"__NodalMigration"}]}}"""]);
        using var client = new HttpClient(handler);
        var transport = new RecordingTransport { FailAtCall = 2 };
        var executor = new TigerGraphMigrationExecutor(client, Options(), "SocialGraph", transport);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await executor.ApplyAsync(Execution()));

        Assert.Equal(3, transport.Commands.Count);
        Assert.StartsWith("DROP JOB", transport.Commands[^1].Text, StringComparison.Ordinal);
        Assert.Single(handler.Requests);
    }

    private static MigrationExecution Execution() => new(
        "001_initial",
        "checksum",
        [
            new MigrationCommand("CREATE SCHEMA_CHANGE JOB nodal_job", false),
            new MigrationCommand("RUN SCHEMA_CHANGE JOB nodal_job", false),
            new MigrationCommand("DROP JOB nodal_job", false),
        ]);

    private static TigerGraphOptions Options() => new()
    {
        Endpoint = new Uri("https://tigergraph.example/", UriKind.Absolute),
        AccessToken = "token",
    };

    private sealed class RecordingTransport : ITigerGraphAdministrativeTransport
    {
        public List<MigrationCommand> Commands { get; } = [];

        public int? FailAtCall { get; init; }

        public ValueTask ExecuteAsync(MigrationCommand command, CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            if (Commands.Count == FailAtCall)
            {
                throw new InvalidOperationException("administrative failure");
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class QueueHandler(IEnumerable<string> responses) : HttpMessageHandler
    {
        private readonly Queue<string> responses = new(responses);

        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri!.ToString(),
                request.Headers.TryGetValues("gsql-atomic-level", out var values) ? values.Single() : null,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responses.Dequeue(), Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed record CapturedRequest(HttpMethod Method, string Uri, string? AtomicLevel, string? Content);
}
