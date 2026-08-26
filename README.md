# NODAL FRAMEWORK IS OPEN SOURCE UNDER MPL-2.0.

Documentation is licensed under CC BY 4.0. Nodal names, logos, and trademarks
are not granted under the software license. Hosted services, premium models,
datasets, commercial APIs, and enterprise support are governed by separate
commercial terms. Contributions require acceptance of the Nodal Contributor
License Agreement.

# Nodal Framework

Nodal Framework is a provider-based .NET graph data access prototype. It keeps the domain model and query API provider-neutral while Neo4j and TigerGraph packages compile and execute the same model through their native transports.

## Packages

| Package | Purpose |
| --- | --- |
| `Nodal.Core` | Provider-neutral model, LINQ query surface, tracking, and unit of work |
| `Nodal.Migrations` | Portable graph schema migration contracts and planning |
| `Nodal.Neo4j` | Neo4j/Cypher provider using the official pooled Bolt driver |
| `Nodal.Analytics` | Provider-neutral analytics contracts and capability integration |
| `Nodal.TigerGraph` | TigerGraph/GSQL provider using REST++ and an optional administrative transport |
| `Nodal.Tool` | .NET global tool for deterministic migration snapshots, diffs, plans, and validation |

The initial alpha targets .NET 10. Package versions move together so provider and core contracts remain compatible during the pre-release period.

Install one provider package; it brings `Nodal.Core` transitively. Add the
migration package only when the application owns schema evolution:

```bash
dotnet add package Nodal.Neo4j --prerelease
# or: dotnet add package Nodal.TigerGraph --prerelease
dotnet add package Nodal.Migrations --prerelease
```

Install the migration CLI separately as a .NET tool:

```bash
dotnet tool install --global Nodal.Tool --prerelease
nodal migrations validate --snapshot nodal.snapshot.json
```

Immutable migration bundles capture provider identity, required capabilities,
ordered up/down commands, execution channels, and destructive flags under a
canonical SHA-256 checksum. `NodalMigrationBundleExecutor` provides idempotent,
provider-neutral apply, rollback, dry-run, checksum-drift detection, explicit
destructive approval, and optional exclusive provider locking. CLI `apply` and
`rollback` load a trusted, provider-composed execution host named by environment
variables; connection credentials remain inside that deployment host and never
enter arguments, plans, bundles, or command output.

`Nodal.Analytics` is an optional public contract layer above providers. It keeps
provider-executed analytics integration and capability declarations separate from
the query and migration foundations. Advanced analytics implementations are not
part of the open-source package contract.

Pin all packages to the same version for reproducible builds, for example
`0.1.0-alpha.2`. The complete console, worker, and ASP.NET Core setup is in the
[installation guide](website/docs/installation.md).

## Compatibility and provider capabilities

Nodal distinguishes vendor client compatibility from versions verified by this repository. The current live QA baselines are Neo4j 5.26 Community and TigerGraph 4.2.4 Community. `Nodal.Neo4j` uses `Neo4j.Driver` 6.3.0; the vendor states that driver 6.x connects to Neo4j 4.4.x, 5.x, 2025.x, and 2026.x, but those additional server families are not yet Nodal-certified. Neo4j 5.26 analytics require the vendor-matched GDS 2.13 release.

| Capability | Neo4j | TigerGraph |
| --- | --- | --- |
| Parameterized queries and fixed traversals | Supported | Supported |
| Variable-depth traversal | Supported | GSQL Syntax V2 with documented restrictions |
| Optional match | Supported | Not supported |
| Correlated existence, additional patterns, row aggregates, and set operations | Supported | Not supported by interpreted GSQL; use an installed provider extension |
| Transaction boundary | Client-managed transaction | Atomic request or installed query |
| Migration execution | Supported | Requires administrative transport |
| Centrality and community detection | Requires compatible GDS and named projection | Requires explicitly configured installed GSQL query |
| Weighted analytics | Algorithm-specific | Must be declared for each installed query |
| Typed shortest paths | Native Cypher; GDS for weighted algorithms | Configured installed GSQL query |

Analytics families currently represented by the portable contract:

