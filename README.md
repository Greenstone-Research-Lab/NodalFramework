# Nodal Framework

Nodal Framework is a provider-based .NET graph data access prototype. It keeps the domain model and query API provider-neutral while Neo4j and TigerGraph packages compile and execute the same model through their native transports.

## Packages

| Package | Purpose |
| --- | --- |
| `Nodal.Core` | Provider-neutral model, LINQ query surface, tracking, and unit of work |
| `Nodal.Migrations` | Portable graph schema migration contracts and planning |
| `Nodal.Neo4j` | Neo4j/Cypher provider using the official pooled Bolt driver |
| `Nodal.TigerGraph` | TigerGraph/GSQL provider using REST++ and an optional administrative transport |

The initial alpha targets .NET 10. Package versions move together so provider and core contracts remain compatible during the pre-release period.

## Attribute-based model

Attributes describe only portable graph semantics. Database-specific indexes, constraints, storage options, and migration details remain in the fluent migration API so domain POCOs do not become coupled to one graph database.

```csharp
using Nodal.Core.Metadata;

[GraphNode("Person")]
public sealed record Person(
    [property: GraphKey]
    [property: GraphProperty("person_id")]
    string Id,

    [property: GraphProperty("display_name")]
    string Name)
{
    [GraphIgnore]
    public string DisplayLabel => $"{Name} ({Id})";
}

[GraphRelation("KNOWS", Directed = true)]
public sealed class Knows(DateTime since)
{
    [GraphProperty("since_at")]
    public DateTime Since { get; set; } = since;
}
```

- `[GraphNode]` maps a CLR node type to its provider-neutral label or vertex type.
- `[GraphRelation]` maps an edge POCO and records whether its direction is meaningful.
- `[GraphKey]` selects the stable domain identifier. By convention, `Id` and `<TypeName>Id` also work.
- `[GraphProperty]` maps both node and relationship payload properties.
- `[GraphIgnore]` excludes calculated, transient, or application-only properties.

Fluent model configuration has the highest precedence, attributes come next, and conventions are the fallback.

## Context and strongly typed sets

Public `GraphSet<T>` and `RelationSet<TSource, TRelation, TTarget>` properties are discovered automatically:

```csharp
public sealed class SocialGraphContext(IGraphProvider provider) : NodalContext(provider)
{
    public GraphSet<Person> People => Set<Person>();

    public RelationSet<Person, Knows, Person> Friendships =>
        Relations<Person, Knows, Person>();
}
```

The same LINQ expression is translated using the mapped graph property names:

```csharp
var adults = await context.People
    .Match(person => person.Name == "Ada" && person.Id == "person-42")
    .Take(10)
    .ToListAsync();
```

Relationships can be traversed without introducing provider-specific query text. `Where` after a traversal filters the reached node, and the result is materialized as that node's POCO type:

```csharp
var peopleKnownByAda = await context.People
    .Match(person => person.Id == "person-42")
    .Traverse(context.Friendships)
    .Where(person => person.Name != "Grace")
    .Take(10)
    .ToListAsync();

var peopleWhoKnowAda = await context.People
    .Match(person => person.Id == "person-42")
    .TraverseIncoming(context.Friendships)
    .ToListAsync();
```

The same provider-neutral traversal model compiles to directed Cypher patterns for Neo4j and directed GSQL path patterns for TigerGraph. Relations declared with `Directed = false` automatically use an undirected traversal.

Use a path projection when the relationship payload is part of the domain operation:

```csharp
var path = await context.People
    .Match(person => person.Id == "person-42")
    .TraversePath(context.Friendships)
    .WhereRelation(knows => knows.Since >= new DateTime(2024, 1, 1))
    .WhereTarget(person => person.Name != "Grace")
    .SingleAsync();

Console.WriteLine(path.Source);
Console.WriteLine(path.Relation);
Console.WriteLine(path.Target);
```

Path nodes and relationships participate in the context identity map. Repeating the query therefore reuses tracked instances. A mutable relationship payload can be persisted through the same unit of work:

```csharp
path.Relation.Since = DateTime.UtcNow;
context.Friendships.Update(path.Source, path.Relation, path.Target);
await context.SaveChangesAsync();
```

`ToRelationsAsync()` is available when only relationship payloads are needed. Neo4j updates a queried relationship by its exact provider element identity, preserving parallel edges. TigerGraph emits source, edge, and target output through typed GSQL accumulators and normalizes the response to the same canonical path records.

## Provider construction

Neo4j uses one long-lived official driver instance and therefore reuses its Bolt connection pool:

```csharp
await using var provider = new Neo4jProvider(new Neo4jOptions
{
    Endpoint = new Uri("neo4j://localhost:7687"),
    Username = "neo4j",
    Password = "secret",
    Database = "neo4j"
});

var context = new SocialGraphContext(provider);
```

TigerGraph uses an externally managed `HttpClient`, allowing applications to control HTTP pooling and handler lifetime:

```csharp
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

Both providers produce Nodal's canonical result model before POCO materialization. Provider-specific response shapes therefore do not escape into application code.

## Unit of work

Node and relationship mutations are collected in the context and converted into an ordered, provider-neutral mutation plan:

```csharp
var ada = new Person("person-1", "Ada");
var alan = new Person("person-2", "Alan");

context.People.Add(ada);
context.People.Add(alan);
context.Friendships.Connect(ada, new Knows(DateTime.UtcNow), alan);

