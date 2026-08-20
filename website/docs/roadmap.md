---
title: Roadmap
description: Delivered foundations, the P2 hardening backlog, and the P3 Pattern Recognition program for Nodal Framework.
---

# Roadmap

Nodal is developed in explicit priority rounds. A round describes an intended
outcome, not a compatibility or release-date promise. Shipped behavior remains
defined by the provider compatibility matrix and package release notes.

## Delivered foundations

P0 delivered the query, tracking, mutation, and migration foundation. P1
delivered typed routes, centrality and community contracts, native Neo4j
shortest paths, weighted GDS path compilers, TigerGraph installed-query
execution, runtime capability discovery, Neo4j projection lifecycle management,
typed options, and compiled analytics factories.

## P2 - production hardening

- Certify Neo4j GDS and TigerGraph installed-query behavior against explicit
  server versions in live CI.
- Harden public APIs through real application samples and cross-provider
  compatibility tests.
- Expand migration diffing, rollback planning, history, and lifecycle tooling.
- Define analytics write and mutate semantics without bypassing tracking and
  unit-of-work guarantees.
- Add event-driven mutation, outbox, idempotency, and delivery integration
  points.
- Publish versioned capability metadata for documentation, tools, and coding
  agents.
- Establish versioned documentation and a stable package promotion policy.

## P3 - Nodal.PatternRecognition alpha

P3 introduces an optional package for discovering repeated structures and
temporal behavior without changing the responsibilities of `Nodal.Core`.

### Foundation

1. Record the package boundaries and execution architecture in an ADR.
2. Create the `Nodal.PatternRecognition` package and dependency rules.
3. Define canonical path, pattern, observation, and result contracts.
4. Define event time, observation time, validity time, sessions, and windows.
5. Add a provider-neutral pattern DSL and canonical analysis plan.
6. Enforce bounded extraction, path budgets, cancellation, and sampling.

### Discovery

7. Implement canonical path signatures and stable hashing.
8. Implement typed edit-distance and n-gram similarity kernels.
9. Implement weighted heterogeneous PathSim/HeteSim-style similarity.
10. Build a sparse top-k similarity graph without an all-pairs materialization.
11. Detect path communities and select representative paths.
12. Mine bounded frequent path and event-sequence patterns.

### Temporal and provider execution

13. Build directed temporal transition graphs with windows and time decay.
14. Add Neo4j planning and native analytics pushdown where semantics match.
15. Add TigerGraph planning and GSQL pushdown where semantics match.
16. Persist versioned patterns, evidence, scores, and explanations explicitly.

### Productization

17. Add property, differential, live-provider, performance, and load tests.
18. Publish benchmarks, an e-commerce showcase, documentation, and the first
    alpha package.

## Provider expansion

- `Nodal.Memgraph`: first candidate because local Docker, Bolt, and Cypher make
  an early compatibility slice practical. It receives its own capability
  profile; Neo4j compatibility is never assumed implicitly.
- `Nodal.ArangoDB`: second candidate because HTTP and AQL exercise a genuinely
  different transport, graph model, traversal compiler, and transaction model.
  The first slice targets bounded query, traversal, materialization, and live
  Docker QA before mutations and migrations.

Additional providers are selected only after a transport, query-language,
transaction, migration, local-QA, and maintenance assessment.

## P4+ - research horizon

- Full frequent-subgraph mining and closed/maximal pattern reduction.
- Overlapping and evolutionary community detection.
- Concept-drift detection and automatic window adaptation.
- Learned path embeddings and approximate vector indexes.
- Probabilistic next-event and temporal point-process models.
- Temporal graph neural networks and online model training.
- Model registry, evaluation, rollback, bias, and drift governance.

The dated execution plan is published in
[the Nodal Journal](/blog/alpha-roadmap-pattern-recognition-and-new-providers).
Track implementation status in the
[GitHub repository](https://github.com/Greenstone-Research-Lab/NodalFramework).
