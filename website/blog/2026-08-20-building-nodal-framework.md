---
slug: building-nodal-framework
title: Building one .NET model for multiple graph engines
authors: [ilker]
tags: [architecture, dotnet, neo4j, tigergraph]
description: Why Nodal Framework separates graph semantics from provider languages and response formats.
image: /img/journal/default-cover.svg
image_alt: Abstract connected graph illustrating the Nodal Framework architecture.
image_caption: The Nodal Framework Journal documents architectural decisions, constraints, and lessons learned.
---

Graph databases share nodes and relationships, but their query languages, transports, transaction boundaries, and response formats are not interchangeable. Nodal Framework began with a simple rule: the domain should be portable, while execution should remain native.

<!-- truncate -->

That rule produced three explicit layers. Application code works with POCO nodes, relationship payloads, typed sets, and expression-based queries. Core converts those operations into provider-neutral query and mutation models. Provider packages validate capabilities, compile native commands, execute through the correct transport, and normalize results.

The important word is **explicit**. When a TigerGraph traversal cannot preserve a requested vertex-simple path semantic, Nodal does not download the graph and pretend the operation was portable. It reports the unsupported combination. When Neo4j can execute a repeated hop through native Cypher, the provider uses that facility.

P0 established the query foundation: parameterized filters, ordering, paging, projections, aggregates, directed traversal, bounded depth, paths with edge payloads, compiled factories, raw query escape hatches, normalized subgraphs, and tracking integration. The next phase will harden these contracts through real applications, improve migration lifecycle tooling, and begin graph analytics design.

This journal will document those decisions—including constraints and failed approaches—so users and future contributors can understand not only how the framework works, but why.