GraphSaveResult result = await context.SaveChangesAsync();
```

New nodes are planned before their relationships. Relationship deletions are planned before node deletions. Entry states are accepted only after the provider confirms a successful commit; a failed commit therefore remains retryable. Providers opt into writes through `IGraphMutationProvider`, allowing read-only providers to remain valid.

Neo4j executes the full plan inside a client-managed write transaction. TigerGraph executes vertex and edge upserts as one REST transaction with `gsql-atomic-level: atomic`; its capability metadata therefore reports `RequestOrQuery` transaction scope. When a plan contains a deletion, Nodal derives a deterministic query name from the operation shape, creates and installs one parameterized `nodal_apply_mutations_*` GSQL query through the configured administrative transport, and invokes its REST endpoint once. Later plans with the same shape reuse that installed query. A definition or installation failure occurs before the data endpoint is called, and Nodal never splits one unit of work into non-atomic requests.

Pending work can be inspected without exposing provider-specific commands:

```csharp
var pending = context.ChangeTracker.Entries(GraphEntryState.Added);
```

## Migrations

Migrations declare portable schema intent and carry a stable history identifier:

```csharp
public sealed class InitialSocialGraph : NodalMigration
{
    public override string Id => "20260816_001_initial_social_graph";

    protected override void Up(MigrationBuilder migration) => migration
        .CreateNode<Person>()
        .CreateRelation<Knows, Person, Person>()
        .CreateIndex<Person, string>(person => person.Name);

    protected override void Down(MigrationBuilder migration) => migration
        .DropRelation<Knows>()
        .DropNode<Person>();
}
```

Planning is side-effect free. Execution skips identifiers already stored in provider history:

```csharp
NodalMigration[] migrations = [new InitialSocialGraph()];

MigrationPlan dryRun = await context.Database.PlanMigrationsAsync(migrations);
MigrationPlan applied = await context.Database.MigrateAsync(migrations);
```

Plans contain deterministic SHA-256 checksums and provider-specific commands. Neo4j applies its commands and `__NodalMigration` history record in one write transaction. TigerGraph compiles typed vertex, edge, and secondary-index operations into one deterministic schema-change job. TigerGraph administrative execution remains an explicit provider capability because its supported REST API exposes schema inspection and query installation but not a general arbitrary-DDL endpoint; the framework does not silently invent or depend on an undocumented route.

TigerGraph migration execution is enabled only when the host supplies an administrative transport appropriate to its deployment:

```csharp
ITigerGraphAdministrativeTransport administration = new MySupportedGsqlTransport();
var provider = new TigerGraphProvider(
    httpClient,
    tigerGraphOptions,
    "SocialGraph",
    administration);
```

The migration executor bootstraps the `__NodalMigration` vertex type when necessary, records checksums through an atomic REST++ upsert, and removes temporary schema jobs even when job execution fails. The same administrative channel enables lazy installation of transactional mutation queries required by delete-containing units of work. Without it, querying and atomic create/update batches remain available while migrations and delete plans report an explicit unsupported-capability error. Because mutation dictionaries currently carry runtime values rather than declared property metadata, a null property in a delete-containing compiled plan is rejected instead of guessing an unsafe GSQL parameter type.

## Quality gate

The repository has one local command matching the CI quality job:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./eng/verify.ps1
```

It restores dependencies, verifies formatting, builds in Release mode, runs the complete test suite, enforces the Core package's minimum 95% line-coverage gate, and validates the publishable NuGet archives. Coverage can also be run independently:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./eng/verify-core-coverage.ps1
```

The script rebuilds the Core tests, produces a Cobertura report under the ignored `TestResults` directory, and fails when line coverage falls below the threshold.

Package verification can also be run independently:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./eng/verify-packages.ps1
```

The package gate produces all four `.nupkg` and `.snupkg` artifacts, then inspects their manifests and contents for the MIT expression, repository metadata, README, license, IntelliSense XML, target framework, and required package dependencies.

## Live integration tests

Live database tests are isolated in `Nodal.IntegrationTests` and are skipped during ordinary unit-test runs unless their environment is configured.

Neo4j can be started in a disposable Docker container and tested end to end with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./eng/run-neo4j-integration.ps1
```

The runner publishes a temporary Bolt port, supplies credentials only through process environment variables, runs commit/read/delete and rollback tests, and removes its uniquely named container in a `finally` block.

TigerGraph live tests expect a dedicated QA graph containing `Person` vertices and `KNOWS` edges with the properties used by the sample model:

```powershell
$env:NODAL_TIGERGRAPH_ENDPOINT = 'https://your-host/'
$env:NODAL_TIGERGRAPH_ACCESS_TOKEN = 'secret-token'
$env:NODAL_TIGERGRAPH_GRAPH = 'NodalQa'
powershell -NoProfile -ExecutionPolicy Bypass -File ./eng/run-tigergraph-integration.ps1
```

No live credentials are stored in the repository. The integration project intentionally avoids the current Testcontainers dependency chain until its high-severity SSH.NET advisory is available through a patched NuGet release.

GitHub Actions runs the quality gate and a disposable Neo4j smoke environment for every pull request targeting `developer`, `staging`, or `master`. TigerGraph smoke tests run only outside pull requests when the repository variable `NODAL_RUN_TIGERGRAPH` is `true`; credentials are read from the protected `tigergraph-qa` environment secrets `NODAL_TIGERGRAPH_ENDPOINT`, `NODAL_TIGERGRAPH_ACCESS_TOKEN`, and `NODAL_TIGERGRAPH_GRAPH`. The TigerGraph suite verifies create/read/update persistence and confirms that an invalid edge rolls back vertices in an atomic REST batch.

## License

Nodal Framework is independently developed and distributed under the [MIT License](LICENSE.txt), the same permissive license model used by Entity Framework Core. Nodal Framework is not affiliated with or endorsed by Microsoft or the .NET Foundation.
