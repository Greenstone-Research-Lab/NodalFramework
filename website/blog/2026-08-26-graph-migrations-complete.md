---
slug: graph-migrations-complete
title: "Graph migrations are complete: from intent to controlled execution"
authors: [ilker]
tags: [migrations, architecture, neo4j, tigergraph, release]
description: Nodal Framework's alpha migration package now provides portable planning, reviewable bundles, recoverable provider execution, and controlled deployment workflows.
image: /img/journal/migration-engine-complete.png
image_alt: Graph structures converge into a blueprint-like migration path and emerge as validated graph models.
image_caption: A provider-neutral migration plan becomes a controlled, reviewable execution path for each supported graph platform.
---

Graph migrations are now a completed alpha capability in Nodal Framework.
The work began with a simple goal: let a .NET application describe graph schema
intent once, then review and execute it without hiding the operational
differences between graph platforms.

<!-- truncate -->

That goal is now represented by `Nodal.Migrations`, the `nodal` CLI, and the
provider execution contracts behind Neo4j and TigerGraph. The result is not an
attempt to make every graph database behave like a relational database. It is a
careful migration workflow that keeps portable intent separate from
provider-specific execution and recovery.

## Start with an explicit plan

Nodal migrations express portable operations such as node and relationship
definitions, indexes, and constraints. Planning is side-effect free. Before a
database is changed, the application can create a deterministic plan, inspect
the generated provider commands, and identify operations that need manual
review.

Schema snapshots make this review practical. Nodal can capture the registered
model, compare it with a provider schema snapshot, and generate deterministic
JSON or Markdown output. Renames are intentionally never guessed: an ambiguous
change remains visible as an add/drop pair until an explicit hint is supplied.

## Make deployment artifacts reviewable

The migration CLI now supports snapshots, diffs, plans, validation, and
immutable bundles. A bundle records the migration identifier, provider identity,
required capabilities, ordered commands, transaction semantics, destructive
flags, and a canonical SHA-256 checksum.

This means a reviewed deployment artifact can be moved through a release
pipeline without reconstructing its intent from ad-hoc command-line input.
Checksum drift, provider mismatch, missing capabilities, and destructive work
without explicit approval are rejected before mutation begins. The same bundle
supports dry runs, idempotent apply, and explicit rollback when ordered down
commands exist.

## Respect provider boundaries

The portable API does not pretend that Neo4j and TigerGraph have identical
operational behavior.

For Neo4j, Nodal separates homogeneous schema-command transactions from graph
write history because the server does not allow them to share one transaction.
Migration history moves through `Applying`, `Applied`, and `Failed` states so an
interrupted run remains reviewable and retryable.

For TigerGraph, schema work is modeled as a durable job lifecycle rather than a
REST++ data transaction. Nodal journals job creation, execution, cleanup, and
history reconciliation. When cancellation makes an outcome unknowable, it stops
automatic replay and requires an operator to inspect the live schema before
confirming the recovery path.

Those differences are not implementation details. They are part of the safety
contract, exposed through provider capabilities and documented recovery steps.

## Keep credentials out of plans

The execution boundary is deliberately narrow. The `nodal migrations apply` and
`rollback` commands load a reviewed, provider-composed host assembly. Connection
details and credentials stay in the deployment environment or secret store; they
are not accepted as command arguments and are not written to plans or bundles.

The repository includes compile-checked host examples for Neo4j and TigerGraph.
The accompanying GitHub workflow is designed for a reviewed artifact and a
protected `staging` or `production` Environment, where approval remains the
human gate for destructive work.

## What “complete” means in alpha

This milestone closes the planned M0–M5 migration foundation: contracts,
preflight, history, recovery, schema snapshots and diffs, provider hardening,
immutable bundles, and the CLI workflow are all implemented and covered by the
repository's automated quality path.

It does not mean that every future provider automatically has migration parity.
Each provider must still certify its own schema, transaction, administrative
transport, locking, recovery, and live integration behavior. Nodal will preserve
that distinction as the provider ecosystem grows.

For implementation details, see the [migration guide](/docs/migrations) and the
[provider compatibility matrix](/docs/providers/compatibility). The next phase
can now build on a safer foundation: beta readiness, clean-room consumer
validation, and the analytics work that sits above—not inside—the provider layer.
