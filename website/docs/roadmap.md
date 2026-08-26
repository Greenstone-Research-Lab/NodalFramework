---
title: Roadmap
description: Delivered foundations, the beta package correction, the P2 hardening backlog, and the P3 analytics program for Nodal Framework.
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

## Completed - analytics package boundary correction

1. Defined `Nodal.Analytics` as the public analytics contract boundary before
   the beta line. Its scope is provider-neutral capability metadata and the
   integration of analytics executed by supported graph platforms. Advanced
   implementations are not part of the public repository or compatibility
   promise.
## Completed - release quality and migration foundation

1. Added SonarCloud analysis to the protected CI path. Pull requests now publish
   the quality result beside formatting, tests, coverage, package validation, and
   live provider smoke checks.
2. Completed the M0–M5 migration program: portable contracts and preflight,
   durable history and recovery, schema snapshots and deterministic diffs,
   Neo4j and TigerGraph execution hardening, immutable migration bundles, and
   the provider-neutral `nodal` CLI workflow.
3. Published the migration guide, operational recovery boundaries, deployment
   host examples, and the first release-quality evidence artifacts. Provider
   migration behavior remains certified independently; portability never means
   that native transaction or administrative semantics are silently emulated.

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

## Advanced analytics

Nodal's public analytics surface remains limited to provider-neutral contracts,
capability metadata, and the integration of analytics executed by supported
graph platforms. Advanced analytics products are developed and licensed
separately. Their implementation scope, performance characteristics, and
roadmap are intentionally not part of this public compatibility promise.

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

Track public implementation status in the
[GitHub repository](https://github.com/Greenstone-Research-Lab/NodalFramework).
