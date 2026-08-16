using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Nodal.Core.ChangeTracking;
using Nodal.Core.Migrations;
using Nodal.Core.Mutations;

namespace Nodal.TigerGraph.Tests;

public sealed class TigerGraphMutationExecutorTests
{
    [Fact]
    public void ProviderReportsRequestScopedAtomicTransactions()
    {
        using var client = new HttpClient(new RecordingHandler("{}"));
        var provider = new TigerGraphProvider(client, TokenOptions(), "SocialGraph");

        Assert.True(provider.Capabilities.SupportsTransactions);
        Assert.True(provider.Capabilities.SupportsAtomicBatch);
        Assert.Equal(GraphTransactionScope.RequestOrQuery, provider.Capabilities.TransactionScope);
        Assert.False(provider.Capabilities.SupportsSavepoints);
        Assert.False(provider.Capabilities.SupportsOptimisticConcurrency);
        Assert.Same(provider.MutationExecutor, ((IGraphMutationProvider)provider).MutationExecutor);
    }

    [Fact]
    public async Task ExecuteAsyncSendsOneAtomicUpsertForNodesAndRelations()
    {
        const string response = """
            {
              "error": false,
              "results": [{ "accepted_vertices": 2, "accepted_edges": 1 }]
            }
            """;
        var handler = new RecordingHandler(response);
        using var client = new HttpClient(handler);
        var executor = new TigerGraphMutationExecutor(client, TokenOptions(), "SocialGraph");
        var source = Identity("Person", "person-1");
        var target = Identity("Person", "person-2");
        var plan = new GraphMutationPlan(
        [
            new CreateNodeOperation(source, Properties(("person_id", "person-1"), ("name", "Ada"))),
            new UpdateNodeOperation(target, Properties(("person_id", "person-2"), ("name", "Alan"))),
            new CreateRelationOperation(source, "KNOWS", target, true, Properties(("since", 2020))),
            new UpdateRelationOperation(source, "KNOWS", target, true, Properties(("since", 2025))),
        ]);

        var result = await executor.ExecuteAsync(plan);

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal(
            "https://tigergraph.example/restpp/graph/SocialGraph?vertex_must_exist=true",
            handler.RequestUri?.ToString());
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "access-token"), handler.Authorization);
        Assert.Equal("atomic", handler.AtomicLevel);
        Assert.True(result.IsAtomic);
        Assert.Equal(2, result.AffectedNodes);
        Assert.Equal(1, result.AffectedRelations);

        using var payload = JsonDocument.Parse(handler.Content!);
        var vertices = payload.RootElement.GetProperty("vertices").GetProperty("Person");
        Assert.Equal("Ada", vertices.GetProperty("person-1").GetProperty("name").GetProperty("value").GetString());
        Assert.False(vertices.GetProperty("person-1").TryGetProperty("person_id", out _));
        Assert.Equal(
            2025,
            payload.RootElement.GetProperty("edges")
                .GetProperty("Person")
                .GetProperty("person-1")
                .GetProperty("KNOWS")
                .GetProperty("Person")
                .GetProperty("person-2")
                .GetProperty("since")
                .GetProperty("value")
                .GetInt32());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DeletePlansAreRejectedInsteadOfBeingSplitAcrossRequests(bool deleteNode)
    {
        var handler = new RecordingHandler("{}");
        using var client = new HttpClient(handler);
        var executor = new TigerGraphMutationExecutor(client, TokenOptions(), "SocialGraph");
        var source = Identity("Person", "person-1");
        GraphMutationOperation operation = deleteNode
            ? new DeleteNodeOperation(source)
            : new DeleteRelationOperation(source, "KNOWS", Identity("Person", "person-2"), true);

        var exception = await Assert.ThrowsAsync<NotSupportedException>(
            async () => await executor.ExecuteAsync(new GraphMutationPlan([operation])));

        Assert.Contains("transactional mutation query", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task DeletePlanIsInstalledOnceAndExecutedAsOneParameterizedTransaction()
    {
        var handler = new RecordingHandler("""{"error":false,"results":[{"status":"ok"}]}""");
        var administration = new RecordingAdministrativeTransport();
        using var client = new HttpClient(handler);
        var executor = new TigerGraphMutationExecutor(
            client,
            TokenOptions(),
            "SocialGraph",
            administration);
        var source = Identity("Person", "person-1");
        var target = Identity("Person", "person-2");
        var firstPlan = new GraphMutationPlan(
        [
            new UpdateNodeOperation(source, Properties(("person_id", "person-1"), ("name", "Ada"))),
            new DeleteRelationOperation(source, "KNOWS", target, true),
            new DeleteNodeOperation(target),
        ]);

        var first = await executor.ExecuteAsync(firstPlan);
        var second = await executor.ExecuteAsync(new GraphMutationPlan(
        [
            new UpdateNodeOperation(
                Identity("Person", "person-3"),
                Properties(("person_id", "person-3"), ("name", "Grace"))),
            new DeleteRelationOperation(
                Identity("Person", "person-3"),
                "KNOWS",
                Identity("Person", "person-4"),
                true),
            new DeleteNodeOperation(Identity("Person", "person-4")),
        ]));

        Assert.Equal(2, administration.Commands.Count);
        var definition = administration.Commands[0];
        Assert.Equal(MigrationCommandKind.QueryDefinition, definition.Kind);
        Assert.Contains("CREATE OR REPLACE QUERY nodal_apply_mutations_", definition.Text, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO Person (PRIMARY_ID, name)", definition.Text, StringComparison.Ordinal);
        Assert.Contains("DELETE e FROM Person:s -(KNOWS:e)-> Person:t", definition.Text, StringComparison.Ordinal);
        Assert.Contains("DELETE n FROM Person:n", definition.Text, StringComparison.Ordinal);
        Assert.Equal(MigrationCommandKind.QueryInstallation, administration.Commands[1].Kind);
        Assert.StartsWith("INSTALL QUERY -FORCE nodal_apply_mutations_", administration.Commands[1].Text);
        Assert.Equal(2, handler.CallCount);
        Assert.All(handler.RequestUris, uri => Assert.Contains(
            "/restpp/query/SocialGraph/nodal_apply_mutations_",
            uri.ToString(),
            StringComparison.Ordinal));
        Assert.Contains("p0=person-1", handler.RequestUris[0].Query, StringComparison.Ordinal);
        Assert.Contains("p0=person-3", handler.RequestUris[1].Query, StringComparison.Ordinal);
        Assert.True(first.IsAtomic);
        Assert.Equal(2, first.AffectedNodes);
        Assert.Equal(1, first.AffectedRelations);
        Assert.Equal(first with { }, second);
    }

    [Fact]
    public async Task FailedQueryInstallationDoesNotExecuteTheMutation()
    {
        var handler = new RecordingHandler("""{"error":false}""");
        var administration = new RecordingAdministrativeTransport(failOnCommand: 2);
        using var client = new HttpClient(handler);
        var executor = new TigerGraphMutationExecutor(
            client,
            TokenOptions(),
            "SocialGraph",
            administration);
        var plan = new GraphMutationPlan([new DeleteNodeOperation(Identity("Person", "person-1"))]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await executor.ExecuteAsync(plan));

        Assert.Equal("administrative failure", exception.Message);
        Assert.Equal(2, administration.Commands.Count);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task ProviderAdministrativeOverloadEnablesTransactionalDeletes()
    {
        var handler = new RecordingHandler("""{"error":false}""");
        var administration = new RecordingAdministrativeTransport();
        using var client = new HttpClient(handler);
        var provider = new TigerGraphProvider(
            client,
            TokenOptions(),
            "SocialGraph",
            administration);

        var result = await provider.MutationExecutor.ExecuteAsync(
            new GraphMutationPlan([new DeleteNodeOperation(Identity("Person", "person-1"))]));

        Assert.True(result.IsAtomic);
        Assert.Single(handler.RequestUris);
        Assert.Equal(2, administration.Commands.Count);
    }

    [Fact]
    public async Task EmptyPlanCompletesAtomicallyWithoutHttpRequest()
    {
        var handler = new RecordingHandler("{}");
        using var client = new HttpClient(handler);
        var executor = new TigerGraphMutationExecutor(client, TokenOptions(), "SocialGraph");

        var result = await executor.ExecuteAsync(new GraphMutationPlan([]));

        Assert.True(result.IsAtomic);
        Assert.Equal(0, result.AffectedNodes);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task TigerGraphErrorResponseBecomesMutationFailure()
    {
        var handler = new RecordingHandler("""{"error":true,"message":"schema mismatch"}""");
        using var client = new HttpClient(handler);
        var executor = new TigerGraphMutationExecutor(client, TokenOptions(), "SocialGraph");
        var plan = new GraphMutationPlan(
            [new CreateNodeOperation(Identity("Person", "person-1"), Properties())]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await executor.ExecuteAsync(plan));

        Assert.Equal("schema mismatch", exception.Message);
    }

    private static TigerGraphOptions TokenOptions() => new()
    {
        Endpoint = new Uri("https://tigergraph.example/", UriKind.Absolute),
        AccessToken = "access-token",
    };

    private static GraphIdentity Identity(string nodeType, object value) =>
        new(typeof(object), nodeType, "person_id", value);

    private static Dictionary<string, object?> Properties(
        params (string Name, object? Value)[] properties) =>
        properties.ToDictionary(property => property.Name, property => property.Value);

    private sealed class RecordingHandler(string response) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        public HttpMethod? Method { get; private set; }

        public Uri? RequestUri { get; private set; }

        public AuthenticationHeaderValue? Authorization { get; private set; }

        public string? AtomicLevel { get; private set; }

        public string? Content { get; private set; }

        public List<Uri> RequestUris { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Method = request.Method;
            RequestUri = request.RequestUri;
            RequestUris.Add(request.RequestUri!);
            Authorization = request.Headers.Authorization;
            AtomicLevel = request.Headers.TryGetValues("gsql-atomic-level", out var values)
                ? Assert.Single(values)
                : null;
            Content = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class RecordingAdministrativeTransport(int? failOnCommand = null)
        : ITigerGraphAdministrativeTransport
    {
        public List<MigrationCommand> Commands { get; } = [];

        public ValueTask ExecuteAsync(
            MigrationCommand command,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add(command);
            if (Commands.Count == failOnCommand)
            {
                throw new InvalidOperationException("administrative failure");
            }

            return ValueTask.CompletedTask;
        }
    }
}
