---
title: Context and typed sets
description: Use NodalContext, GraphSet, and RelationSet as the strongly typed graph boundary.
---

# Context and typed sets

`NodalContext` owns a model, provider, identity map, change tracker, and database facade. Public set properties make the graph vocabulary explicit:

```csharp
public GraphSet<Person> People => Set<Person>();

public RelationSet<Person, Knows, Person> Friendships =>
    Relations<Person, Knows, Person>();
```

`GraphSet<TNode>` begins node queries and tracks node mutations. `RelationSet<TSource, TRelation, TTarget>` preserves the source, relationship payload, and target types for traversal and changes. The relation is not reduced to a foreign key; it remains a domain object.

Keep providers long-lived. Neo4j's provider owns a pooled driver, while TigerGraph accepts an externally managed `HttpClient` so the host controls handler lifetime and HTTP pooling.
