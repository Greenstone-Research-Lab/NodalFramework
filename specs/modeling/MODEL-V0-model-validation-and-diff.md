---
id: MODEL-V0
title: Canonical model validation, inspection, and diff
status: implemented
type: feature
owners: Nodal maintainers
last-reviewed: 2026-08-30
---

# MODEL-V0 — Canonical model validation, inspection, and diff

## Objective and user value

Give local tooling and CI a stable way to validate, summarize, fingerprint,
compare, and compatibility-gate canonical graph model descriptors before code
generation, migration, import, or deployment.

## Non-goals

- Infer business semantics or migrate data.
- Compare provider-native physical schema objects.
- Replace C# compiler or package API compatibility validation.

## Terminology and invariants

Issue codes and logical paths are stable automation contracts. Diff order is
ordinal and compatibility impact is explicit.

## API and usage examples

```powershell
nodal model validate --descriptor model.nodal.json
nodal model diff --from previous.json --to current.json --fail-on-breaking true
```

## Provider and version scope

Provider neutral; supports the current canonical descriptor format only.

## Architecture and dependencies

Pure validation, inspection, and diff functions live in `Nodal.Core.Modeling`.
Filesystem and process exit behavior remain in `Nodal.Tool`.

## Operational behavior

Validation returns stable issue codes and paths. Inspection reports format,
fingerprint, type/property counts, composite keys, and review markers. Diff
classifies ordered changes as breaking or non-breaking. `--fail-on-breaking`
turns an incompatible model change into a deterministic CLI failure.

## Security and privacy

Commands process schema metadata only and never echo source credentials or row
data. Output may be retained as CI evidence.

## Verification strategy

Tests cover valid, warning, unsupported-version, null-member, structural,
unchanged, additive, removal, key, relation-shape, CLR-name, property-type, and
nullability cases in both Core and CLI layers.

## Performance budget

Validation and comparison are linearithmic in descriptor members and allocate
only bounded evidence proportional to detected changes.

## Delivery impact

Adds public Core contracts and four additive model CLI commands. Existing
migration and import commands are unchanged.

## Acceptance criteria and evidence

- [x] Specification accepted.
- [x] Non-throwing validation evidence and stable exception implemented.
- [x] Deterministic inspection and compatibility diff implemented.
- [x] CLI validate, inspect, and diff commands implemented.
- [x] Repository quality gates pass.
