---
title: Versioning and deprecation policy
description: Compatibility, deprecation, and release-promotion rules for Nodal Framework beta packages.
---

# Versioning and deprecation policy

Nodal Framework uses SemVer-compatible package versions. All public Nodal
packages produced from one release share the same version and must be upgraded
together. A mixed package graph is not a supported deployment.

## Beta compatibility window

Before 1.0, a breaking change remains possible, but it cannot be silent. A
public API selected for removal is marked obsolete and documented in release
notes. Except where a security vulnerability or incorrect provider behavior
requires immediate action, that API remains functional for at least the next
beta release and for no fewer than 90 days after the deprecation notice.

The CI API baseline detects unreviewed removals and signature changes. An
approved break must update the baseline, release notes, upgrade guidance, and
consumer verification in the same pull request.

`Nodal.PatternRecognition` was an experimental alpha package removed before
this beta policy took effect. Its alpha versions were unlisted, it has no beta
successor in the public repository, and consumers should remove the reference.

## Provider compatibility

A provider version is supported only when it appears in the published
[compatibility matrix](../providers/compatibility.md) with repeatable compiler
and live-integration evidence. Client connectivity alone does not establish
support. Provider-specific functionality remains an explicit extension and
never becomes a silent fallback for the portable API.

## Promotion and approval

- Feature work enters `developer` through a reviewed pull request.
- A beta candidate moves from `developer` to `staging` through the protected
  promotion path.
- A `staging` commit publishes one immutable beta version after the required
  quality, security, package, and evidence gates pass.
- Only an approved `staging` commit may move to `master`.
- NuGet packages are never overwritten. A failed publication is corrected by a
  new version, and affected versions are unlisted only when necessary.

Every published beta is traceable to its protected source commit, package
checksums, SPDX SBOM, symbol packages, provenance attestation, capability graph,
and clean-room consumer result.
