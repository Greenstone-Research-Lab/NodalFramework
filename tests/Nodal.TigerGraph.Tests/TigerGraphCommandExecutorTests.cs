using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Nodal.Core.Providers;

namespace Nodal.TigerGraph.Tests;

public sealed class TigerGraphCommandExecutorTests
{
    [Fact]
    public async Task InstalledAnalyticsRoutePreservesCanonicalNodeMetricRows()
    {
        var handler = new RecordingHandler("""
        {"error":false,"results":[{"nodal_node":{"v_id":"p1","v_type":"Person","attributes":{"Name":"Ada"}},"nodal_metrics":{"score":0.9,"communityId":7}}]}
        """);
        using var client = new HttpClient(handler);
        var executor = new TigerGraphCommandExecutor(client, TokenOptions());

        var result = await executor.ExecuteAsync(new GraphCommand(
            string.Empty,
            new Dictionary<string, object?> { ["nodal_limit"] = 10 },
            "restpp/query/SocialGraph/nodal_pagerank"));

        var row = Assert.Single(result.ResultRows);
        Assert.Equal("p1", row.Node?.Id);
        Assert.Equal(0.9, row.Values["score"]);
        Assert.Contains("restpp/query/SocialGraph/nodal_pagerank", handler.RequestUri?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsyncNormalizesSyntaxV2TabularRowsAndAggregateObjects()
    {
        var handler = new RecordingHandler("""
        {"error":false,"results":[{"nodal_rows":[{"name":"Ada","orders":2},{"name":"Alan","orders":1}]},{"nodal_rows":{"total":3}}]}
        """);
        using var client = new HttpClient(handler);
        var result = await new TigerGraphCommandExecutor(client, TokenOptions()).ExecuteAsync(
            new GraphCommand("INTERPRET QUERY", new Dictionary<string, object?>()));

        Assert.Collection(
            result.ResultRows,
            row =>
            {
                Assert.Null(row.Node);
                Assert.Equal("Ada", row.Values["name"]);
                Assert.Equal(2L, row.Values["orders"]);
            },
            row => Assert.Equal("Alan", row.Values["name"]),
            row => Assert.Equal(3L, row.Values["total"]));
    }

    [Fact]
    public async Task InstalledPathRoutePreservesOrderingRelationsAndCost()
    {
        var handler = new RecordingHandler("""
        {"error":false,"results":[{"nodal_nodes":[{"v_id":"p1","v_type":"Person","attributes":{}},{"v_id":"p2","v_type":"Person","attributes":{}}],"nodal_relations":[{"e_type":"KNOWS","e_id":"e1","from_id":"p1","to_id":"p2","attributes":{}}],"nodal_total_cost":4.5}]}
        """);
        using var client = new HttpClient(handler);
        var result = await new TigerGraphCommandExecutor(client, TokenOptions()).ExecuteAsync(
            new GraphCommand(string.Empty, new Dictionary<string, object?>(), "restpp/query/G/path"));

        var route = Assert.Single(result.RouteRecords);
        Assert.Equal(["p1", "p2"], route.Nodes.Select(node => node.Id));
        Assert.Equal("e1", Assert.Single(route.Relations).Id);
        Assert.Equal(4.5, route.TotalCost);
    }
    [Fact]
    public async Task ExecuteAsyncSendsGsqlAndNormalizesVertexResults()
    {
        const string response = """
            {
              "error": false,
              "results": [{
                "Result": [{
                  "v_id": "person-42",
                  "v_type": "Person",
                  "attributes": { "Name": "Ada", "Age": 24 }
                }]
              }]
            }
            """;
        var handler = new RecordingHandler(response);
        using var client = new HttpClient(handler);
        var executor = new TigerGraphCommandExecutor(client, new TigerGraphOptions
        {
            Endpoint = new Uri("https://tigergraph.example/", UriKind.Absolute),
            Username = "graph-user",
            Password = "secret",
        });
        var command = new GraphCommand(
            "INTERPRET QUERY () FOR GRAPH SocialGraph { PRINT 1; }",
            new Dictionary<string, object?> { ["minimumAge"] = 18 });

        var result = await executor.ExecuteAsync(command);

        Assert.Equal("https://tigergraph.example/gsql/v1/queries/interpret?minimumAge=18", handler.RequestUri?.ToString());
        Assert.Equal(new AuthenticationHeaderValue("Basic", "Z3JhcGgtdXNlcjpzZWNyZXQ="), handler.Authorization);
        Assert.Equal(command.Text, handler.Content);
        var node = Assert.Single(result.Nodes);
        Assert.Equal("person-42", node.Id);
        Assert.Equal("Ada", node.Properties["Name"]);
    }

    [Fact]
    public async Task ExecuteAsyncPrefersBearerTokenAuthentication()
    {
        var handler = new RecordingHandler("""{"error":false,"results":[]}""");
        using var client = new HttpClient(handler);
        var executor = new TigerGraphCommandExecutor(client, new TigerGraphOptions
        {
            Endpoint = new Uri("https://tigergraph.example/", UriKind.Absolute),
            AccessToken = "token-42",
        });

        await executor.ExecuteAsync(new GraphCommand("INTERPRET QUERY", new Dictionary<string, object?>()));

        Assert.Equal(new AuthenticationHeaderValue("Bearer", "token-42"), handler.Authorization);
    }

    [Fact]
    public async Task ExecuteAsyncRepeatsCollectionParametersAndFormatsDates()
    {
        var handler = new RecordingHandler("""{"error":false,"results":[]}""");
        using var client = new HttpClient(handler);
        var executor = new TigerGraphCommandExecutor(client, new TigerGraphOptions
        {
            Endpoint = new Uri("https://tigergraph.example/", UriKind.Absolute),
            AccessToken = "token",
        });

        await executor.ExecuteAsync(new GraphCommand("INTERPRET QUERY", new Dictionary<string, object?>
        {
            ["ids"] = new List<string> { "person-1", "person-2" },
            ["created"] = new DateTime(2026, 8, 20, 12, 30, 0, DateTimeKind.Utc),
        }));

        Assert.Equal(
            "https://tigergraph.example/gsql/v1/queries/interpret?ids=person-1&ids=person-2&created=2026-08-20 12%3A30%3A00",
            handler.RequestUri?.ToString());
    }

    [Fact]
    public async Task ExecuteAsyncNormalizesCanonicalPathFromNamedGsqlOutputs()
    {
        const string response = """
            {
              "error": false,
              "results": [{
                "nodal_sources": [{"v_id":"person-1","v_type":"Person","attributes":{"Name":"Ada"}}],
                "nodal_relations": [{
                  "e_id":"edge-1","e_type":"KNOWS","from_id":"person-1","to_id":"person-2",
                  "attributes":{"SinceYear":2020}
                }],
                "nodal_targets": [{"v_id":"person-2","v_type":"Person","attributes":{"Name":"Alan"}}]
              }]
            }
            """;
        var handler = new RecordingHandler(response);
        using var client = new HttpClient(handler);
        var executor = new TigerGraphCommandExecutor(client, new TigerGraphOptions
        {
            Endpoint = new Uri("https://tigergraph.example/", UriKind.Absolute),
            AccessToken = "token",
        });

        var result = await executor.ExecuteAsync(new GraphCommand("INTERPRET QUERY", new Dictionary<string, object?>()));

        var path = Assert.Single(result.PathRecords);
        Assert.Equal("person-1", path.Source.Id);
        Assert.Equal("edge-1", path.Relation.Id);
        Assert.Equal(2020L, path.Relation.Properties["SinceYear"]);
        Assert.Equal("person-2", path.Target.Id);
    }

    [Fact]
    public async Task ExecuteAsyncCoversScalarFallbackAndParameterFormatting()
    {
        const string response = """
            {
              "error": false,
              "results": [{
                "nodes": [{"v_id":42,"v_type":"Person"}],
                "relations": [{"e_type":"KNOWS","from_id":42,"to_id":43}],
                "nodal_count": 1.5,
                "nodal_active": true,
                "nodal_optional": null,
                "ignored": {"value": "nested"}
              }]
            }
            """;
        var handler = new RecordingHandler(response);
        using var client = new HttpClient(handler);
        var executor = new TigerGraphCommandExecutor(client, new TigerGraphOptions
        {
            Endpoint = new Uri("https://tigergraph.example/", UriKind.Absolute),
            AccessToken = "token",
        });

        var result = await executor.ExecuteAsync(new GraphCommand(
            "INTERPRET QUERY",
            new Dictionary<string, object?>
            {
                ["active"] = true,
                ["missing"] = null,
                ["custom"] = new TextValue(),
            }));

        Assert.Contains("active=true", handler.RequestUri?.Query, StringComparison.Ordinal);
        Assert.Contains("missing=", handler.RequestUri?.Query, StringComparison.Ordinal);
        Assert.Contains("custom=custom-value", handler.RequestUri?.Query, StringComparison.Ordinal);
        Assert.Equal(42L, Assert.Single(result.Nodes).Id);
        Assert.Equal("42->43", Assert.Single(result.RelationRecords).Id);
        Assert.Empty(result.PathRecords);
        Assert.Equal(1.5, result.ScalarValues["nodal_count"]);
        Assert.Equal(true, result.ScalarValues["nodal_active"]);
        Assert.Null(result.ScalarValues["nodal_optional"]);
    }

    [Fact]
    public async Task ExecuteAsyncSurfacesHttpAuthenticationAndTigerGraphErrors()
    {
        using var unauthenticatedClient = new HttpClient(new RecordingHandler("{}"));
        var unauthenticated = new TigerGraphCommandExecutor(
            unauthenticatedClient,
            new TigerGraphOptions { Endpoint = new Uri("https://tigergraph.example/") });
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await unauthenticated.ExecuteAsync(
            new GraphCommand("QUERY", new Dictionary<string, object?>())));

        using var failedClient = new HttpClient(new RecordingHandler("denied", HttpStatusCode.Forbidden));
        var failed = new TigerGraphCommandExecutor(failedClient, TokenOptions());
        var httpError = await Assert.ThrowsAsync<HttpRequestException>(async () => await failed.ExecuteAsync(
            new GraphCommand("QUERY", new Dictionary<string, object?>())));
        Assert.Equal(HttpStatusCode.Forbidden, httpError.StatusCode);

        using var errorClient = new HttpClient(new RecordingHandler("""{"error":true,"message":"bad query"}"""));
        var errored = new TigerGraphCommandExecutor(errorClient, TokenOptions());
        var graphError = await Assert.ThrowsAsync<InvalidOperationException>(async () => await errored.ExecuteAsync(
            new GraphCommand("QUERY", new Dictionary<string, object?>())));
        Assert.Equal("bad query", graphError.Message);

        using var unknownClient = new HttpClient(new RecordingHandler("""{"error":true}"""));
        var unknown = new TigerGraphCommandExecutor(unknownClient, TokenOptions());
        var unknownError = await Assert.ThrowsAsync<InvalidOperationException>(async () => await unknown.ExecuteAsync(
            new GraphCommand("QUERY", new Dictionary<string, object?>())));
        Assert.Contains("unknown", unknownError.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConstructorRejectsNullDependencies()
    {
        using var client = new HttpClient();

        Assert.Throws<ArgumentNullException>(() => new TigerGraphCommandExecutor(null!, TokenOptions()));
        Assert.Throws<ArgumentNullException>(() => new TigerGraphCommandExecutor(client, null!));
    }

    private static TigerGraphOptions TokenOptions() => new()
    {
        Endpoint = new Uri("https://tigergraph.example/", UriKind.Absolute),
        AccessToken = "token",
    };

    private sealed class RecordingHandler(
        string response,
        HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        public AuthenticationHeaderValue? Authorization { get; private set; }

        public string? Content { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization;
            Content = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class TextValue
    {
        public override string ToString() => "custom-value";
    }
}
