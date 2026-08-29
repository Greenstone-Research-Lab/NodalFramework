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
    Database = "neo4j",
    GraphDataScienceEnabled = true
});
```

Queries compile to parameterized Cypher. Repeated-hop traversals and simple paths use native Cypher patterns. Mutation plans and migration history are committed through write transactions.

`GraphDataScienceEnabled` is an explicit capability declaration, not a plugin installer. When enabled, analytics compile to parameterized GDS stream procedures over the named projection selected by `OnProjection`. Leave it disabled when GDS is not installed so unsupported operations fail before Bolt execution.

`GraphAnalyticsScope.For<TNode>(...)` adds a provider-neutral multi-relation path. Before execution, Nodal creates or reuses one fingerprinted GDS projection containing every mapped relationship and then runs the selected GDS stream procedure against it. Fingerprinted physical names prevent a changed scope from silently reusing stale projection topology. The current native projection boundary supports unit relationship-family coefficients and either unweighted relations or one common numeric weight-property name across all included relations. Other shapes fail with a stable capability code before Bolt transport.

For edition- or version-specific deployments, populate `AnalyticsAlgorithms` from `CALL gds.list()` instead of advertising every compiler-supported algorithm. The current live database baseline is Neo4j 5.26 Community; analytics compilation targets its compatible GDS 2.13 API family, with live analytics certification still pending.

Never store production credentials in source control. Supply them through your host's secret configuration system.
