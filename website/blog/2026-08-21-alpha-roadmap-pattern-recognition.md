---
slug: alpha-roadmap-pattern-recognition-and-new-providers
title: "Alpha roadmap: pattern recognition and the next providers"
authors: [ilker]
tags: [roadmap, pattern-recognition, performance, providers]
description: The P2, P3, and P4+ plan for Nodal Framework, including a performance-first similarity engine and the Memgraph and ArangoDB provider tracks.
image: /img/journal/pattern-recognition-analytics-shell.png
image_alt: Two provider graph streams rise into a shared analytics shell where paths form communities and temporal transitions.
image_caption: Nodal.PatternRecognition sits above providers as an optional analytics shell, turning canonical paths into explainable discoveries.
---

Nodal Framework is still an alpha, which is exactly the right time to test its
most ambitious architectural claim: a provider-neutral graph model can support
portable intelligence without flattening the native strengths of each graph
engine.

P2 remains the production-hardening track. P3 starts the optional
`Nodal.PatternRecognition` package. P4+ records the research horizon so
experimental ideas do not silently become compatibility promises.

`Nodal.PatternRecognition` is not another database provider. It is an optional
**analytics shell above every provider**: Neo4j, TigerGraph, and future engines
keep their own query languages, transports, and native accelerators, while the
shell consumes Nodal's canonical nodes, relationships, paths, events, and
capability declarations. Provider pushdown is an optimization; the analysis
contract and its evidence remain portable.

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

![Directed graph paths are encoded into typed multi-hot bitsets, compared through XOR and intersection kernels, then assembled into similarity communities.](/img/journal/pattern-recognition-similarity-lab.png)

<small><em>The first experiment separates encoding, exact bitset kernels, candidate filtering, and community construction so every performance claim remains measurable.</em></small>

A naive comparison of every path with every other path is quadratic. One
million paths would imply roughly one trillion pair comparisons before useful
filtering. P3 therefore treats candidate generation as part of the public
execution semantics, not as an invisible implementation detail.

The engine will use three lanes:

### Exact structural lane

Canonical node labels, relationship types, direction, bounded property buckets,
and temporal buckets form a stable path signature. Hashing and grouping discover
exact repeats in linear time relative to the encoded path volume.

For pairwise structural comparison, those features are also encoded as typed
multi-hot bitsets. A path is not one category, so a single one-hot value would
be insufficient. Separate feature families preserve meaning:

- node-label and relationship-type presence;
- direction-aware relationship types;
- position-aware `(position, type)` tokens;
- typed path n-grams;
- bounded property and temporal buckets.

The bitsets make several exact binary metrics inexpensive:

```text
difference      = popcount(A XOR B)
intersection    = popcount(A AND B)
union           = popcount(A OR B)
hamming         = difference / featureCount
jaccard         = intersection / union
binary cosine   = intersection / sqrt(popcount(A) * popcount(B))
```

Presence-only bitsets are fast but discard order. Position-aware and n-gram
families retain local order, while typed edit distance remains the exact fallback
for variable-length sequences where shifting one hop should not invalidate every
following position.

### Sparse heterogeneous lane

Typed edge and node n-grams form sparse weighted features. An inverted index,
length buckets, endpoint types, and MinHash/LSH-style candidate generation avoid
an all-pairs scan. Exact weighted Jaccard, typed edit distance, PathSim, or
HeteSim-style scoring is applied only to candidates.

Heterogeneous scoring keeps each feature family separate and combines normalized
scores with explicit weights. This prevents a high-cardinality property family
from overwhelming relationship direction or temporal shape. Alpha weights are
configuration, recorded with every result, and never learned silently.

### Dense semantic lane

Optional embeddings support semantic similarity through a top-k approximate
nearest-neighbor index. This lane remains replaceable and opt-in; canonical
exact results must never depend on a particular model or vector index.

Dense vectors use cosine or dot-product kernels over contiguous spans. Sparse
and dense representations therefore coexist: bitsets answer exact structural
questions, while vectors retrieve semantically close candidates.

### .NET execution paths

The portable kernel stores dense bitsets as contiguous `ulong` words and exposes
read-only span-based operations. The baseline uses `BitOperations.PopCount`,
which uses hardware intrinsics when the runtime and processor support them.
Candidate vector paths use the widest supported `Vector512`, `Vector256`, or
`Vector128` bitwise operations and retain a scalar fallback.

Vector width alone is not accepted as proof of speed. Popcount reduction,
short-buffer overhead, alignment, density, and CPU architecture can make a
narrower or scalar loop faster. Every execution path must return the same score
and earn its dispatch threshold through benchmarks. Dense floating-point vector
scoring is benchmarked separately from binary bitset scoring.

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

The benchmark matrix covers:

| Dimension | Initial values |
| --- | --- |
| Feature width | 256, 1,024, 4,096, and 16,384 bits |
| Feature density | 0.5%, 5%, 25%, and 50% |
| Path length | 2, 4, 8, and 16 hops |
| Corpus size | 10,000, 100,000, and 1,000,000 paths |
| Candidate count | exact all-pairs only for oracle-sized data; top-k candidate budgets for scale |
| CPU path | scalar, unrolled popcount, Vector128, Vector256, and Vector512 when supported |

Every optimized result is checked against a simple scalar oracle. Published
results report comparisons per second, nanoseconds per comparison, allocation,
bytes per path, index-build time, peak memory, top-k latency, and recall-at-k for
approximate candidate strategies. Sparse sorted integers and a compressed bitmap
candidate are benchmarked alongside dense bitsets so low-density paths do not
pay for thousands of zero words.

Native graph operations will be pushed down only when the provider can preserve
the requested semantics. The portable .NET engine handles the remaining
canonicalization, candidate refinement, evidence, and explanation stages.

## First experiment: scalar wins the opening round

On August 21, 2026, the first executable P3 slice compared scalar, manually
unrolled, and Vector256 kernels on .NET 10 using 256, 4,096, and 16,384-bit
vectors at 5% and 25% density. All kernels returned identical exact scores and
allocated zero bytes per comparison.

The scalar `BitOperations.PopCount` loop was fastest in every measured case:
approximately 8-10 ns at 256 bits, 66-74 ns at 4,096 bits, and 202-209 ns at
16,384 bits. Manual unrolling was 1.2-2.4 times slower; the first Vector256
lane-reduction candidate was 1.4-2.5 times slower. The result makes the scalar
loop the initial production kernel and keeps SIMD as an evidence-gated research
track rather than a marketing assumption.

The [reproducible benchmark summary](https://github.com/Greenstone-Research-Lab/NodalFramework/blob/developer/benchmarks/results/first-bitset-similarity.md)
records the machine, runtime, full matrix, limitations, and command.

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
