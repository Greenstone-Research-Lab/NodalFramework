# P1: Graph analytics foundation

## Objective

P1 adds graph-native analytics without folding provider-specific algorithms into the ordinary node query model. The first vertical slice is an unweighted shortest-path operation that starts and ends with strongly typed `GraphQuery<TNode>` selectors.

The intended application experience is:

```csharp
GraphRoute<Person, Knows> route = await context.People
    .Match(person => person.Id == "person-a")
    .ShortestPathTo(
        context.People.Match(person => person.Id == "person-b"),
        context.Friendships)
    .MaxDepth(8)
    .SingleAsync();
```

This preserves the existing `Match` expression translator, mapped property names, parameterization, cancellation, and provider selection. It does not require application code to construct database-specific vertex identifiers.

## Architectural boundary

Analytics uses a dedicated model and provider contract:

```text
GraphQuery<TNode> source + GraphQuery<TNode> target
                       |
                       v
          GraphShortestPathQueryModel
                       |
                       v
       IGraphAnalyticsCompiler / Executor
                       |
              +--------+--------+
              |                 |
           Neo4j              TigerGraph
```

`GraphQueryModel` remains responsible for record-oriented matching and traversal. Analytics models describe algorithm intent, limits, weights, and result shape. This separation prevents centrality, community detection, and path algorithms from accumulating unrelated optional fields in the core query record.

Providers opt into analytics through a segregated capability interface. A read/write provider remains valid without analytics support. Unsupported algorithms or semantics fail before transport execution; Nodal must not silently download a graph and emulate them in memory.

## Canonical result

The initial result is `GraphRoute<TNode, TRelation>`:

- ordered `Nodes`;
- ordered `Relations` connecting adjacent nodes;
- `HopCount` derived from relationships;
- optional `TotalCost` reserved for a later weighted-path slice.

Materialized route members use the context identity map by default. An analytics query can opt out through `AsNoTracking`. Provider-native identities are retained internally so parallel edges are not collapsed.

## Provider strategy

### Neo4j

The first slice compiles to parameterized Cypher using native shortest-path semantics. It returns `nodes(path)`, `relationships(path)`, and `length(path)` for normalization. Weighted algorithms must not assume the separately installed Graph Data Science plugin; GDS-dependent operations will have an explicit capability and configuration boundary.

### TigerGraph

Shortest path execution uses a deterministic installed GSQL query through `ITigerGraphAdministrativeTransport`, following the existing transactional mutation-query lifecycle. The query is installed once per graph and operation shape, then invoked through REST++. Environments without an administrative transport report the capability as unavailable. Nodal does not invent an undocumented administrative endpoint.

## Delivery slices

1. **Contracts and validation**
   - shortest-path model, route result, options, capabilities, compiler and executor interfaces;
   - immutable validation for source/target types, edge compatibility, depth, and tracking;
   - unit and architecture tests.
2. **Neo4j vertical slice**
   - Cypher compiler, normalized path response, materialization, cancellation, and compiler tests;
   - live Bolt integration test.
3. **TigerGraph vertical slice**
   - deterministic GSQL definition and installation lifecycle;
   - REST++ execution, canonical normalization, reuse tests, and live integration test.
4. **Weighted paths**
   - mapped edge-weight selector, numeric validation, total cost, and explicit provider capabilities.
5. **Additional algorithms**
   - scored node results for centrality/PageRank;
   - community membership results;
   - algorithm-specific options rather than one universal property bag.

## Quality gates

- New Core product code maintains at least 95% line coverage.
- Every provider compiler has exact command and parameter tests.
- Unsupported capability tests prove that no transport call occurs.
- Live tests cover one reachable path, one unreachable target, cancellation, and provider error normalization.
- Public members include professional XML documentation and executable usage examples where appropriate.
- Architecture tests keep Core independent from Neo4j, TigerGraph, and transport packages.

## Non-goals for the first slice

- client-side graph algorithms;
- automatic GDS installation;
- one abstraction that pretends all provider algorithms have identical semantics;
- centrality and community execution before the shortest-path result and capability contracts are stable.