| Family | Algorithms | Neo4j status | TigerGraph status |
| --- | --- | --- | --- |
| Centrality | ArticleRank, articulation points, betweenness, bridges, CELF, closeness, degree, eigenvector, harmonic, HITS, PageRank | GDS compiler; live certification pending | Installed-query contract; configured per deployment |
| Community | Clique counting, conductance, HDBSCAN, K-core, K-1 coloring, K-means, label propagation, Leiden, local clustering coefficient, Louvain, modularity, modularity optimization, SCC, triangle count, WCC, maximum k-cut, SLLPA | GDS compiler; live certification pending | Installed-query contract; configured per deployment |
| Path finding | Shortest/all-shortest, Dijkstra, A*, Yen | Native unweighted execution and GDS weighted compiler | Installed-query execution with canonical routes |

The full matrix and verification legend are published in the [compatibility documentation](website/docs/providers/compatibility.md). Unsupported capabilities fail before transport and are never emulated by downloading the graph into application memory.

Weekly Dependabot checks cover NuGet, documentation npm packages, Docker database images, and GitHub Actions. Update pull requests target `developer`; a database version becomes a supported Nodal baseline only after its compatibility and live integration suites pass.

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

### Query engine

The fluent query surface keeps values parameterized while pushing filtering, ordering, paging, distinctness, traversal, and aggregates into the selected provider:

```csharp
string[] selectedIds = ["person-42", "person-84"];

var page = await context.People.Query()
    .Where(person => selectedIds.Contains(person.Id))
    .Where(person => person.Name.StartsWith("Ad") && person.Name != null)
    .OrderBy(person => person.Name)
    .ThenByDescending(person => person.Id)
    .Skip(20)
    .Take(10)
    .Distinct()
    .AsNoTracking()
    .Select(person => new { person.Id, person.Name })
    .ToListAsync();

var exists = await context.People.Match(person => person.Name.Contains("Lovelace")).AnyAsync();
var count = await context.People.Match(person => selectedIds.Contains(person.Id)).CountAsync();
var person = await context.People.Match(person => person.Id == "person-42").SingleAsync();
```

`FirstAsync`, `FirstOrDefaultAsync`, `SingleAsync`, `SingleOrDefaultAsync`, `AnyAsync`, and `CountAsync` apply bounded or aggregate execution. `CountAsync` uses a server-side aggregate when paging has not changed LINQ count semantics. `AsAsyncEnumerable` provides cancellation-aware asynchronous consumption; HTTP providers necessarily receive one response payload, while the API keeps consumer code provider-neutral.

Neo4j additionally supports correlated existence checks, independently named required patterns, provider-side row aggregates, and compatible node-query unions. Each value remains parameterized; the second union operand is automatically rebased so parameter names cannot collide:

```csharp
var selected = await context.People.Match(person => person.Active)
    .Union(context.People.Match(person => person.Name.StartsWith("Ada")))
    .OrderBy(person => person.Name)
    .Take(50)
    .ToListAsync();

var summary = await context.People.Query()
    .ToRows()
    .Select("name", person => person.Name)
    .Count("people")
    .Having("people", GraphComparisonOperator.GreaterThan, 1)
    .OrderByDescending("people")
    .ToListAsync();
```

Scalar columns selected together with aggregate columns define the provider-side grouping key. TigerGraph's interpreted GSQL route does not advertise these query shapes: Nodal rejects them before database transport rather than attempting an in-memory fallback. An installed TigerGraph provider extension can expose a separately verified execution path.

Graph analytics retain the same typed model while executing centrality and community algorithms on the provider:

```csharp
var influentialPeople = await context.People.Query()
    .Analyze(context.Friendships)
    .PageRank()
    .OnProjection("social")
    .Top(20)
    .ToListAsync();

var communities = await context.People.Query()
    .Analyze(context.Friendships)
    .Louvain(new LouvainOptions(MaximumLevels: 8))
    .OnProjection("social")
    .ToListAsync();
```

The analytics contract covers the full centrality and community-detection families and preserves algorithm-specific metrics for HITS, bridges, components, cliques, clustering, and modularity. Neo4j uses explicitly enabled GDS procedures; TigerGraph advertises only explicitly configured installed GSQL query endpoints. Unsupported operations fail before transport execution and are never emulated by downloading the graph into application memory.

Shortest paths keep both endpoints strongly typed:

```csharp
GraphRoute<Person, Knows> route = await context.People
    .Match(person => person.Id == sourceId)
    .ShortestPathTo(context.People.Match(person => person.Id == targetId), context.Friendships)
    .MaxDepth(8)
    .SingleAsync();
```

