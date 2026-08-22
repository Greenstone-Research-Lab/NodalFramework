---
sidebar_position: 3
description: Define a graph model, create a Nodal context, and execute your first provider-neutral query.
---

# Getting started

This walkthrough assumes a .NET 10 project with either `Nodal.Neo4j` or
`Nodal.TigerGraph` installed. Start with [Install and configure](./installation)
for NuGet commands, reproducible package versions, dependency injection, and
provider lifetime guidance.

## 0. Add a provider package

```bash title="Choose one provider"
dotnet add package Nodal.Neo4j --prerelease
# or
dotnet add package Nodal.TigerGraph --prerelease
```

Add migrations only when this application owns graph schema changes:

```bash
dotnet add package Nodal.Migrations --prerelease
```

Add provider-neutral path and pattern analytics when the application needs
similarity, communities, sequences, or temporal transitions:

```bash
dotnet add package Nodal.Analytics --prerelease
```

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

## 5. Apply the initial schema when needed

Provider packages already include their migration dialect. The separate
`Nodal.Migrations` package supplies migration definitions and orchestration:

```csharp
using Nodal.Migrations;

public sealed class InitialSocialGraph : NodalMigration
{
    public override string Id => "20260821_001_initial_social_graph";

    protected override void Up(MigrationBuilder migration) => migration
        .CreateNode<Person>()
        .CreateRelation<Knows, Person, Person>()
        .CreateIndex<Person, string>(person => person.Name);

    protected override void Down(MigrationBuilder migration) => migration
        .DropRelation<Knows>()
        .DropNode<Person>();
}

await context.Database.MigrateAsync([new InitialSocialGraph()]);
```

Neo4j migration execution is available directly. TigerGraph requires an
explicit administrative transport because schema changes and installed-query
management are privileged operations. See [Migrations](./migrations) before
running schema changes in production.

## 6. Verify the integration

```bash
dotnet restore
dotnet build
dotnet list package
```

Run one bounded query against a non-production graph before enabling writes.
Use `AsNoTracking()` for read-only checks and keep credentials in the host's
secret configuration rather than source control.
