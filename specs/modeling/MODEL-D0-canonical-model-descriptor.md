---
id: MODEL-D0
title: Canonical graph model descriptor
status: implemented
type: feature
owners: Nodal maintainers
last-reviewed: 2026-08-30
---

# MODEL-D0 — Canonical graph model descriptor

## Objective and user value

Define a deterministic, provider-neutral schema document shared by discovery,
code generation, validation, import, and private runtime execution without
reducing source types to strings.

## Non-goals

- Infer business semantics from physical database names.
- Store provider credentials or executable provider commands.
- Define tenant deployment or proprietary dynamic runtime behavior.

## Terminology and invariants

- Node and relation IDs are stable semantic identities.
- CLR names are generation hints and remain separate from graph names.
- Keys reference declared node properties.
- Provider annotations are isolated metadata and do not alter portable
  semantics.
- Canonical JSON ordering and fingerprints are ordinal, culture independent,
  and deterministic.

## API and usage examples

```csharp
var descriptor = new GraphModelDescriptor(
    GraphModelFormat.CurrentVersion,
    [customerNode],
    [placedOrderRelation]);
string json = GraphModelDescriptorJson.Serialize(descriptor);
string fingerprint = GraphModelDescriptorJson.ComputeFingerprint(descriptor);
```

## Provider and version scope

The format is provider neutral. Provider annotations may preserve source hints
but cannot change semantic identity or portable value kinds.

## Architecture and dependencies

Contracts live in `Nodal.Core.Modeling`. Import and tooling packages depend
inward on these contracts.

## Operational behavior

Serialization canonicalizes all descriptor and annotation ordering. Unsupported
format versions, malformed JSON, duplicate identities, invalid endpoints, or
invalid keys fail explicitly.

## Security and privacy

Descriptors contain schema metadata only. Secrets and source rows are excluded.
Collection and nesting limits belong to import/runtime execution contracts.

## Verification strategy

Unit tests cover canonical ordering, culture independence, round trips,
fingerprints, duplicate identities, keys, endpoints, annotations, and value
kind conversion.

## Performance budget

Serialization and validation are linearithmic in descriptor members due to
deterministic sorting. Callers cache parsed descriptors by fingerprint.

## Delivery impact

Additive API in `Nodal.Core`; no migration or provider behavior changes.

## Acceptance criteria and evidence

- [x] Specification accepted.
- [x] Typed descriptor and value contracts implemented.
- [x] Canonical JSON and SHA-256 fingerprint implemented.
- [x] Validation and tests pass at the repository quality gate.
