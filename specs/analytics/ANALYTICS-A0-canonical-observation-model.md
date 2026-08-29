---
id: ANALYTICS-A0
title: Canonical analytics observation model
status: accepted
type: feature
owners: Nodal maintainers
last-reviewed: 2026-08-29
---

# ANALYTICS-A0 — Canonical analytics observation model

## Objective and user value

Define the provider-neutral snapshot consumed by public analytics without
exposing provider payloads or private pattern-recognition techniques. An
application can convert a normalized provider result into one bounded,
immutable representation and pass that representation between analytics
components without depending on Neo4j, TigerGraph, Cypher, GSQL, or a provider
SDK.

The first vertical slice owns normalization after provider execution. Provider
query planning and transport adapters are delivered by later specifications so
this contract can remain small and independently testable.

## Non-goals

- Implement similarity, pattern recognition, narrative generation, embeddings,
  or premium research models in this public repository.
- Compile or execute provider queries in the first vertical slice.
- Emulate provider algorithms client-side when semantic parity is uncertain.
- Include scalar aggregates, analytics rows, or route collections in the first
  observation format.
- Define cloud tenancy, billing, or dedicated execution infrastructure.

## Terminology and invariants

An **observation** is a self-contained snapshot of nodes and directed
relationships. A **key** is the invariant, typed representation of a provider
identity. A **projection** is the explicit set of properties permitted to enter
the observation.

- Node identity is the combination of node type and canonical key.
- Relationship identity is the combination of relationship type and canonical
  key.
- Relationship direction is always `Source -> Target`.
- Every relationship endpoint must exist in the same observation.
- Duplicate node or relationship identities are rejected.
- Input order is preserved for both nodes and relationships.
- Parallel relationships are preserved when their identities differ.
- Property names use ordinal comparison and property values are recursively
  copied into read-only collections.
- An empty projection includes no properties. Properties are never included
  implicitly.
- Exceeding a configured bound fails the entire materialization. No partial
  observation is returned as success.

## API and usage examples

The public API belongs to `Nodal.Analytics.Observations`:

```csharp
var options = new GraphObservationOptions
{
    MaxNodes = 1_000,
    MaxRelations = 5_000,
    NodeProperties = new HashSet<string>(StringComparer.Ordinal)
    {
        "name",
        "category",
    },
    RelationProperties = new HashSet<string>(StringComparer.Ordinal)
    {
        "orderedAt",
    },
};

GraphObservation observation = GraphObservationMaterializer.Materialize(
    providerResult,
    options);
```

`GraphObservationOptions` defaults to 10,000 nodes, 50,000 relationships, and
empty property projections. `GraphObservationMaterializer` accepts only the
provider-neutral `GraphQueryResult` defined by `Nodal.Core`.

Canonical keys support string, GUID, signed and unsigned integer identities.
Unsupported identity types fail with a descriptive exception instead of using
locale-sensitive `ToString()` behavior.

## Provider and version scope

The first vertical slice begins after the provider has produced a
`GraphQueryResult`, so its behavior is identical for every conforming provider.
The currently verified upstream normalization boundaries are:

| Provider | Verified version | First-slice behavior |
| --- | --- | --- |
| Neo4j | 5.26 Community | Normalized result can be materialized; direct observation query adapter is deferred. |
| TigerGraph | 4.2.4 Community | Normalized result can be materialized; direct observation query adapter is deferred. |

A later provider-capability specification must define projection compilation,
server-side limits, result ordering, and live integration evidence before a
provider advertises direct observation execution.

## Architecture and dependencies

Public observation contracts and materialization live in `Nodal.Analytics`,
which depends inward on `Nodal.Core`. `Nodal.Core` and provider packages do not
depend on `Nodal.Analytics`. The materializer is a pure, stateless boundary; it
does not own a transport, service locator, cache, or provider switch.

Provider adapters will depend on an observation-source abstraction introduced
by their own accepted specification. Advanced analysis implementations remain
outside this public repository.

## Operational behavior

Materialization is synchronous because its input is already in memory. It
performs no I/O and is safe to repeat. Node and relationship order match the
normalized provider result. The returned object graph exposes read-only
collections and does not retain mutable input collections.

Cancellation belongs to the later provider execution boundary. A provider
adapter must propagate cancellation and must not call this materializer after a
cancelled or partially failed read. Partial provider results are never promoted
to a successful observation.

## Security and privacy

Properties are opt-in and use separate node and relationship projections.
Unknown properties are ignored; values outside the projection do not enter the
snapshot. Diagnostics and exception messages contain counts, types, and
property names but not property values or credentials. Tenant isolation remains
the responsibility of the provider query and application boundary.

## Verification strategy

- Unit tests cover bounds, projection, immutable copying, ordering, canonical
  key formatting, endpoint validation, duplicate identities, parallel edges,
  unsupported identifiers, and null arguments.
- `Nodal.Analytics.Tests` is added to the solution and to the product coverage
  gate.
- Architecture tests continue to enforce that `Nodal.Core` does not depend on
  `Nodal.Analytics` and that analytics does not depend on providers.
- Provider compiler, cancellation, and live version-gated tests are required by
  the later direct-observation provider specification.
- Added product code must maintain at least 95% line coverage.

Evidence locations:

- Tests: `tests/Nodal.Analytics.Tests`
- Architecture rules: `tests/Nodal.ArchitectureTests`
- Coverage enforcement: `eng/verify-coverage.ps1`
- User documentation: `website/docs/concepts/analytics-boundary.md`
- Release evidence: the package artifact and CI summary for the implementing PR

## Performance budget

Default limits are 10,000 nodes and 50,000 relationships. Bounds are evaluated
before allocating observation records. Materialization is O(nodes +
relationships + selected properties), preserves provider ordering without a
sort, and performs no reflection. A reproducible benchmark is required before
raising defaults or publishing throughput claims.

## Delivery impact

- Package: additive public API in `Nodal.Analytics`; no new package.
- Compatibility: no existing public API is removed or changed.
- Providers: no provider behavior or capability declaration changes in the
  first slice.
- Migrations: no impact.
- Documentation: analytics boundary page gains the observation contract and an
  executable-style example.
- Release notes: record the canonical observation model as an additive beta
  capability.

## Acceptance criteria and evidence

- [x] Compile-safe public API and example are defined.
- [x] Neo4j and TigerGraph normalization scope and tested versions are explicit.
- [x] Ordering, direction, parallel-edge, cancellation, and partial-result semantics are explicit.
- [x] Data-minimization and immutable-copy behavior are explicit.
- [x] Default limits and asymptotic materialization budget are defined.
- [x] Test, coverage, architecture, documentation, and release evidence locations are defined.
- [ ] The first vertical slice is implemented and its CI evidence is linked from the implementing PR.
