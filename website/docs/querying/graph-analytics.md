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

## Multi-relation analytics scopes

Centrality can describe one node network through several relationship families. The scope is immutable, homogeneous, and canonical: every relation connects the same node type, inclusion order does not change its binding identity, and the database provider performs the calculation.

```csharp
var influence = GraphAnalyticsScope.For<Author>("author-influence")
    .Include(context.CoAuthorships)
    .Include(context.SharedInterests);

var ranks = await context.Authors.Query()
    .Analyze(influence)
    .PageRank()
    .Top(20)
    .ToListAsync();
```

Nodal derives a versioned fingerprint from the algorithm, node type, relationship types, direction, weights, and coefficients. Neo4j uses it in an idempotent multi-relation GDS projection name. TigerGraph resolves the same shape through a verified installed-query binding. An unsupported semantic shape fails before database transport; Nodal does not silently switch to `Nodal.Analytics` or download the graph.

Per-relation coefficients and different weight-property names remain part of the portable contract but require a provider binding that declares those semantics. Neo4j's current native-projection path accepts unit coefficients and, when weighted, one common weight property across all relations. Nodal-managed TigerGraph generation currently accepts unweighted unit-coefficient PageRank; richer shapes use an explicitly verified binding.

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

TigerGraph algorithms execute through installed GSQL REST++ endpoints. The legacy single-relation map remains supported:

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

## Bounded observations and derived networks

`Nodal.Analytics` is intentionally a second analytics surface. It does not
replace provider-native algorithms for a database-resident graph. It operates
only after an explicitly bounded subgraph query has become a canonical
`GraphObservation`, which is useful when an application constructs a new
derived network that does not exist in Neo4j or TigerGraph.

```csharp
var request = new GraphObservationRequest(
    query.ToQueryModel() with { Projection = GraphQueryProjection.Subgraph },
    new GraphObservationOptions
    {
        MaxNodes = 5_000,
        MaxRelations = 20_000,
        NodeProperties = new HashSet<string>(["segment"], StringComparer.Ordinal),
        RelationProperties = new HashSet<string>(["observedAt"], StringComparer.Ordinal),
    },
    Timeout: TimeSpan.FromSeconds(30));

var source = new GraphQueryObservationSource(provider);
GraphObservation observation = await source.ObserveAsync(request, cancellationToken);

DerivedNetworkAnalysis result = GraphObservationNetworkAnalyzer.Analyze(
    observation,
    new DerivedNetworkAnalysisOptions
    {
        RelationTypes = new HashSet<string>(["FOLLOWED_BY"], StringComparer.Ordinal),
        TreatAsUndirected = false,
    });
```

The public baseline computes in/out/total degree, weakly connected components,
and deterministic PageRank. Server and client bounds, cancellation, timeout,
endpoint integrity, convergence, and relation selection are explicit. An
oversized or malformed observation fails; there is no silent partial result or
silent fallback from provider-native analytics.
