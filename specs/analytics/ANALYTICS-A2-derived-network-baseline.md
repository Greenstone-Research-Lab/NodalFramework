---
id: ANALYTICS-A2
title: Bounded derived-network baseline analytics
status: implemented
type: feature
owners: Nodal maintainers
last-reviewed: 2026-08-30
---

# ANALYTICS-A2 — Bounded derived-network baseline analytics

## Objective and user value

Compute transparent degree, weak-component, and PageRank evidence when users
intentionally derive a bounded network from a canonical `GraphObservation`.
This API does not replace Neo4j or TigerGraph native analytics for
database-resident graphs and is never a silent capability fallback.

## Non-goals

- Download an unbounded database graph.
- Emulate a missing provider capability.
- Implement proprietary similarity, motif, or narrative intelligence.

## Terminology and invariants

Included relations form the derived network. Weak components ignore direction;
degree and PageRank honor direction unless undirected treatment is explicit.

## API and usage examples

```csharp
DerivedNetworkAnalysis evidence = GraphObservationNetworkAnalyzer.Analyze(observation);
```

## Provider and version scope

Provider neutral and valid only for already normalized, bounded observations.

## Architecture and dependencies

Implementation lives in `Nodal.Analytics` and depends inward on canonical
observation contracts. Providers never depend on it.

## Operational behavior

Callers may select relation types and explicitly treat directions as
undirected. PageRank has finite iteration, damping, and convergence budgets.
Output preserves observation node order and includes the executed iteration
count and convergence state.

## Security and privacy

The analyzer performs no I/O. Callers own observation redaction and retention.

## Verification strategy

Tests cover direction, type filtering, isolated nodes, weak components,
convergence, exhausted iteration budgets, empty input, and invalid options.

## Performance budget

Memory is proportional to bounded nodes and relations. PageRank is limited by
the explicit iteration budget and never expands the source graph.

## Delivery impact

Adds an optional public baseline API to `Nodal.Analytics`; provider behavior is
unchanged.

## Acceptance criteria and evidence

- [x] Specification accepted.
- [x] Deterministic degree, weak-component, and PageRank metrics implemented.
- [x] Bounds, selection, direction, convergence, and negative cases tested.
- [x] Repository quality gates pass.
