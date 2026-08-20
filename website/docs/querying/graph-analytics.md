---
title: Graph analytics
description: Run strongly typed centrality and community-detection algorithms through Neo4j GDS or installed TigerGraph queries.
---

# Graph analytics

Nodal keeps graph analytics separate from ordinary record queries. A typed node selection and relationship mapping define the domain boundary; the provider executes the algorithm on the database server and returns row-preserving node metrics.

```csharp
var influentialPeople = await context.People.Query()
    .Analyze(context.Friendships)
    .PageRank(new PageRankOptions(DampingFactor: 0.85, MaximumIterations: 30))
    .OnProjection("social")
    .WeightedBy(friendship => friendship.Strength)
    .Top(20)
    .ToListAsync();

foreach (var result in influentialPeople)
{
    Console.WriteLine($"{result.Node?.Name}: {result.Score}");
}
```

Community detection uses the same entry point:

```csharp
var communities = await context.People.Query()
    .Analyze(context.Friendships)
    .Louvain(new LouvainOptions(IncludeIntermediateCommunities: true))
    .OnProjection("social")
    .ToListAsync();

foreach (var member in communities)
{
    Console.WriteLine($"{member.Node?.Name}: community {member.CommunityId}");
}
```

## Algorithm coverage

The provider-neutral contract models the complete centrality and community families rather than forcing every operation into a single score:

- centrality and structural importance: ArticleRank, articulation points, betweenness, bridges, CELF, closeness, degree, eigenvector, harmonic, HITS, and PageRank;
- community and cohesion: clique counting, conductance, HDBSCAN, K-core, K-1 coloring, K-means, label propagation, Leiden, local clustering coefficient, Louvain, modularity, modularity optimization, SCC, triangle count, WCC, approximate maximum k-cut, and speaker-listener label propagation.

`GraphAnalyticsRecord<TNode>.Metrics` preserves algorithm-specific values. `Score` and `CommunityId` are conveniences when those canonical fields exist. Graph-level and edge-level operations such as conductance, modularity, and bridges may return a null `Node` with their measurements intact.

## Capabilities and installation

Neo4j centrality, community, and weighted path algorithms require Graph Data Science. Native unweighted shortest paths do not. When GDS is enabled, Nodal can discover `gds.version()`, `gds.list()`, and existing projections with a bounded cache, then explicitly create, reuse, and drop projections:

```csharp
var runtime = context.Database.GetAnalyticsRuntime();
var deployment = await runtime.DiscoverAsync();
await runtime.EnsureProjectionAsync(
    new GraphProjectionDefinition("social", "Person", "KNOWS", WeightProperty: "strength"));
```

Typed path selection reuses mapped predicates and returns ordered nodes, ordered relationships, hop count, and optional total cost:

```csharp
GraphRoute<Person, Knows> route = await context.People
    .Match(person => person.Id == sourceId)
    .ShortestPathTo(context.People.Match(person => person.Id == targetId), context.Friendships)
    .MaxDepth(8)
    .SingleAsync();
```

TigerGraph algorithms execute through installed GSQL REST++ endpoints. Configure only queries that are actually installed:

```csharp
var options = new TigerGraphOptions
{
    Endpoint = new Uri("https://example.i.tgcloud.io/"),
    AccessToken = "secret-token",
    AnalyticsQueries = new Dictionary<GraphAnalyticsAlgorithm, string>
    {
        [GraphAnalyticsAlgorithm.PageRank] = "nodal_pagerank",
        [GraphAnalyticsAlgorithm.Louvain] = "nodal_louvain"
    }
};
```

Installed queries return `nodal_node` and `nodal_metrics` fields. Algorithms that are not configured are excluded from the provider capability set and fail before any HTTP request is made. Nodal never downloads a large graph to emulate an unsupported algorithm in application memory.

Weight support is algorithm-specific. Neo4j publishes it in each `GraphAlgorithmCapability`; TigerGraph requires the host to include compatible installed queries in `WeightedAnalyticsAlgorithms`. See [Compatibility and capabilities](../providers/compatibility.md) for version baselines, verification levels, and the provider matrix.

Reusable analytics shapes can be compiled once with `NodalCompiledAnalyticsQuery.Compile(...)`; `CreateCacheKey(...)` supplies a deterministic SHA-256 shape key for application caches.
