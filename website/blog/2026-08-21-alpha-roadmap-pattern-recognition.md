---
slug: alpha-roadmap-pattern-recognition-and-new-providers
title: "Alpha roadmap: pattern recognition and the next providers"
authors: [ilker]
tags: [roadmap, pattern-recognition, performance, providers]
description: The P2, P3, and P4+ plan for Nodal Framework, including a performance-first similarity engine and the Memgraph and ArangoDB provider tracks.
image: /img/journal/default-cover.svg
image_alt: Abstract connected graph illustrating paths, similarity links, and provider expansion.
image_caption: Nodal's alpha roadmap separates production hardening, pattern discovery, and longer-term learning research.
---

Nodal Framework is still an alpha, which is exactly the right time to test its
most ambitious architectural claim: a provider-neutral graph model can support
portable intelligence without flattening the native strengths of each graph
engine.

P2 remains the production-hardening track. P3 starts the optional
`Nodal.PatternRecognition` package. P4+ records the research horizon so
experimental ideas do not silently become compatibility promises.

<!-- truncate -->

## What remains in P2

P2 will certify live analytics versions, harden application samples and
compatibility tests, expand migration lifecycle tooling, define analytics
write-back semantics, add event-driven mutation and outbox integration, publish
machine-readable capabilities, and establish stable documentation and package
promotion.

These tasks are prerequisites for Pattern Recognition. Temporal learning needs
reliable events. Persisted discoveries need explicit write semantics. Native
acceleration needs versioned provider capabilities.

## What P3 will deliver

P3 contains eighteen work items across four slices:

1. Package architecture, canonical contracts, temporal semantics, the analysis
   DSL, and bounded extraction.
2. Path signatures, heterogeneous similarity, sparse top-k similarity graphs,
   communities, and frequent sequences.
3. Directed temporal transition graphs, provider pushdown, and versioned pattern
   evidence.
4. Tests, benchmarks, an e-commerce showcase, documentation, and the first
   alpha package.

The first release will discover repeated path families and temporal transitions.
It will not claim causal inference, unrestricted subgraph mining, or a trained
temporal neural network.

## Performance-first similarity

A naive comparison of every path with every other path is quadratic. One
million paths would imply roughly one trillion pair comparisons before useful
filtering. P3 therefore treats candidate generation as part of the public
execution semantics, not as an invisible implementation detail.

The engine will use three lanes:

### Exact structural lane

Canonical node labels, relationship types, direction, bounded property buckets,
and temporal buckets form a stable path signature. Hashing and grouping discover
exact repeats in linear time relative to the encoded path volume.

### Sparse heterogeneous lane

Typed edge and node n-grams form sparse weighted features. An inverted index,
length buckets, endpoint types, and MinHash/LSH-style candidate generation avoid
an all-pairs scan. Exact weighted Jaccard, typed edit distance, PathSim, or
HeteSim-style scoring is applied only to candidates.

### Dense semantic lane

Optional embeddings support semantic similarity through a top-k approximate
nearest-neighbor index. This lane remains replaceable and opt-in; canonical
exact results must never depend on a particular model or vector index.

All lanes produce a sparse similarity graph. Community detection runs over that
graph rather than a dense similarity matrix. Every result records the metric,
weights, candidate strategy, cutoff, provider execution, and evidence needed to
explain or reproduce it.

Initial performance gates are:

- no unbounded all-pairs execution;
- deterministic exact signatures and scores;
- explicit memory, path-count, hop, time-window, and candidate budgets;
- streaming and cancellation throughout extraction and scoring;
- allocation, throughput, latency, and peak-memory benchmarks;
- recall-at-k reporting whenever an approximate candidate/index strategy is
  compared with an exact baseline;
- identical canonical results across providers when their declared semantics
  match.

Native graph operations will be pushed down only when the provider can preserve
the requested semantics. The portable .NET engine handles the remaining
canonicalization, candidate refinement, evidence, and explanation stages.

## The next provider tracks

`Nodal.Memgraph` is the first expansion candidate. Memgraph supports Cypher and
Bolt-compatible clients, runs locally, and offers graph analytics through MAGE.
That makes it a fast provider slice, but not a Neo4j alias: dialect, procedure,
transaction, and analytics capabilities will be certified independently.

`Nodal.ArangoDB` is the second candidate. Its HTTP transport, AQL traversal
language, named graphs, document/edge collections, and transaction behavior
exercise different parts of Nodal's abstraction. ArangoDB's official driver
catalog does not currently include .NET, so Nodal will begin with a narrow,
testable HTTP transport rather than adopting an unowned general-purpose client.

References:

- [Memgraph](https://memgraph.com/)
- [ArangoDB AQL graph traversals](https://docs.arango.ai/arangodb/stable/aql/graph-queries/traversals/)
- [ArangoDB official driver catalog](https://docs.arango.ai/ecosystem/drivers/)

## Schedule: August 24-30, 2026

| Date | Focus | Exit condition |
| --- | --- | --- |
| Monday, August 24 | Convert P2, P3, and P4+ into GitHub issues and dependency links | Every task has scope, acceptance criteria, and an owner-ready label |
| Tuesday, August 25 | Pattern Recognition ADR, package boundaries, and canonical contracts | Architecture and dependency direction are reviewable |
| Wednesday, August 26 | Exact signatures, sparse feature encoding, and benchmark harness | Exact baseline and representative datasets run locally |
| Thursday, August 27 | Candidate generation and heterogeneous similarity spike | No all-pairs materialization; recall and throughput are measurable |
| Friday, August 28 | `Nodal.Memgraph` provider scaffold and live Docker smoke slice | Connection, bounded query, and normalized result smoke tests pass |
| Saturday, August 29 | `Nodal.ArangoDB` transport/compiler scaffold | Authenticated HTTP health check and parameterized AQL query slice pass |
| Sunday, August 30 | QA review, benchmark report, documentation, and next-round decision | Results, limitations, and follow-up tasks are published |

This is an alpha execution calendar, not a promise that two providers and a new
analytics product reach feature parity in seven days. The week's goal is to
establish tested vertical slices and use measured evidence to determine the next
implementation round.

## Beyond P3

P4+ keeps full frequent-subgraph mining, overlapping and evolutionary
communities, concept drift, learned path embeddings, probabilistic next-event
models, temporal point processes, temporal graph neural networks, and model
governance outside the first package contract.

The distinction matters: P3 must produce a fast, explainable, provider-aware
engine before P4 teaches it more sophisticated models.
