using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Nodal.Core.Providers;

namespace Nodal.TigerGraph.Tests;

public sealed class TigerGraphCommandExecutorTests
{
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

    private sealed class RecordingHandler(string response) : HttpMessageHandler
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
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json"),
            };
        }
    }
}
