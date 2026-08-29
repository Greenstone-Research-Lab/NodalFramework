---
id: MODEL-G0
title: Deterministic strong-type model generator
status: implemented
type: feature
owners: Nodal maintainers
last-reviewed: 2026-08-30
---

# MODEL-G0 — Deterministic strong-type model generator

## Objective and user value

Generate readable, AOT-friendly C# nodes, relations, a `NodalContext`, source
generation manifest, and `JsonSerializerContext` from a reviewed canonical
descriptor. Generated output uses one model type per file and never requires
runtime type emission.

## Non-goals

- Execute dictionary-shaped tenant models at runtime.
- Infer business semantics or rename source relationships using an LLM.
- Generate credentials, provider commands, or customer data.

## Terminology and invariants

- The descriptor fingerprint and generator version are embedded in output.
- Output ordering, paths, and content are deterministic.
- Graph names remain separate from safe C# identifiers.
- Invalid or colliding CLR names fail before any file is written.
- Composite source keys use one explicit generated application identity;
  Nodal never emits multiple `[GraphKey]` members.

## API and usage examples

```powershell
nodal model generate --descriptor model.nodal.json --output Generated
```

## Provider and version scope

The generator is provider neutral and targets the repository's .NET 10
baseline. Provider annotations never alter generated portable semantics.

## Architecture and dependencies

`Nodal.Modeling.CodeGeneration` depends only on `Nodal.Core`. `Nodal.Tool`
provides the filesystem boundary through `nodal model generate`.

## Operational behavior

Generation validates and renders the full source set in memory before any CLI
write. Files are sorted ordinally and use stable slash-separated relative paths.

## Security and privacy

Descriptors and output must not contain credentials or source rows. Names are
validated before entering C# source and graph names are escaped as literals.

## Verification strategy

Unit tests cover deterministic output, every portable value kind, custom
namespaces, invalid identifiers, collisions, composite keys, context members,
fingerprints, and AOT serializer metadata. The reference journey compiles the
generated source in an isolated consumer.

## Performance budget

Generation is linearithmic in model members because canonical ordering is
required. No reflection, provider I/O, or compilation occurs in the engine.

## Delivery impact

Adds a non-packable public-source engine bundled inside `Nodal.Tool`; no new
NuGet library dependency is required by generated applications.

## Acceptance criteria and evidence

- [x] Specification accepted.
- [x] Deterministic one-type-per-file generation implemented.
- [x] Context, manifest, and AOT serialization metadata implemented.
- [x] CLI generation command implemented.
- [x] Repository quality gates and isolated consumer compilation pass.
