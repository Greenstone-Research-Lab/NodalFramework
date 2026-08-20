---
title: Graph model
description: Map graph nodes, edges, keys, and properties to ordinary .NET types.
---

# Graph model

Nodal treats nodes and relationships as separate first-class POCOs. A relationship can carry its own properties and can be directed or undirected.

| Attribute | Meaning |
| --- | --- |
| `GraphNode` | Maps a CLR type to a portable node label or vertex type. |
| `GraphRelation` | Maps an edge type and its direction. |
| `GraphKey` | Selects the stable domain identity. |
| `GraphProperty` | Maps a CLR property to its stored name. |
| `GraphIgnore` | Excludes application-only or calculated state. |

Configuration precedence is fluent model configuration, then attributes, then conventions. Provider-only schema options belong in migrations, keeping domain types independent of Neo4j and TigerGraph.

```csharp
[GraphNode("Person")]
public sealed class Person
{
    [GraphKey, GraphProperty("person_id")]
    public required string Id { get; init; }

    [GraphProperty("display_name")]
    public required string Name { get; set; }

    [GraphIgnore]
    public string DisplayLabel => $"{Name} ({Id})";
}
```
