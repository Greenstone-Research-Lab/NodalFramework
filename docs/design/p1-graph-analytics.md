# P1: Graph analytics foundation

## Objective

P1 adds graph-native analytics without mixing provider-specific algorithms into ordinary node queries. It includes typed paths, centrality and community detection, provider capability metadata, Neo4j GDS compilation, and TigerGraph installed-query endpoints.

```csharp
GraphRoute<Person, Knows> route = await context.People
    .Match(person => person.Id == "person-a")
    .ShortestPathTo(
        context.People.Match(person => person.Id == "person-b"),
        context.Friendships)
    .MaxDepth(8)
    .SingleAsync();
```

The API reuses mapped property names, expression translation, parameterization, cancellation, tracking, and provider selection. Application code does not construct database-specific vertex identifiers.

## Architectural boundary

```text
GraphQuery<TNode> source + GraphQuery<TNode> target
                       |
                       v
          GraphAnalyticsQueryModel
                       |
                       v
       IGraphAnalyticsCompiler / Executor
                       |
              +--------+--------+
              |                 |
           Neo4j              TigerGraph
```

`GraphQueryModel` remains responsible for record queries and traversal. Analytics models describe algorithm intent, limits, weights, projection, endpoint selectors, and result shape. Providers opt in through segregated capability and runtime interfaces. Unsupported algorithms fail before transport; Nodal never downloads a graph to emulate them in memory.

## Canonical results

`GraphRoute<TNode, TRelation>` contains ordered nodes, ordered connecting relationships, a derived hop count, and optional total cost. `GraphAnalyticsRecord<TNode>` associates a materialized node with provider-neutral metrics while preserving algorithm-specific fields.

Materialized nodes and relationships use the context identity map unless no-tracking is selected. Provider-native relationship identities are retained so parallel edges are not collapsed.

## Provider strategy

### Neo4j

Unweighted shortest and all-shortest paths compile to parameterized native Cypher. Dijkstra, A*, Yen, centrality, and community operations compile to GDS stream procedures and remain behind explicit capability configuration. The analytics runtime discovers and caches GDS version, procedures, and projections and manages projection create/reuse/drop explicitly.

### TigerGraph

Analytics execute through explicitly configured installed GSQL REST++ endpoints. The operator owns schema-specific query definitions and installation through a supported administrative channel. Nodal transports typed endpoint parameters and normalizes the canonical response. It does not invent an undocumented administrative endpoint or install an unverified algorithm template.

## Delivered slices

1. **Contracts and validation — complete**
   - shortest-path model, typed routes, full centrality/community taxonomy and detailed capabilities;
   - endpoint, edge compatibility, depth, numeric weight, tracking, and unsupported-provider validation;
   - typed PageRank and Louvain options plus an explicit provider-extension escape hatch.
2. **Neo4j vertical slice — complete at compiler and contract level**
   - native and GDS path compilers, normalized paths, identity-map materialization and cancellation;
   - GDS centrality/community compilers and deployment allow-lists;
   - projection lifecycle and bounded runtime discovery cache.
3. **TigerGraph vertical slice — complete for installed-query deployments**
   - installed-query mapping, source/target/depth transport and canonical analytics/route normalization;
   - configured capability discovery and pre-transport rejection of unavailable semantics.
4. **Reusable execution — complete**
   - compiled analytics factories and deterministic SHA-256 expression-shape keys;
   - runnable native shortest-path and opt-in PageRank/Louvain samples for both providers.

## P1 closure and post-P1 certification

The portable contracts, provider compilers, runtime discovery, projection lifecycle, route normalization, typed options, samples, and documentation are complete. Neo4j's environment-gated live suite covers reachable, unreachable, and cancelled native path execution.

Per-version live GDS and TigerGraph installed-query certification remains an ongoing QA matrix because those components are optional. A future TigerGraph query generator must prove its schema constraints and output contract across the supported version matrix before it can replace operator-owned installed queries.

## Quality gates

- Core and provider product code maintains at least 95% line coverage.
- Provider compilers have exact command and parameter tests.
- Unsupported capability tests prove no transport call occurs.
- Public APIs include professional XML documentation and executable examples.
- Architecture tests keep Core independent from provider and transport packages.

## Non-goals

- client-side graph algorithms;
- automatic GDS installation;
- pretending provider algorithms have identical semantics;
- analytics mutate/write modes before their unit-of-work semantics are designed.
