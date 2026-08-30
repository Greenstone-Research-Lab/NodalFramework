---
id: IMPORT-D1
title: Relational discovery to canonical descriptor
status: implemented
type: feature
owners: Nodal maintainers
last-reviewed: 2026-08-30
---

# IMPORT-D1 — Relational discovery to canonical descriptor

## Objective and user value

Convert the lossless relational interaction model into a deterministic
`GraphModelDescriptor` suitable for review, generation, validation, and import.

## Non-goals

- Use an LLM to invent business semantics.
- Mutate a source or target database.
- Hide incomplete keys, external objects, or review-required relation names.

## Terminology and invariants

Physical object, column, key, foreign-key, referential-action, and source
fingerprint evidence is retained as isolated annotations. Portable graph names
follow deterministic conventions and remain user-overridable.

## API and usage examples

```csharp
RelationalInteractionModel interaction = RelationalInteractionModelBuilder.Build(snapshot);
GraphModelDescriptor descriptor = RelationalGraphModelDescriptorBuilder.Build(interaction);
```

## Provider and version scope

Provider neutral. Native data-type names are mapped through a conservative
portable type classifier; unknown types remain text and require review.

## Architecture and dependencies

Implementation lives in `Nodal.Import.Relational` and depends inward on the
canonical contracts in `Nodal.Core` through `Nodal.Import`.

## Operational behavior

Primary keys are preserved in ordinal order. Objects without a discovered key
receive an explicit synthetic key proposal and review annotation rather than a
silent semantic claim.

## Security and privacy

Only schema metadata enters the descriptor. Connection strings and row data
are excluded.

## Verification strategy

Northwind-shaped fixtures cover nodes, composite keys, relations, type mapping,
external objects, deterministic fingerprints, annotations, and CLI output.

## Performance budget

Mapping is linearithmic in schema members due to deterministic sorting.

## Delivery impact

Additive API in `Nodal.Import.Relational`; relational CLI gains an optional
canonical descriptor output.

## Acceptance criteria and evidence

- [x] Specification accepted.
- [x] Deterministic descriptor builder implemented.
- [x] CLI output and tests implemented.
- [x] Repository quality gates pass.
