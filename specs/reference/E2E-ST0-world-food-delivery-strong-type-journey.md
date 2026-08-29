---
id: E2E-ST0
title: World Food Delivery strong-type reference journey
status: implemented
type: feature
owners: Nodal maintainers
last-reviewed: 2026-08-30
---

# E2E-ST0 — World Food Delivery strong-type reference journey

## Objective and user value

Prove the public data-to-graph route in an application that restores immutable
Nodal packages only: CSV mutation planning, relational discovery, canonical
descriptor production, strong-type generation, migration planning, equivalent
provider query compilation, provider-native analytics compilation, canonical
observation materialization, bounded derived analytics, and schema evolution.

## Non-goals

- Benchmark production database throughput.
- Run proprietary pattern intelligence.
- Put credentials or live customer data in the reference repository.

## Terminology and invariants

- The consumer project contains no `ProjectReference`.
- Every public Nodal dependency and `Nodal.Tool` uses one exact package version.
- The tool is installed into an isolated temporary directory.
- Generated source recompiles without manual edits.
- Inspection, diff, fingerprint, package, and generator evidence is retained by
  CI outside the disposable consumer workspace.

## API and usage examples

```powershell
./eng/run-published-consumer-smoke.ps1 `
  -PackageVersion '0.1.0-beta.1' `
  -PackageSource 'https://api.nuget.org/v3/index.json'
```

## Provider scope

Portable queries compile for Neo4j and TigerGraph. Provider-native PageRank
compiles to Neo4j GDS and a configured TigerGraph installed query. Live database
execution remains in the existing version-gated provider smoke jobs.

## Provider and version scope

The reference targets the exact Neo4j and TigerGraph baselines certified by
their provider packages and one immutable Nodal package version.

## Architecture and dependencies

The template is outside the solution and contains package references only. The
script creates an isolated workspace, package cache, and tool directory.

## Operational behavior

The scenario imports deterministic CSV rows, emits review artifacts, validates
equivalent provider compilation, generates source, recompiles, evolves the
descriptor additively, and records machine-readable evidence.

## Security and privacy

The fixture is synthetic. Temporary files and local tool installations are
removed in a `finally` block and credentials are never CLI arguments.

## Verification strategy

CI runs the template against freshly packed artifacts; publish workflows repeat
it against immutable NuGet packages. Provider live jobs remain separate.

## Performance budget

The fixture is intentionally bounded to a few rows and types. Restore retry
budgets address NuGet indexing only and do not hide execution failures.

## Delivery impact

Strengthens the existing clean-room gate and adds no runtime dependency to
product packages.

## Acceptance criteria and evidence

- [x] Specification accepted.
- [x] Relational interaction model and canonical descriptor produced.
- [x] Equivalent provider queries and native analytics plans verified.
- [x] Canonical observation and public bounded analytics verified.
- [x] Published-tool generation and isolated recompilation scripted.
- [x] Schema diff and regeneration round scripted.
- [x] Clean-room CI run passes against the packaged slice.
