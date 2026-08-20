using System.Net.Http.Headers;
using System.Text.Json;
using Nodal.Core;
using Nodal.Core.Metadata;
using Nodal.Core.Query;
using Nodal.TigerGraph;

namespace Nodal.IntegrationTests;

public sealed class TigerGraphConnectionTests
{
    [TigerGraphIntegrationFact]
    [Trait("Category", "Integration")]
    [Trait("Provider", "TigerGraph")]
    public async Task UnitOfWorkCreatesReadsAndUpdatesThroughLiveRestConnection()
    {
        var endpoint = new Uri(
            Environment.GetEnvironmentVariable("NODAL_TIGERGRAPH_ENDPOINT")!,
            UriKind.Absolute);
        var options = CreateOptions(endpoint);
        var graphName = Environment.GetEnvironmentVariable("NODAL_TIGERGRAPH_GRAPH")!;
        using var httpClient = new HttpClient { BaseAddress = endpoint };
        var provider = new TigerGraphProvider(
            httpClient,
            options,
            graphName);
        var context = new SocialContext(provider);
        var suffix = Guid.NewGuid().ToString("N");
        var source = new Person($"nodal-source-{suffix}", "Ada");
        var target = new Person($"nodal-target-{suffix}", "Alan");
        var relation = new Knows(2020);

        try
        {
            context.People.Add(source);
            context.People.Add(target);
            context.Friendships.Connect(source, relation, target);
            var result = await context.SaveChangesAsync();

            Assert.True(result.IsAtomic);
            Assert.Equal(2, result.AffectedNodes);
            Assert.Equal(1, result.AffectedRelations);

            var readContext = new SocialContext(provider);
            var storedSource = Assert.Single(await readContext.People
                .Match(person => person.Id == source.Id)
                .ToListAsync());
            var storedPath = Assert.Single(await readContext.People
                .Match(person => person.Id == source.Id)
                .TraversePath(readContext.Friendships)
                .ToListAsync());
            var detachedContext = new SocialContext(provider);
            string[] selectedIds = [source.Id, target.Id];
            var paged = await detachedContext.People.Query()
                .Where(person => selectedIds.Contains(person.Id) && person.Name.StartsWith("Ad"))
                .OrderBy(person => person.Name)
                .Skip(0)
                .Take(1)
                .AsNoTracking()
                .ToListAsync();
            var raw = await detachedContext.Database.QueryRawAsync<Person>(
                $"INTERPRET QUERY (STRING id) FOR GRAPH {graphName} {{ result = SELECT node FROM Person:node WHERE node.Id == id; PRINT result; }}",
                new Dictionary<string, object?> { ["id"] = target.Id });
            var subgraph = await detachedContext.People.Match(person => person.Id == source.Id)
                .Traverse(detachedContext.Friendships)
                .WithoutCycles()
                .ToSubgraphAsync();
            var count = await detachedContext.People.Query()
                .Where(person => selectedIds.Contains(person.Id))
                .CountAsync();
            Assert.Equal("Ada", storedSource.Name);
            Assert.Equal(target.Id, storedPath.Target.Id);
            Assert.Equal(2020, storedPath.Relation.SinceYear);
            Assert.Equal("Ada", Assert.Single(paged).Name);
            Assert.Empty(detachedContext.ChangeTracker.Entries());
            Assert.Equal("Alan", Assert.Single(raw).Name);
            Assert.Equal(2, subgraph.Nodes.Count);
            Assert.Single(subgraph.RelationRecords);
            Assert.Equal(2, count);

            source.Name = "Ada Lovelace";
            relation.SinceYear = 2025;
            context.People.Update(source);
            context.Friendships.Update(source, relation, target);
            var updated = await context.SaveChangesAsync();

            Assert.Equal(1, updated.AffectedNodes);
            Assert.Equal(1, updated.AffectedRelations);
            var verificationContext = new SocialContext(provider);
            var updatedSource = Assert.Single(await verificationContext.People
                .Match(person => person.Id == source.Id)
                .ToListAsync());
            var updatedPath = Assert.Single(await verificationContext.People
                .Match(person => person.Id == source.Id)
                .TraversePath(verificationContext.Friendships)
                .ToListAsync());
            Assert.Equal("Ada Lovelace", updatedSource.Name);
            Assert.Equal(2025, updatedPath.Relation.SinceYear);
        }
        finally
        {
            await DeleteVertexAsync(httpClient, options, graphName, source.Id);
            await DeleteVertexAsync(httpClient, options, graphName, target.Id);
        }
    }

