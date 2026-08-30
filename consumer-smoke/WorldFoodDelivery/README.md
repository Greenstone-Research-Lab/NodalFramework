# World Food Delivery clean-room consumer

This project proves that a new application can restore and use Nodal only from
immutable NuGet packages. It deliberately has no project reference to the Nodal
repository.

It is also the canonical package-only documentation contract. Public guide
examples are accepted for a beta only when this external-consumer journey
continues to compile and run against the exact package set.

The scenario imports a small Tallinn food-delivery dataset, tracks a
provider-neutral graph mutation batch, compiles portable queries for Neo4j and
TigerGraph, compiles provider-native PageRank plans, creates a bounded canonical
observation, runs transparent derived-network metrics, plans a graph migration,
and inspects the equivalent normalized relational design. Relational inspection
produces an interaction model, a canonical graph descriptor, GraphML, GEXF, and
DOT projections and then verifies their structural meaning.

The clean-room script installs the published `Nodal.Tool`, validates and
inspects the descriptor, generates one strong type per file, recompiles the
consumer with those generated types, and proves an additive schema
diff/regeneration round. No repository project reference is allowed.

When run directly, generated inspection files are retained in `artifacts/`.
Pass a second command-line argument to select another output directory.

## Project layout

- `Domain/Nodes`: graph node POCOs, one type per file.
- `Domain/Relations`: graph relation POCOs, one type per file.
- `Domain/Enums`: domain value sets, one type per file.
- `Application`: CSV ingestion, import orchestration, and the scenario use case.
- `Persistence`: the Nodal context and code-first migration.
- `Infrastructure`: the isolated smoke-test provider boundary.
- `Relational`: normalized SQL metadata and interaction-model export workflow.
- `Verification`: observable outcome checks for graph and relational behavior.

`Program.cs` is only the composition root. Provider-specific concerns do not
leak into domain POCOs or application orchestration.

## Run against package artifacts

From the repository root, first pack the solution, then run:

```powershell
./eng/run-published-consumer-smoke.ps1 `
  -PackageVersion '0.1.0-beta.1' `
  -PackageSource (Resolve-Path './TestResults/package-verification')
```

The same command runs in CI against freshly built artifacts and, after a beta
publication, against NuGet.org. The script fails if a project reference is
introduced or any expected Nodal package is not restored.
