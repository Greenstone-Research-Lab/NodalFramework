---
sidebar_position: 5
title: Model discovery and strong-type generation
description: Turn relational metadata into a reviewed canonical graph model and deterministic C# types.
---

# Model discovery and strong-type generation

Nodal separates physical database evidence from graph semantics. Relational
inspection first creates a lossless interaction model. A canonical graph model
descriptor then becomes the stable handoff to validation, diff, code
generation, import, and deployment evidence.

```text
relational metadata
        |
        v
interaction model ----> GraphML / GEXF / DOT review
        |
        v
model.nodal.json
        |
        +----> validate / inspect / diff
        |
        `----> deterministic C# nodes, relations, context, manifest, AOT JSON metadata
```

## Produce a descriptor

The trusted inspection host owns the connection and supplies normalized schema
metadata. The CLI never receives a connection string:

```powershell
nodal import relational `
  --output northwind.nodalmodel.json `
  --descriptor northwind.nodal.json `
  --graphml northwind.graphml
```

Physical names, native types, key order, foreign-key endpoints, referential
actions, and source fingerprints remain annotations. Deterministic suggested
relation names are reviewable conventions—not claims about business meaning.

## Validate and inspect

```powershell
nodal model validate --descriptor northwind.nodal.json
nodal model inspect --descriptor northwind.nodal.json --format json --output model-evidence.json
```

Validation uses stable issue codes. Inspection records the canonical SHA-256
fingerprint, type/property counts, composite keys, and review markers.

## Generate strong types

```powershell
nodal model generate `
  --descriptor northwind.nodal.json `
  --output Generated `
  --namespace Acme.Northwind.Graph `
  --context NorthwindGraphContext
```

Output is deterministic and contains one node or relation per file, a typed
`NodalContext`, a manifest with the descriptor fingerprint and generator
version, and a `JsonSerializerContext` for Native AOT-friendly serialization.
Unsafe or colliding C# names fail before output is written.

## Gate schema evolution

```powershell
nodal model diff `
  --from model.previous.nodal.json `
  --to model.current.nodal.json `
  --fail-on-breaking true
```

The diff classifies additions, removals, key changes, relation-shape changes,
CLR name changes, property shape changes, and nullability changes. CI can retain
the JSON output as deployment evidence.

## Public and commercial boundary

Canonical descriptors and deterministic strong-type generation are public.
The complete arbitrary-schema dictionary runtime, tenant artifact build and
routing, and advanced pattern intelligence are separate commercial systems.
Public packages do not contain or document those proprietary implementations.

Generated application source follows the repository's
[`GENERATED_CODE_POLICY.md`](https://github.com/Greenstone-Research-Lab/NodalFramework/blob/master/GENERATED_CODE_POLICY.md).
