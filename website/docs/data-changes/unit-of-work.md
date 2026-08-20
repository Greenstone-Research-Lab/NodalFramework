---
title: Unit of Work
description: Collect graph mutations and commit an ordered provider-aware plan.
---

# Unit of Work

The context collects node and relationship changes, then creates one ordered mutation plan:

```csharp
var ada = new Person("person-1", "Ada");
var alan = new Person("person-2", "Alan");

context.People.Add(ada);
context.People.Add(alan);
context.Friendships.Connect(ada, new Knows(2026), alan);

GraphSaveResult result = await context.SaveChangesAsync();
```

Creation orders nodes before their relationships; deletion orders relationships before nodes. States are accepted only after successful provider execution, so a failed save remains retryable.

Neo4j executes a plan in a client-managed write transaction. TigerGraph uses an atomic REST request for upserts and a deterministic installed GSQL query when deletion semantics require it. Provider capability metadata exposes these differences.
