---
title: Provider overview
description: Compare the Neo4j and TigerGraph integration boundaries.
---

# Provider overview

Provider packages compile the same query and mutation models while respecting their database's native transport and capability boundaries.

| Concern | Neo4j | TigerGraph |
| --- | --- | --- |
| Query language | Cypher | GSQL |
| Transport | Official Bolt driver | REST++ and optional administration transport |
| Pool ownership | Long-lived driver | Host-managed `HttpClient` |
| Mutation transaction | Client-managed write transaction | Atomic REST request or installed mutation query |
| Schema migrations | Transactional Cypher commands | Deterministic schema job through administration transport |

Both providers normalize native responses into Nodal result records before POCO materialization. Application code does not parse provider payloads.
