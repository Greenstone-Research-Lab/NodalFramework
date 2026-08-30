---
id: RELEASE-B0
title: Public beta release closure
status: implemented
type: feature
owners: Greenstone Research Lab
last-reviewed: 2026-08-30
---

# RELEASE-B0 — Public beta release closure

## Objective and user value

Close the public beta line with one reproducible, traceable release path. A
consumer must be able to restore the packages without repository source,
compile the documented reference journey, inspect package provenance, and
trace every artifact to one reviewed commit.

## Non-goals

- Implement proprietary pattern intelligence or a dynamic tenant runtime.
- Create the private hands-on experience repository.
- Expand provider capabilities or silently emulate unsupported behavior.
- Promise compatibility beyond the published provider matrix.

## Terminology and invariants

- All nine public packages produced by one run share one immutable version.
- Public asynchronous APIs follow the .NET `Async` and cancellation contract.
- Repacking identical inputs produces byte-identical NuGet and symbol packages.
- Package-only verification contains no `ProjectReference` to repository code.
- Release evidence includes package hashes, dependency audit, SBOM,
  reproducibility evidence, provider baselines, and the source commit.
- `developer -> staging -> master` remains the only promotion path.

## API and usage examples

The slice adds no product API. Maintainers run the governed release contract
through:

```powershell
./eng/verify.ps1 -PackageVersion '0.1.0-beta.1'
```

## Provider and version scope

The release retains the published .NET 10, Neo4j 5.26 Community, Neo4j.Driver
6.3.0, and TigerGraph 4.2.4 Community baselines. It does not expand those
claims.

## Architecture and dependencies

1. Enforce public API async/cancellation conventions in architecture tests.
2. Pack twice from the same reviewed build and compare SHA-256 hashes.
3. Treat the World Food Delivery clean-room application as the canonical,
   package-only documentation contract.
4. Emit one machine-readable release manifest containing every evidence hash.
5. After NuGet publication and the published-consumer smoke test, create an
   immutable GitHub prerelease and tag for the exact staging commit.

## Operational behavior

- Missing or mismatched artifacts fail before publication.
- Existing tags/releases are accepted only when they target the same source
  commit; a conflicting identity fails closed.
- Package publication never uses `--skip-duplicate`.
- Release jobs are non-cancelling and serialized by the staging concurrency
  group.
- Published-package indexing retries remain bounded and observable.

## Security and privacy

- Trusted publishing uses a short-lived OIDC NuGet credential.
- GitHub release creation uses the job-scoped repository token.
- No source credential, database secret, or consumer data enters evidence.
- Dependency review, vulnerability audit, SBOM, provenance attestation, and
  immutable hashes are required release evidence.

## Verification strategy

- Unit and architecture tests cover API conventions.
- Every governed production package remains at or above 95% line coverage.
- Package verification checks identity, metadata, contents, dependencies, and
  API compatibility.
- Reproducibility compares all `.nupkg` and `.snupkg` files byte-for-byte.
- The package-only World Food Delivery journey restores, runs, generates a
  model, compiles generated code, and demonstrates additive schema evolution.
- Neo4j and TigerGraph live-container suites remain provider evidence.
- Docusaurus and DocFX builds remain required checks.

## Performance budget

The additional release gates operate on bounded package artifacts and metadata.
They do not execute graph algorithms or add runtime overhead. The complete CI
quality job retains its 15-minute budget; staging publication retains its
20-minute budget.

## Delivery impact

- Adds no new public package.
- Adds release-closure evidence and a GitHub prerelease record.
- Documents the canonical package-only reference journey and evidence model.

## Acceptance criteria and evidence

- [x] Public API convention tests pass for every product assembly.
- [x] All nine package and symbol artifacts reproduce byte-for-byte.
- [x] Package-only consumer verification passes without project references.
- [x] Release evidence contains hashes for packages, SBOM, dependency audit,
      reproducibility report, and capability graph.
- [x] Staging publication creates or verifies an immutable beta tag and GitHub
      prerelease for the source commit.
- [x] CI, coverage, SonarQube, live providers, packages, security, and
      documentation gates remain green.
