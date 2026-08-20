---
title: Traversals and paths
description: Traverse incoming, outgoing, undirected, and bounded repeated graph relationships.
---

# Traversals and paths

Traversal methods retain type information while expressing graph direction and depth:

```csharp
var neighborhood = await context.People
    .Match(person => person.Id == "person-42")
    .Traverse(context.Friendships, minDepth: 1, maxDepth: 3)
    .WithoutCycles()
    .ToSubgraphAsync();
```

Use `TraverseIncoming` for the reverse direction. Relations marked `Directed = false` produce undirected patterns. `TraversePath` returns `GraphPath<TSource, TRelation, TTarget>` when the edge payload belongs to the operation:

```csharp
var path = await context.People
    .Match(person => person.Id == "person-42")
    .TraversePath(context.Friendships)
    .WhereRelation(edge => edge.SinceYear >= 2024)
    .SingleAsync();
```

Neo4j and TigerGraph support different native path semantics. Nodal reports unsupported combinations explicitly rather than promising a misleading common denominator.
