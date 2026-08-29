# Nodal specification repository

Nodal uses executable governance around written design. A specification is the
reviewable contract between product intent, public APIs, provider behavior,
tests, documentation, and release evidence. Code explains how the current
implementation works; a specification explains what must remain true.

## Lifecycle

1. **Draft** — the problem, boundaries, examples, risks, and measurable
   acceptance criteria are being shaped. Product implementation does not begin.
2. **Accepted** — maintainers approve the contract and its provider impact.
   Implementation branches may now reference the specification.
3. **Implemented** — every acceptance criterion has linked evidence and the
   released behavior matches the contract.
4. **Superseded** — a newer specification replaces the contract. The original
   remains in history and names its replacement.
5. **Rejected** — the proposal is deliberately declined and retains its reason.

Scope discovered during implementation is not hidden in code review. Update
the accepted specification first, review that delta, and only then merge the
implementation.

## Document types and identifiers

- Feature specifications use a durable domain identifier such as
  `ANALYTICS-A0`.
- Provider capability specifications use identifiers such as `CAP-NEO4J-001`.
- Architecture decisions use sequential identifiers such as `ADR-0001`.

Identifiers never change after acceptance. File names may add a readable slug,
but references in pull requests, tests, and release notes use the identifier.

## Required traceability

An implementation pull request links:

- its accepted specification;
- architecture decisions that constrain the design;
- unit, contract, architecture, integration, and performance evidence;
- provider/version compatibility changes;
- documentation, migration, packaging, and release-note impact.

CI validates document structure and requires exactly one `Specification:` line
in non-Dependabot pull requests. Use `N/A - <meaningful reason>` only when the
change cannot affect observable product behavior.

## Authoring

Start from the appropriate file in `specs/templates/`, keep language precise,
and make acceptance criteria independently verifiable. Run:

```powershell
./eng/verify-specifications.ps1
./eng/verify-pr-specification.ps1 -PullRequestBody 'Specification: ADR-0001'
```
