---
title: Pattern Recognition analytics shell
description: Understand how Nodal.Analytics adds portable path similarity and pattern discovery above graph database providers.
---

# Pattern Recognition analytics shell

`Nodal.Analytics` is the provider-neutral analytics layer of Nodal
Framework. It sits above `Nodal.Core` and provider packages, consumes canonical
graph results, and produces explainable similarity, community, sequence, and
temporal-transition evidence.

:::caution Experimental P3 surface

The package is under active alpha development. The bitset similarity kernel is
the first executable slice; path extraction, candidate generation, community
detection, and temporal APIs will follow behind explicit limits and benchmarks.

:::

![Neo4j and TigerGraph streams rise through a canonical graph membrane into path communities and directed temporal transitions.](/img/journal/pattern-recognition-analytics-shell.png)

The dependency and data flow runs upward: **providers → canonical Nodal graph
results → Pattern Recognition analytics shell → application evidence**. Optional
native pushdown travels back down as a capability-checked optimization.

## Why it is a shell

The database still owns storage, indexes, transactions, and native query
execution. Pattern Recognition surrounds those capabilities with a portable
analysis pipeline:

1. Extract bounded canonical paths through any provider.
2. Encode typed structural, positional, property, and temporal features.
3. Generate a bounded candidate set instead of materializing all pairs.
4. Score candidates with exact bitsets, sparse features, or optional vectors.
5. Build similarity communities and directed temporal transitions.
6. Return evidence, configuration, provider execution, and version metadata.

Provider-native algorithms can accelerate a stage only when the capability
matrix certifies equivalent semantics. They never redefine the portable result
silently.

## First executable experiment

The first kernel packs typed multi-hot features into contiguous `ulong` words.
It computes XOR difference, AND intersection, OR union, normalized Hamming,
Jaccard, and binary cosine scores without allocating during comparison. A scalar
oracle validates unrolled and hardware-vector candidates across feature widths
and densities before any runtime dispatcher is selected.

See the [P3 Journal entry](/blog/alpha-roadmap-pattern-recognition-and-new-providers)
for the benchmark design and [roadmap](../roadmap) for the remaining slices.
