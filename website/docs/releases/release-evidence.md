---
title: Release evidence and reproducibility
description: Verify Nodal beta packages, source identity, SBOM, provenance, and package-only consumer behavior.
---

# Release evidence and reproducibility

Every Nodal beta is a set of nine packages built from one protected staging
commit. The release workflow records evidence before and after publication so
consumers do not have to trust a version label alone.

## Evidence bundle

The GitHub prerelease attaches:

- all `.nupkg` and `.snupkg` artifacts;
- SHA-256, size, package version, and source commit for every artifact;
- the SPDX 2.2 SBOM and its hash;
- the NuGet transitive-dependency vulnerability audit and its hash;
- the byte-for-byte package reproducibility report;
- the provider capability knowledge graph and verified database baselines;
- the package-only World Food Delivery consumer result;
- GitHub build-provenance attestations for package artifacts.

The machine-readable `nodal-release-evidence.json` is the root manifest. A
release is incomplete when any required evidence file is absent.

## Reproduce the package gate

Run the full local release verification with one candidate version:

```powershell
./eng/verify.ps1 -PackageVersion '0.1.0-beta.1'
```

The reproducibility gate packs the complete public package set twice from the
same Release build and compares all nine NuGet packages and all nine symbol
packages byte-for-byte:

```powershell
./eng/verify-reproducible-packages.ps1 -PackageVersion '0.1.0-beta.1'
```

Package validation also compares previously published APIs against the
approved baseline. Moving that baseline requires an explicit compatibility
decision, release notes, upgrade guidance, and consumer evidence.

## Canonical external-consumer contract

The World Food Delivery application is both the clean-room consumer and the
canonical package-only documentation contract. It has no project reference to
the repository. CI restores immutable packages, runs CSV and relational import,
compiles portable and provider-native queries, plans a migration, materializes
a bounded observation, runs derived analytics, generates strong types, compiles
them, and verifies one additive model evolution.

This journey proves package composition and public usage. Live Neo4j and
TigerGraph suites provide separate database execution evidence.

## Immutable beta identity

After NuGet publication and published-consumer verification, the staging
workflow creates `v<package-version>` and a GitHub prerelease targeting the
exact staging SHA. If that identity already exists for another commit, the
workflow fails; it never moves or overwrites the release.
