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
        var accessToken = Environment.GetEnvironmentVariable("NODAL_TIGERGRAPH_ACCESS_TOKEN")!;
        var graphName = Environment.GetEnvironmentVariable("NODAL_TIGERGRAPH_GRAPH")!;
        using var httpClient = new HttpClient { BaseAddress = endpoint };
        var provider = new TigerGraphProvider(
            httpClient,
            new TigerGraphOptions
            {
                Endpoint = endpoint,
                AccessToken = accessToken,
            },
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
            Assert.Equal("Ada", storedSource.Name);
            Assert.Equal(target.Id, storedPath.Target.Id);
            Assert.Equal(2020, storedPath.Relation.SinceYear);

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
            await DeleteVertexAsync(httpClient, accessToken, graphName, source.Id);
            await DeleteVertexAsync(httpClient, accessToken, graphName, target.Id);
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
        var accessToken = Environment.GetEnvironmentVariable("NODAL_TIGERGRAPH_ACCESS_TOKEN")!;
        var graphName = Environment.GetEnvironmentVariable("NODAL_TIGERGRAPH_GRAPH")!;
        using var httpClient = new HttpClient { BaseAddress = endpoint };
        var provider = new TigerGraphProvider(
            httpClient,
            new TigerGraphOptions
            {
                Endpoint = endpoint,
                AccessToken = accessToken,
            },
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

            Assert.False(await VertexExistsAsync(httpClient, accessToken, graphName, source.Id));
            Assert.False(await VertexExistsAsync(httpClient, accessToken, graphName, target.Id));
        }
        finally
        {
            await DeleteVertexAsync(httpClient, accessToken, graphName, source.Id);
            await DeleteVertexAsync(httpClient, accessToken, graphName, target.Id);
        }
    }

    private static async Task DeleteVertexAsync(
        HttpClient httpClient,
        string accessToken,
        string graphName,
        string identity)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"restpp/graph/{Uri.EscapeDataString(graphName)}/vertices/Person/{Uri.EscapeDataString(identity)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClient.SendAsync(request);
        if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }
    }

    private static async Task<bool> VertexExistsAsync(
        HttpClient httpClient,
        string accessToken,
        string graphName,
        string identity)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"restpp/graph/{Uri.EscapeDataString(graphName)}/vertices/Person/{Uri.EscapeDataString(identity)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
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