Neo4j GDS deployments expose discovery and projection create/reuse/drop operations through `context.Database.GetAnalyticsRuntime()`. TigerGraph exposes its configured installed-query snapshot through the same segregated runtime contract.

Hot query factories can be compiled once:

```csharp
var personById = NodalCompiledQuery.Compile((SocialGraphContext database, string id) =>
    database.People.Match(person => person.Id == id));

var ada = await personById(context, "person-42").SingleAsync();
```

Graph-native queries support incoming, outgoing, and undirected hops, repeated-hop depth bounds, multiple compatible edge types, explicit cycle policy, and normalized subgraph output:

```csharp
GraphQueryResult neighborhood = await context.People
    .Match(person => person.Id == "person-42")
    .Traverse(context.Friendships, minDepth: 1, maxDepth: 3)
    .WithoutCycles()
    .ToSubgraphAsync();
```

Neo4j compiles repeated hops and simple paths to Cypher. TigerGraph switches only repeated-hop queries to GSQL Syntax V2 and keeps fixed traversals on the stable Syntax V1 path. Unsupported semantic combinations fail explicitly: TigerGraph does not emulate optional match or a vertex-simple variable-depth path when GSQL cannot expose the required intermediate aliases.

Provider-native escape hatches remain parameterized and return the same normalized result contracts:

```csharp
var rawPeople = await context.Database.QueryRawAsync<Person>(
    "MATCH (`node`:`Person`) WHERE `node`.`person_id` = $id RETURN `node`",
    new Dictionary<string, object?> { ["id"] = "person-42" });
```

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

Queries use identity resolution by default. Tracked mutable POCOs are compared with their original mapped-property snapshots when `SaveChangesAsync` runs, so calling `Update` is not required for ordinary property edits. `AutoDetectChangesEnabled`, `DetectChanges`, `Entry`, `Attach`, `Detach`, property-level `IsModified`, `AsNoTracking`, and `ReloadAsync` provide explicit control for high-volume workloads.

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

Plans contain deterministic SHA-256 checksums and provider-specific commands. Neo4j commits a homogeneous schema-command batch transactionally, then records the `__NodalMigration` state in a separate graph-write transaction because Neo4j does not permit schema modifications and graph writes in the same transaction. Nodal uses an `Applying`/`Applied`/`Failed` state machine and idempotent DDL so interrupted schema work is visible and retryable. TigerGraph compiles typed vertex, edge, and secondary-index operations into one deterministic schema-change job. TigerGraph administrative execution remains an explicit provider capability because its supported REST API exposes schema inspection and query installation but not a general arbitrary-DDL endpoint; the framework does not silently invent or depend on an undocumented route.

TigerGraph migration execution is enabled only when the host supplies an administrative transport appropriate to its deployment. Self-managed and local Docker installations can use the included documented GSQL process transport:

```csharp
ITigerGraphAdministrativeControlPlane administration = new TigerGraphGsqlProcessTransport(
    new TigerGraphGsqlProcessOptions
    {
        FileName = "docker",
        PrefixArguments =
        [
            "exec",
            "nodal-tigergraph",
            "/home/tigergraph/tigergraph/app/4.2.4/cmd/gsql"
        ],
        GraphName = "SocialGraph",
        VerifiedServerVersion = "4.2.4 Community"
    });
var provider = new TigerGraphProvider(
    httpClient,
    tigerGraphOptions,
    "SocialGraph",
    administration);
```

Migration support is advertised only after the control plane verifies schema read/write, job inspection, cleanup, and graph-scoped locking. The executor bootstraps `__NodalMigration` plus an independent `__NodalSchemaJob` journal, records every irreversible phase, and performs temporary-job cleanup with a bounded token independent from caller cancellation. A restart resumes cleanup or history persistence without replaying a schema change known to have succeeded. A cancelled RUN has an unknown outcome and throws `TigerGraphMigrationRecoveryRequiredException` until an operator inspects the graph and calls `provider.MigrationRecovery.ConfirmSchemaAppliedAsync(...)` or `ConfirmSchemaNotAppliedAsync(...)`.

