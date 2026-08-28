---
sidebar_position: 2
---

# Query parity guide

Nodal's portable query API is a semantic contract, not an agreement to turn
every vendor feature into a least-common-denominator query. A provider may
advertise a query shape only when Nodal has evidence that its compiler,
transport, result materializer, and live server behaviour preserve the Nodal
meaning.

## What parity means

A fluent operation is portable when Neo4j and TigerGraph both execute the same
observable result shape, ordering, duplicate semantics, null behaviour, and
failure behaviour. The framework does not download a graph to emulate a
provider feature that is unavailable at the database.

Some features have a documented native restriction. For example, TigerGraph's
`Skip` requires `Take`; Nodal exposes that restriction as a predictable error
instead of silently changing pagination.

## Current categories

| Category | Neo4j | TigerGraph interpreted GSQL |
| --- | --- | --- |
| Parameterized filters, fixed traversal, ordering, limit, count, fixed paths, subgraphs | Supported | Supported |
| Node-result `Distinct` | Supported | Supported through native vertex-set uniqueness |
| Variable-depth traversal | Supported | Syntax V2, with documented path restrictions |
| Fixed-depth simple paths | Supported | Supported |
| Variable-depth simple paths | Supported | Not supported: repeated-hop aliases are unavailable |
| Scalar rows and aggregate rows | Supported | Supported through SQL-like GSQL Syntax V2 |
| Correlated `WhereExists` / `WhereNotExists` | Supported | Conditional: Nodal-generated installed query plus administrative transport; live verified |
| Optional traversal, additional patterns, set operations | Supported | Explicitly unavailable; rejected before transport |

TigerGraph's row support uses the database's table-producing `SELECT ... INTO`
form. Nodal compiles property columns, `Count`, `Sum`, `Average`, `Min`, `Max`,
automatic grouping for mixed property/aggregate projections, `Having`, row
ordering, and bounded results. The provider normalizes both TigerGraph table
shapes: a multi-row JSON array and a one-row aggregate JSON object. The
baseline includes a live test for property projection, `Count`, `GROUP BY`,
`HAVING`, `ORDER BY`, `LIMIT`, `Sum`, `Average`, `Min`, and `Max` against
TigerGraph 4.2.4 Community. Result-column names must not be reserved GSQL
identifiers; the compiler rejects them before transport.

## Verification ledger

| Query shape | Neo4j 5.26 Community | TigerGraph 4.2.4 Community | Missing-feature behaviour |
| --- | --- | --- | --- |
| Parameter filters and fixed traversal | Compiler + unit + live container | Compiler + unit + live container | Not applicable |
| Node `Distinct` | Compiler + unit + live container | Compiler + unit + live convergence test | Not applicable |
| Variable-depth traversal | Compiler + unit | Compiler + unit + live bounded Syntax V2 traversal | Unsupported combinations fail during compilation |
| Fixed vertex-simple path | Compiler + unit | Compiler + unit + live subgraph test | Variable-depth simple path fails pre-transport |
| Scalar and aggregate rows | Compiler + unit | Compiler + unit + live grouping/count/numeric aggregate test | Unsupported row shapes and reserved aliases fail during compilation |
| Correlated existence | Compiler + unit + live container | Generated-query unit + live `WhereExists`/`WhereNotExists` container test | Capability preflight rejects it unless runtime generation and administration are configured |
| Optional traversal | Compiler + unit | Explicitly excluded | `NODAL-QUERY-OPTIONAL-TRAVERSAL` |
| Additional required patterns | Compiler + unit | Explicitly excluded | `NODAL-QUERY-MULTIPLE-PATTERNS` |
| `Union` / `UnionAll` | Compiler + unit + live container | Explicitly excluded | `NODAL-QUERY-SET-OPERATIONS` |

“Live container” refers to the repository's executable Docker baselines, not a
claim inferred from vendor documentation. The generated TigerGraph extension
test additionally requires the documented GSQL administrative channel; the
ordinary REST++ test lane cannot create or install queries.

See TigerGraph's [SQL-like SELECT reference](https://docs.tigergraph.com/gsql-ref/3.11/querying/select-statement/sql-like-select-statement)
for the underlying table semantics.

The full supported-version and operational matrix remains in [Compatibility and
capabilities](./compatibility.md).

## Adding a provider

Every new provider starts with the common model and an explicit capability
profile. Do not infer a capability merely because the vendor language has a
similar keyword.

1. Compile a focused `GraphQueryModel` for the shape.
2. Parameterize values and validate identifiers in the provider compiler.
3. Normalize the response into Nodal result records.
4. Add compiler, negative capability, and live-container tests.
5. Record the tested server, driver, extension, and query-language versions in
   the compatibility matrix.
6. Advertise the capability only after the live result semantics are verified.

## Provider extensions

When a feature needs a database-installed procedure or query, the extension is
an explicit provider dependency. It must declare its version, supported query
shapes, request/response contract, and live verification evidence. If it is
not installed, Nodal fails before transport with a capability-specific error.

This keeps application code honest during provider replacement: missing
behaviour never becomes a silent in-memory fallback.

TigerGraph correlated existence is the first runtime-generated extension. It
is disabled by default and requires both an explicit feature opt-in and a
supported administrative transport:

```csharp
var options = new TigerGraphOptions
{
    Endpoint = new Uri("https://example.i.tgcloud.io/"),
    AccessToken = "secret-token",
    GeneratedQueryExtensions = new HashSet<TigerGraphQueryExtensionFeature>
    {
        TigerGraphQueryExtensionFeature.CorrelatedExistence
    }
};

var provider = new TigerGraphProvider(
    httpClient,
    options,
    "SocialGraph",
    administrativeTransport);
```

Nodal fingerprints the parameterized query shape, uses `CREATE OR REPLACE
QUERY`, installs it with `-FORCE`, and invokes the resulting REST++ route.
Concurrent calls through the same provider for a graph and fingerprint share
one installation task. The live suite verifies positive and negative existence with node
and relationship predicates against TigerGraph 4.2.4 Community. Enabling the
feature requires query create/update/install privileges; configuring a
preinstalled-query manifest alone does not enable runtime generation.

For a separately deployed installed-query bundle, use the asynchronous factory
at application startup. It calls the manifest's discovery query, checks the
exact semantic version and declared features, and does not return a provider
when the deployed contract is missing or incompatible:

```csharp
var manifest = new TigerGraphQueryExtensionManifest(
    new Version(1, 0, 0),
    new Dictionary<TigerGraphQueryExtensionFeature, string>
    {
        [TigerGraphQueryExtensionFeature.CorrelatedExistence] = "nodal_exists_v1"
    });

var options = new TigerGraphOptions
{
    Endpoint = new Uri("https://example.i.tgcloud.io/"),
    AccessToken = "secret-token",
    QueryExtensions = manifest
};

var provider = await TigerGraphProviderFactory.CreateAsync(
    httpClient,
    options,
    "SocialGraph",
    cancellationToken);
```

The verified snapshot is available through
`provider.VerifiedQueryExtensions`. Direct constructors remain I/O-free and do
not claim that a configured manifest has been verified.
