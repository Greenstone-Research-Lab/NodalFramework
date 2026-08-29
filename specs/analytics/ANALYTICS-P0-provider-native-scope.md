---
id: ANALYTICS-P0
title: Provider-native multi-relation analytics scope
status: accepted
type: feature
owners: Nodal maintainers
last-reviewed: 2026-08-29
---

# ANALYTICS-P0 — Provider-native multi-relation analytics scope

## Objective and user value

Allow one homogeneous node network to be analysed through one or more mapped
relationship types without downloading the graph or asking applications to
manage provider procedure names. A user can define, for example, an Author
network containing both `CO_AUTHORED` and `SHARES_INTEREST` relationships and
run provider-native PageRank through the same provider-neutral fluent API.

## Non-goals

- Execute database-resident centrality or community algorithms in
  `Nodal.Analytics`.
- Silently fall back to client-side computation when a provider binding is
  missing.
- Add heterogeneous node projections in the first vertical slice.
- Infer domain semantics, coefficients, or weight normalization.
- Grant runtime administration privileges when the host selected validation
  only.

## Terminology and invariants

An **analytics scope** contains one node type and one or more same-node
relationship descriptors. A **binding** is the canonical combination of graph,
algorithm, node type, ordered relationship descriptors, projection contract,
and version. Relationship descriptors are sorted ordinally before hashing, so
fluent inclusion order never changes identity.

- Every relationship connects the scope node type to itself.
- Relationship type names are unique within a scope.
- Coefficients are finite and greater than zero.
- Weight properties are mapped numeric properties.
- Directedness comes from relationship metadata and cannot be overridden.
- Existing single-relation `Analyze(relationSet)` calls remain source and binary
  compatible and are represented internally as one-relation scopes.
- Unsupported scopes fail before provider transport.
- Runtime algorithm options and result limits are parameters and do not alter
  binding identity.

## API and usage examples

```csharp
var influence = GraphAnalyticsScope<Author>
    .Create("author-influence")
    .Include(context.CoAuthorships,
        relation => relation.SharedPaperCount,
        coefficient: 0.70)
    .Include(context.SharedInterests,
        relation => relation.Similarity,
        coefficient: 0.30);

var ranks = await context.Authors.Query()
    .Analyze(influence)
    .PageRank(new PageRankOptions())
    .Top(20)
    .ToListAsync();
```

Single-relation code continues to work:

```csharp
var ranks = await context.Authors.Query()
    .Analyze(context.CoAuthorships)
    .PageRank()
    .ToListAsync();
```

## Provider and version scope

| Provider | Verified baseline | Multi-relation implementation |
| --- | --- | --- |
| Neo4j | 5.26 Community with GDS 2.13 | One named GDS projection containing every scope relationship. |
| TigerGraph | 4.2.4 Community | One verified or Nodal-managed installed-query binding addressed by deterministic convention. |

## Architecture and dependencies

`Nodal.Core` owns scope, binding, capability, and provisioning-policy contracts.
Provider packages compile and validate those contracts without runtime provider
name switches in Core. Existing result records and analytics execution remain
unchanged. `Nodal.Analytics` is not involved in provider-native execution.

TigerGraph keeps legacy algorithm-to-query configuration for compatibility.
New scope bindings use a binding registry. Nodal-managed binding names are
derived from canonical metadata and include a contract version and checksum.
Validation is the safe default; installation requires an explicit
administrative transport and opt-in provisioning policy.

## Operational behavior

- `ValidateOnly` rejects a missing or incompatible binding before HTTP I/O.
- `InstallMissing` may register and install a deterministic Nodal-managed
  definition through the explicit administrative transport.
- `UpgradeManaged` may replace only definitions whose manifest identifies them
  as Nodal-managed; user-owned query names are never overwritten.
- Concurrent installation of one fingerprint is coalesced.
- Cancellation is propagated through discovery, installation, and execution.
- Neo4j projection creation is idempotent and uses the existing runtime gate.

The first generated TigerGraph definition boundary supports homogeneous
PageRank. Other algorithms require a verified binding until their generated
definition has provider/version evidence.

## Security and privacy

Binding diagnostics contain schema identifiers, algorithm names, versions, and
checksums but never credentials or graph property values. Validation-only
runtime identities require no GSQL administration privilege. Generated
identifiers are validated and user-owned installed queries are not replaced.

## Verification strategy

- Core tests cover scope invariants, mapping, canonical ordering, fingerprints,
  compatibility, and old API preservation.
- Shared provider contracts cover deterministic provider-native compilation and
  pre-transport scope failure.
- Neo4j tests cover multi-relation projection parameters and execution commands.
- TigerGraph tests cover deterministic names, binding resolution, policy,
  generated-definition registration, concurrent install, and missing-binding
  failure before HTTP.
- Live tests remain environment gated and identify Neo4j 5.26/GDS 2.13 and
  TigerGraph 4.2.4 explicitly.
- Product packages retain at least 95% line coverage.

## Performance budget

Scope construction and binding identity are O(relationship count). A scope is
limited to 256 relationship descriptors. Canonicalization sorts at most 256
items and caches no graph data. Provider-native execution returns only
algorithm rows and never materializes the analysed graph in application memory.

## Delivery impact

- Packages: additive APIs in `Nodal.Core`, `Nodal.Neo4j`, and
  `Nodal.TigerGraph`; no new package.
- Compatibility: existing single-relation calls and legacy TigerGraph query
  maps remain supported.
- Migrations: no graph schema migration.
- Administration: generated TigerGraph bindings require explicit opt-in.
- Documentation: analytics, TigerGraph, Neo4j, compatibility, and beta notes
  describe scope-level capability.

## Acceptance criteria and evidence

- [ ] One homogeneous scope accepts multiple mapped same-node relationship types.
- [ ] Existing single-relation analytics source and binary behavior is preserved.
- [ ] Canonical binding identity is stable across relationship inclusion order.
- [ ] Capability validation is binding-aware and occurs before transport.
- [ ] Neo4j creates a multi-relation GDS projection and executes against its name.
- [ ] TigerGraph resolves deterministic verified bindings without manual per-relation options.
- [ ] Missing TigerGraph PageRank bindings can be installed only under explicit policy and administration.
- [ ] Unsupported generated algorithms fail before transport.
- [ ] Tests, coverage, package validation, documentation build, and release evidence pass.
