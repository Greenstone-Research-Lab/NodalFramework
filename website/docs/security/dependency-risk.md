---
title: Dependency risk register
description: Accepted documentation build risks and the controls applied to Nodal Framework release artifacts.
---

# Dependency risk register

This register covers known dependency findings that cannot be removed before
the 0.1 beta without replacing the documentation platform or applying an
unsupported dependency combination. It does not waive vulnerabilities in Nodal
runtime packages.

## Docusaurus build-time image parsing

**Status:** Accepted temporarily for the 0.1 beta; reviewed 2026-08-28.  
**Affected chain:** Docusaurus 3.10.2 → `@docusaurus/mdx-loader` →
`image-size` 2.0.2.  
**Advisories:** `GHSA-w3rx-r6r6-pgpr` and `GHSA-5p2g-fcmc-qvqq`.  
**Risk:** A deliberately malformed ICNS, JXL, or HEIF asset can make the
documentation build consume unbounded processing time. The affected parser is
used during static-site generation and is not shipped as executable code in
the generated Cloudflare Pages site.

No patched `image-size` release is available at the recorded review date. The
beta accepts this build-availability risk with the following controls:

- Documentation CI has a 15-minute timeout and read-only repository token.
- Pull-request documentation jobs receive no deployment environment secrets.
- Production deployment runs only from reviewed `master` content inside the
  protected documentation environment.
- Repository image formats remain allowlisted by review; ICNS, JXL, and HEIF
  assets are not accepted without an explicit security review.
- Dependabot and npm audit re-evaluate the dependency weekly. The first
  compatible patched release must replace this acceptance.

Transitive `serialize-javascript` and `uuid` findings are overridden to patched
versions and verified by the documentation build. Overrides are temporary and
must be removed when Docusaurus carries compatible patched dependencies.

## Runtime release policy

NuGet runtime packages are not covered by this acceptance. A known vulnerable
runtime dependency fails the release dependency audit. Every beta publication
also emits an SPDX SBOM and immutable provenance evidence so consumers can
inspect the exact release composition.