    [TigerGraphIntegrationFact]
    [Trait("Category", "Integration")]
    [Trait("Provider", "TigerGraph")]
    public async Task InvalidEdgeRollsBackVerticesInAtomicRestBatch()
    {
        var endpoint = new Uri(
            Environment.GetEnvironmentVariable("NODAL_TIGERGRAPH_ENDPOINT")!,
            UriKind.Absolute);
        var options = CreateOptions(endpoint);
        var graphName = Environment.GetEnvironmentVariable("NODAL_TIGERGRAPH_GRAPH")!;
        using var httpClient = new HttpClient { BaseAddress = endpoint };
        var provider = new TigerGraphProvider(
            httpClient,
            options,
            graphName);
        var context = new FailureContext(provider);
        var suffix = Guid.NewGuid().ToString("N");
        var source = new Person($"nodal-rollback-source-{suffix}", "Source");
        var target = new Person($"nodal-rollback-target-{suffix}", "Target");

        try
        {
            context.People.Add(source);
            context.People.Add(target);
            context.InvalidRelations.Connect(source, new MissingRelation(), target);

            await Assert.ThrowsAnyAsync<Exception>(async () => await context.SaveChangesAsync());

            Assert.False(await VertexExistsAsync(httpClient, options, graphName, source.Id));
            Assert.False(await VertexExistsAsync(httpClient, options, graphName, target.Id));
        }
        finally
        {
            await DeleteVertexAsync(httpClient, options, graphName, source.Id);
            await DeleteVertexAsync(httpClient, options, graphName, target.Id);
        }
    }

    private static TigerGraphOptions CreateOptions(Uri endpoint) => new()
    {
        Endpoint = endpoint,
        AccessToken = Environment.GetEnvironmentVariable("NODAL_TIGERGRAPH_ACCESS_TOKEN"),
        Username = Environment.GetEnvironmentVariable("NODAL_TIGERGRAPH_USERNAME"),
        Password = Environment.GetEnvironmentVariable("NODAL_TIGERGRAPH_PASSWORD"),
    };

    private static async Task DeleteVertexAsync(
        HttpClient httpClient,
        TigerGraphOptions options,
        string graphName,
        string identity)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"restpp/graph/{Uri.EscapeDataString(graphName)}/vertices/Person/{Uri.EscapeDataString(identity)}");
        ApplyAuthentication(request, options);
        using var response = await httpClient.SendAsync(request);
        if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }
    }

    private static async Task<bool> VertexExistsAsync(
        HttpClient httpClient,
        TigerGraphOptions options,
        string graphName,
        string identity)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"restpp/graph/{Uri.EscapeDataString(graphName)}/vertices/Person/{Uri.EscapeDataString(identity)}");
        ApplyAuthentication(request, options);
        using var response = await httpClient.SendAsync(request);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(payload);
        return ContainsVertex(document.RootElement, identity);
    }

    private static void ApplyAuthentication(HttpRequestMessage request, TigerGraphOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.AccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.AccessToken);
            return;
        }

        var credentials = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{options.Username}:{options.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    private static bool ContainsVertex(JsonElement element, string identity)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("v_id", out var id) && id.GetString() == identity)
            {
                return true;
            }

            return element.EnumerateObject().Any(property => ContainsVertex(property.Value, identity));
        }

        return element.ValueKind == JsonValueKind.Array &&
            element.EnumerateArray().Any(item => ContainsVertex(item, identity));
    }

    private sealed class SocialContext(TigerGraphProvider provider) : NodalContext(provider)
    {
        public GraphSet<Person> People => Set<Person>();

        public RelationSet<Person, Knows, Person> Friendships => Relations<Person, Knows, Person>();
    }

    private sealed class FailureContext(TigerGraphProvider provider) : NodalContext(provider)
    {
        public GraphSet<Person> People => Set<Person>();

        public RelationSet<Person, MissingRelation, Person> InvalidRelations =>
            Relations<Person, MissingRelation, Person>();
    }

    [GraphNode("Person")]
    private sealed class Person(string id, string name)
    {
        [GraphKey]
        public string Id { get; } = id;

        public string Name { get; set; } = name;
    }

    [GraphRelation("KNOWS")]
    private sealed class Knows(int sinceYear)
    {
        public int SinceYear { get; set; } = sinceYear;
    }

    [GraphRelation("NODAL_INTENTIONALLY_MISSING_EDGE")]
    private sealed class MissingRelation;
}
