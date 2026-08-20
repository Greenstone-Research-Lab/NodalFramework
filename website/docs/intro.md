---
sidebar_position: 1
title: Nodal Framework
description: A provider-neutral, strongly typed graph data access framework for .NET.
keywords: [dotnet, graph database, Neo4j, TigerGraph, LINQ]
---

# Graph data access that belongs to your domain

Nodal Framework gives .NET applications one strongly typed model for querying and changing graph data. Provider packages translate that model to the database's native language and normalize its result shape before application code sees it.

The first provider family supports Neo4j through Cypher and the pooled Bolt driver, and TigerGraph through GSQL and REST++. The domain model, query expressions, tracking rules, and migration intent remain provider-neutral.

## Design principles

- **Domain-first:** nodes and relationships are ordinary POCOs.
- **Provider-native:** Nodal compiles supported semantics; it does not hide unsupported behavior behind client-side emulation.
- **Parameterized by default:** expression values become provider parameters.
- **Explicit capabilities:** transaction, migration, and query limitations fail visibly.
- **Escape hatches:** native queries remain available without abandoning normalized results.

Nodal Framework currently targets .NET 10 and is under active pre-release development.

Continue with [Getting started](./getting-started.md), or read the [architecture](./architecture.md) first.
