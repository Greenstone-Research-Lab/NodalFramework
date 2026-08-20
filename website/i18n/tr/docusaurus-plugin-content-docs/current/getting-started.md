---
sidebar_position: 2
title: Başlangıç
description: Graph modelini ve NodalContext'i tanımlayıp ilk provider bağımsız sorgunuzu çalıştırın.
---

# Başlangıç

Node ve relation POCO'larını tanımlayın:

```csharp
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

Graph sözlüğünü güçlü tipli setlerle açın:

```csharp
public sealed class SocialGraphContext(IGraphProvider provider) : NodalContext(provider)
{
    public GraphSet<Person> People => Set<Person>();
    public RelationSet<Person, Knows, Person> Friendships =>
        Relations<Person, Knows, Person>();
}
```

Provider'ı oluşturduktan sonra uygulama sorgusu Neo4j ve TigerGraph için aynıdır:

```csharp
var ada = await context.People
    .Match(person => person.Id == "person-42")
    .SingleAsync();
```

Provider seçimi yalnızca composition root'ta değişir; domain modeli ve iş akışı aynı kalır.
