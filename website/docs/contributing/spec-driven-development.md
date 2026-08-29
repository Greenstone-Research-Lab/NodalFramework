---
sidebar_position: 1
title: Spec-driven development
description: How Nodal turns an accepted behavioral contract into code, provider evidence, and a release.
---

# Spec-driven development

Nodal treats a specification as the contract that joins product intent to
public APIs, provider behavior, tests, documentation, and release evidence.
Code is written after the contract is accepted, not before it is documented.

## The delivery loop

1. Write a draft using the feature, provider-capability, or architecture
   decision template in the repository's `specs/templates` directory.
2. Review user value, non-goals, invariants, API examples, provider versions,
   failure semantics, security, performance budgets, and measurable acceptance
   criteria.
3. Mark the document `accepted` before implementation begins.
4. Reference its durable identifier in every implementation pull request.
5. Update the specification first when implementation reveals a scope change.
6. Mark it `implemented` only when tests, compatibility evidence,
   documentation, and release artifacts satisfy every acceptance criterion.

Rejected and superseded specifications remain visible. This preserves the
reason behind a decision and prevents old proposals from quietly returning as
new implementation assumptions.

## Pull request traceability

Every non-Dependabot pull request contains exactly one `Specification:` line.
It may list known identifiers:

```text
Specification: ANALYTICS-A0, CAP-NEO4J-001
```

A meaningful exemption is allowed only when observable product behavior cannot
change:

```text
Specification: N/A - corrects spelling in existing documentation only
```

CI validates the referenced identifiers and the structure of every governed
specification. The canonical lifecycle, templates, and current documents are
available in the [GitHub specification repository](https://github.com/Greenstone-Research-Lab/NodalFramework/tree/master/specs).
