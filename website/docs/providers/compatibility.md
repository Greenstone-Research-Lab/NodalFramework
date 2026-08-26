---
title: Compatibility and capabilities
description: Version baselines, provider feature boundaries, analytics requirements, and verification status.
---

# Compatibility and capabilities

This page distinguishes three different claims:

- **client compatibility** comes from the database vendor;
- **implemented** means Nodal has a provider implementation and automated compiler or contract tests;
- **live verified** means the repository exercises the feature against the exact database baseline listed below.

Nodal does not infer that a feature exists merely because a driver can connect to a server version.

## Version policy

| Component | Nodal baseline | Vendor compatibility | Current verification |
| --- | --- | --- | --- |
| .NET | .NET 10 | Nodal packages currently target `net10.0` | Build, unit tests, package validation |
| Neo4j driver | `Neo4j.Driver` 6.3.0 | Driver 6.x supports Neo4j 4.4.x, 5.x, 2025.x, and 2026.x | Package and compiler tests |
| Neo4j database | Neo4j 5.26 Community | Wider connectivity follows the official driver policy | Live query, mutation, and rollback baseline; migration compiler/unit verified |
| Neo4j GDS | GDS 2.13 is the official match for Neo4j 5.26 | GDS must match the server according to Neo4j's compatibility matrix | Current procedure compiler verified; per-algorithm 2.13 live certification pending |
| TigerGraph | TigerGraph 4.2.4 Community | No wider Nodal compatibility promise yet | Live REST++ query/mutation plus GSQL migration apply, cleanup, restart, history, and revert baseline; analytics endpoint contract verified |

