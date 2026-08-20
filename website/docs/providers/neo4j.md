---
title: Neo4j
description: Configure the Nodal Neo4j provider, its pooled driver, Cypher compiler, and transactions.
---

# Neo4j provider

`Nodal.Neo4j` uses the official Neo4j driver. Create one provider for the application lifetime so its Bolt connection pool can be reused.

```csharp
await using var provider = new Neo4jProvider(new Neo4jOptions
{
    Endpoint = new Uri("neo4j://localhost:7687"),
    Username = "neo4j",
    Password = "secret",
    Database = "neo4j"
});
```

Queries compile to parameterized Cypher. Repeated-hop traversals and simple paths use native Cypher patterns. Mutation plans and migration history are committed through write transactions.

Never store production credentials in source control. Supply them through your host's secret configuration system.
