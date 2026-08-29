---
id: ADR-0001
title: Specification lifecycle and merge traceability
status: accepted
type: decision
owners: Nodal maintainers
last-reviewed: 2026-08-29
---

# ADR-0001 — Specification lifecycle and merge traceability

## Context

Nodal exposes provider-neutral APIs whose observable behavior must remain
consistent across databases, transports, migrations, imports, and analytics.
Issue descriptions and implementation-only pull requests do not provide a
stable contract for that breadth. Design intent can otherwise drift between
code, provider capabilities, tests, documentation, and releases.

## Decision

Behavioral work begins as a versioned specification in `specs/`. The document
moves from draft to accepted before implementation starts and to implemented
only after every acceptance criterion has evidence. Pull requests reference a
known specification identifier or state a meaningful non-behavioral exemption.
CI validates both the repository and the pull request reference.

Scope changes update the accepted specification before the implementation is
merged. Rejected and superseded documents remain in version control with an
explicit reason or replacement.

## Consequences

Review begins earlier and provider differences become visible before API code
is committed. Tests and release notes gain durable traceability. Small
maintenance pull requests incur one explicit exemption line, while behavioral
changes require additional design work before coding.

## Alternatives considered

- Issue-only planning was rejected because issues are mutable and do not ship
  beside the code contract.
- Documentation-after-implementation was rejected because it records the
  implementation rather than constraining it.
- A heavyweight external requirements platform was rejected for now because it
  would add cost, permissions, and availability dependencies to an open-source
  repository.