References: [Neo4j .NET Driver compatibility](https://neo4j.com/docs/dotnet-manual/current/install/) and [Neo4j–GDS compatibility matrix](https://neo4j.com/docs/graph-data-science/current/installation/supported-neo4j-versions/).

The Docker images in `compose.local.yml` are the executable source for the live QA versions. Supporting a newer database version requires updating that baseline and passing the live suite; changing only this table is not sufficient.

Dependabot checks NuGet, documentation npm packages, Docker database images, and GitHub Actions every week. Its pull requests target `developer`. An update PR is a notification, not an automatic compatibility promise: database baselines move only after the relevant compiler and live integration suites pass.

## Provider feature matrix

Legend: **Yes** is implemented; **Conditional** requires the condition shown; **No** fails explicitly.

| Capability | Neo4j | TigerGraph |
| --- | --- | --- |
| Parameterized node filtering and ordering | Yes, Cypher | Yes, interpreted GSQL |
| Fixed directed/undirected traversal | Yes | Yes |
| Variable-depth traversal | Yes | Conditional: GSQL Syntax V2 |
| Optional match | Yes | No |
| Vertex-simple variable-depth path | Yes | No: intermediate aliases are unavailable |
| Correlated existence pattern | Yes: Cypher `EXISTS` subquery | No: use a separately verified installed-query extension |
| Additional named required pattern | Yes: Cypher `MATCH` | No: use a separately verified installed-query extension |
| Provider-side scalar/aggregate rows | Yes | No: interpreted GSQL route rejects it before transport |
| Compatible node-query `Union` / `UnionAll` | Yes | No: use a separately verified installed-query extension |
| Client-managed multi-command transaction | Yes | No: request or installed-query boundary |
| Atomic mutation plan | Yes | Conditional: REST++ or installed mutation query |
| Migration execution | Yes | Conditional: verified administrative control plane and graph lock |
| Centrality/community analytics | Conditional: compatible GDS and named projection | Conditional: configured installed GSQL query |
| Weighted analytics | Algorithm-specific GDS capability | Explicitly declared per installed query |
| Analytics deployment discovery | Live GDS discovery with bounded cache | Configured installed-query snapshot |
| Analytics projection lifecycle | Yes: explicit create/reuse/drop | Not applicable; algorithms use the database graph |
| Typed shortest-path result | Yes: native Cypher | Conditional: configured installed GSQL query |
| Weighted shortest paths | Conditional: GDS Dijkstra, A*, Yen | Conditional: configured weighted installed query |

## Migration evolution matrix

Migration operations are analyzed before provider transport. A provider must
compile an operation explicitly; Nodal never emulates an unsupported change by
loading the graph into application memory.

| Operation | Neo4j | TigerGraph |
| --- | --- | --- |
| Add node/relation property | Native graph flexibility; reported as a warning with no DDL | ALTER VERTEX/EDGE ADD ATTRIBUTE |
| Drop node/relation property | Native graph flexibility; reported as a warning with no DDL | ALTER VERTEX/EDGE DROP ATTRIBUTE |
| Rename property | Warning-only on Neo4j; application data rewrite remains explicit | Unsupported; use an explicit provider backfill |
| Alter property type | Unsupported without an explicit backfill | Unsupported without an explicit backfill |
| Drop index | Typed Cypher DROP INDEX | Typed `ALTER VERTEX ... DROP INDEX` |
| Drop unique constraint | Typed Cypher DROP CONSTRAINT | Unsupported beyond primary IDs |
| Destructive operations | Require AllowDestructiveOperations = true | Require AllowDestructiveOperations = true |

Type rewrites and large data changes use the bounded backfill contract. Batch
size, continuation tokens, cancellation, retry, and recovery behavior must be
defined by the application/provider integration; no provider silently converts
or deletes persisted values.

## Analytics algorithm matrix

All algorithms below exist in the portable contract. “Compiler” does not mean that the optional database component has been installed or live-certified.

| Family | Algorithms | Neo4j | TigerGraph |
| --- | --- | --- | --- |
| Centrality | ArticleRank, articulation points, betweenness, bridges, CELF, closeness, degree, eigenvector, harmonic, HITS, PageRank | GDS stream compiler; deployment allow-list supported | Installed-query mapping; unavailable until configured |
| Community and cohesion | Clique counting, conductance, HDBSCAN, K-core, K-1 coloring, K-means, label propagation, Leiden, local clustering coefficient, Louvain, modularity, modularity optimization, SCC, triangle count, WCC, approximate maximum k-cut, SLLPA | GDS stream compiler; deployment allow-list supported | Installed-query mapping; unavailable until configured |
| Path finding | Unweighted shortest path, all shortest paths, Dijkstra, A*, Yen k-shortest paths | Native Cypher for unweighted paths; GDS compiler for weighted paths | Installed-query mapping with canonical route response |

Neo4j can restrict advertised algorithms to the procedures actually available in a deployment:

```csharp
var options = new Neo4jOptions
{
    Endpoint = new Uri("neo4j://localhost:7687"),
    Username = "neo4j",
    Password = "secret",
    GraphDataScienceEnabled = true,
    AnalyticsAlgorithms = new HashSet<GraphAnalyticsAlgorithm>
    {
        GraphAnalyticsAlgorithm.PageRank,
        GraphAnalyticsAlgorithm.Louvain
    }
};
```

Use `context.Database.GetAnalyticsRuntime().DiscoverAsync()` to read and cache `gds.list()`, `gds.version()`, and projection names. The deployment allow-list remains explicit so a newly installed server procedure cannot silently expand application permissions.

TigerGraph requires both the installed-query mapping and an explicit declaration for queries that accept weights:

```csharp
var options = new TigerGraphOptions
{
    Endpoint = new Uri("https://example.i.tgcloud.io/"),
    AccessToken = "secret-token",
    AnalyticsQueries = new Dictionary<GraphAnalyticsAlgorithm, string>
    {
        [GraphAnalyticsAlgorithm.PageRank] = "nodal_pagerank"
    },
    WeightedAnalyticsAlgorithms = new HashSet<GraphAnalyticsAlgorithm>
    {
        GraphAnalyticsAlgorithm.PageRank
    }
};
```

An unsupported algorithm or semantic option is rejected before database transport. Nodal never downloads the graph to simulate missing server functionality.