The same administrative channel enables lazy installation of transactional mutation queries required by delete-containing units of work. Without it, querying and atomic create/update batches remain available while migrations and delete plans report an explicit unsupported-capability error. Because mutation dictionaries currently carry runtime values rather than declared property metadata, a null property in a delete-containing compiled plan is rejected instead of guessing an unsafe GSQL parameter type.

## Documentation

The documentation platform combines an English Docusaurus guide and journal with a DocFX API reference generated from the product's XML documentation. Machine-readable `llms.txt`, extended coding-agent context, and a JSON-LD capability graph are published with the static site.

Build the complete site locally with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./eng/build-docs.ps1
```

The production output is written to `website/build`. During authoring, restore the website packages and start the live Docusaurus server with:

```powershell
npm.cmd ci --prefix website
npm.cmd run start --prefix website
```

API pages under `/api` are generated by DocFX before the Docusaurus build. See [`docs/DEPLOYMENT.md`](docs/DEPLOYMENT.md) for the Cloudflare Pages setup and required GitHub environment secrets.

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

The package gate produces all six `.nupkg` and `.snupkg` artifacts, then inspects their manifests and contents for the MPL-2.0 expression, repository metadata, README, license, IntelliSense XML, target framework, and required package dependencies.

## Publishing

Alpha packages are published only after a pull request promotes `developer` to `staging`. The `Publish Alpha Packages` workflow assigns one immutable `0.1.0-alpha.<run>` version to all six packages, runs the complete QA gate, exchanges GitHub's OIDC identity for a short-lived NuGet credential, and publishes `Nodal.Core` before its dependent packages. No long-lived NuGet API key is stored by the repository.

After publication, the same workflow runs a clean-room World Food Delivery consumer smoke test. It copies a small CSV order dataset into a fresh temporary console application, restores only the immutable packages from NuGet.org, imports customers, restaurants, foods, orders, couriers, and relationship payloads in one bounded unit of work, and validates migration planning plus Neo4j and TigerGraph query boundaries. The consumer project contains no `ProjectReference`; its resolved package identities are retained as a workflow artifact. This verifies the experience an external application receives, rather than merely rebuilding this repository.

The GitHub `staging` environment must define `NUGET_USER` as the NuGet profile name. NuGet Trusted Publishing must match repository owner `Greenstone-Research-Lab`, repository `NodalFramework`, workflow file `publish-alpha.yml`, and environment `staging`. Publishing deliberately does not use `--skip-duplicate`, ensuring package conflicts and reserved identifiers fail visibly.

## Live integration tests

Live database tests are isolated in `Nodal.IntegrationTests` and are skipped during ordinary unit-test runs unless their environment is configured.

### Local Docker stack

Neo4j and TigerGraph Community can be started together as a persistent local development stack:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./eng/start-local-databases.ps1
```

Neo4j Browser is available at `http://localhost:7474` with local-only credentials `neo4j` / `NodalLocal123!`. TigerGraph GraphStudio and its consolidated REST/GSQL endpoint are available at `http://localhost:14240` with the Community image's local credentials `tigergraph` / `tigergraph`; the startup script creates the `NodalQa` graph with the `Person` vertex and `KNOWS` edge schema used by the integration suite. TigerGraph is substantially larger than Neo4j and requires at least 8 GB of Docker memory.

Run both live provider suites against the persistent containers with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./eng/run-local-integration.ps1
```

Stop containers while preserving their data volumes with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./eng/stop-local-databases.ps1
```

The compose ports and credentials are intentionally limited to loopback-bound local development. REST++ data authentication is disabled by the Community image's local configuration, while interpreted GSQL requests use Basic authentication. These settings must not be reused for shared or production deployments.

### Runnable provider demos

The [`samples`](samples/README.md) directory contains one shared social graph model and two console hosts. Both hosts execute the same provider-neutral create, path traversal, update, and verification workflow; only provider construction differs.

With the local Docker stack available, run both demos using:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./eng/run-local-demos.ps1
```

The generated `Ada -> KNOWS -> Alan` paths remain in each database for visual inspection. Connection settings can be overridden through the documented `NODAL_NEO4J_*` and `NODAL_TIGERGRAPH_*` environment variables.

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

Nodal Framework source code is distributed under the [MPL-2.0 license](LICENSE.txt).
Documentation is distributed under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/).
Trademarks and hosted or commercial services are governed by the policies in this repository.
