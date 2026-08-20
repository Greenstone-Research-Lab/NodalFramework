---
sidebar_position: 2
description: Define a graph model, create a Nodal context, and execute your first provider-neutral query.
---

# Getting started

## 1. Define the graph model

```csharp
using Nodal.Core.Metadata;

[GraphNode("Person")]
public sealed class Person(string id, string name)
{
    [GraphKey]
    public string Id { get; } = id;

    public string Name { get; set; } = name;
}

[GraphRelation("KNOWS", Directed = true)]
public sealed class Knows(int sinceYear)
{
    public int SinceYear { get; set; } = sinceYear;
}
```

## 2. Create a context

```csharp
using Nodal.Core;
using Nodal.Core.Execution;
using Nodal.Core.Query;

public sealed class SocialGraphContext(IGraphProvider provider) : NodalContext(provider)
{
    public GraphSet<Person> People => Set<Person>();

    public RelationSet<Person, Knows, Person> Friendships =>
        Relations<Person, Knows, Person>();
}
```

## 3. Select a provider

```csharp title="Neo4j"
await using var provider = new Neo4jProvider(new Neo4jOptions
{
    Endpoint = new Uri("neo4j://localhost:7687"),
    Username = "neo4j",
    Password = "secret",
    Database = "neo4j"
});

var context = new SocialGraphContext(provider);
```

```csharp title="TigerGraph"
var httpClient = new HttpClient();
var provider = new TigerGraphProvider(
    httpClient,
    new TigerGraphOptions
    {
        Endpoint = new Uri("https://example.i.tgcloud.io/"),
        Username = "tigergraph",
        Password = "secret"
    },
    graphName: "SocialGraph");

var context = new SocialGraphContext(provider);
```

## 4. Query and save

```csharp
var ada = await context.People
    .Match(person => person.Id == "person-42")
    .SingleAsync();

var alan = new Person("person-84", "Alan");
context.People.Add(alan);
context.Friendships.Connect(ada, new Knows(2026), alan);

await context.SaveChangesAsync();
```

Provider construction is the only part that changes. The model and application workflow stay the same.
