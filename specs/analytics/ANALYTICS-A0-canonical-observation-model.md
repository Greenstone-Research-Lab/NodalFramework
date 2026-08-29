---
id: ANALYTICS-A0
title: Canonical analytics observation model
status: draft
type: feature
owners: Nodal maintainers
last-reviewed: 2026-08-29
---

# ANALYTICS-A0 — Canonical analytics observation model

## Objective and user value

Define the provider-neutral observation contract consumed by public analytics
without exposing provider payloads or private pattern-recognition techniques.
Applications should request bounded graph observations once and pass a stable,
typed representation to analytics components regardless of Neo4j or TigerGraph.

## Non-goals

- Implement similarity, pattern recognition, narrative generation, or premium
  research models in this public repository.
- Emulate provider algorithms client-side when semantic parity is uncertain.
- Define cloud tenancy, billing, or dedicated execution infrastructure.

## Terminology and invariants

An observation is a bounded, immutable snapshot of node identities, labels,
relation identities, directions, timestamps, and explicitly selected
properties. Identity and edge direction are preserved. Parallel edges are not
collapsed unless the request explicitly selects that operation.

## API and usage examples

The final API will accept a provider-neutral request containing projection,
filter, traversal, ordering, and limit information and return an immutable
observation. The implementation specification must include compile-safe C#
examples before this document can become accepted.

## Provider and version scope

Neo4j and TigerGraph are the initial providers. Acceptance requires a versioned
capability statement for each provider and explicit unsupported behavior when
either cannot preserve the observation invariants.

## Architecture and dependencies

Contracts belong in the public analytics boundary and depend inward on
`Nodal.Core` abstractions. Provider compilation remains in provider packages.
Advanced analysis implementations remain outside this public repository.

## Operational behavior

Requests are bounded, cancellable, and safe to retry only when read-only.
Cancellation propagates to the provider transport. Partial results are not
returned as successful observations. Concurrency and ordering semantics must be
declared before acceptance.

## Security and privacy

Properties are opt-in, diagnostics exclude values by default, and credentials
never enter the observation. The contract must support data minimization and
tenant isolation at the application boundary.

## Verification strategy

Acceptance requires unit tests for invariants, shared provider contract tests,
provider compiler tests, live version-gated integration tests, cancellation and
limit tests, architecture rules, and at least 95% line coverage for added
product code.

## Performance budget

The accepted revision will define maximum default result size, allocation
targets for materialization, and reproducible provider-specific latency
baselines. Unbounded extraction is prohibited.

## Delivery impact

No package or API is changed by this draft. Acceptance will identify package
ownership, compatibility impact, documentation pages, migration implications,
and release-note entries.

## Acceptance criteria and evidence

- [ ] Compile-safe public API examples are approved.
- [ ] Neo4j and TigerGraph capability/version contracts are linked.
- [ ] Cancellation, ordering, parallel-edge, and partial-result semantics are explicit.
- [ ] Security, performance, test, package, and documentation evidence locations are defined.
