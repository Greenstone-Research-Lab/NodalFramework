---
title: Import and onboarding
description: Stream bounded imports and inspect relational databases as deterministic interaction models.
---

# Import and onboarding

`Nodal.Import` is the free, provider-neutral onboarding boundary for finite data
loads. It deliberately separates source reading, explicit graph mapping,
planning, review, and provider execution:

```text
CSV or relational source
        ↓
bounded GraphImportBatch<TRecord>
        ↓
explicit node and relation mapping
        ↓
GraphMutationPlan + GraphImportDryRunReport
        ↓
application-approved provider execution
```

The import package depends on `Nodal.Core`, but never on Neo4j, TigerGraph, a
provider SDK, or `NodalContext`. The resulting `GraphMutationPlan` can therefore
be reviewed, stored as evidence, or passed to the mutation executor selected at
the application's composition boundary.

## Explicit mapping

```csharp
using Nodal.Import;

var mapping = GraphImportMapping.For<OrderRow>()
    .Node<Customer>(
        mappingName: "customer",
        nodeType: "Customer",
        keyProperty: "Id",
        keySelector: row => row.CustomerId,
        configure: node => node.Property("Name", row => row.CustomerName))
    .Node<Order>(
        mappingName: "order",
        nodeType: "Order",
        keyProperty: "Id",
        keySelector: row => row.OrderId,
        configure: node => node.Property("Total", row => row.Total))
    .Relation(
        mappingName: "placed",
        sourceMappingName: "customer",
        targetMappingName: "order",
        relationType: "PLACED",
        directed: true,
        configure: relation => relation.Property("OrderedAt", row => row.OrderedAt))
    .Build();
```

Mapping names are stable references inside the mapping definition. Node keys
come from dedicated key selectors and cannot be repeated as ordinary
properties. Relation endpoints must reference defined node mappings, so invalid
definitions fail during `Build()` rather than during provider execution.

## Bounded planning and dry-run evidence

```csharp
var batch = new GraphImportBatch<OrderRow>(
    FirstRecordNumber: 1,
    Records: rows);

var result = new GraphImportPlanner<OrderRow>().Plan(
    batch,
    mapping,
    new GraphImportPlanningOptions(MaxOperations: 5_000));

Console.WriteLine(result.DryRun.PlannedNodeCount);
Console.WriteLine(result.DryRun.PlannedRelationCount);

if (!result.DryRun.Succeeded || result.DryRun.HasDestructiveRisks)
{
    foreach (var diagnostic in result.DryRun.Diagnostics)
    {
        Console.WriteLine($"{diagnostic.RecordNumber}: {diagnostic.Code}");
    }

    foreach (var risk in result.DryRun.Risks)
    {
        Console.WriteLine($"{risk.Code}: {risk.OccurrenceCount}");
    }
}
```

The planner guarantees:

- no database or network I/O;
- a hard maximum on unique mutation operations;
- node operations before dependent relation operations;
- deterministic last-record-wins coalescing inside one batch;
- record-addressed, payload-safe diagnostics for missing identities;
- explicit risk indicators for property overwrite, duplicate identity, and
  omitted graph elements.

`CreateNodeOperation` and `CreateRelationOperation` have upsert semantics in
the current providers. Consequently, a dry run marks mapped property writes as
a destructive risk: applying the plan can replace an existing mapped property.
The application owns the approval decision and provider execution step.

## CSV command-line workflow

Install the global tool and create a versioned mapping document:

```bash
dotnet tool install --global Nodal.Tool --prerelease
```

```json title="world-food-delivery.mapping.json"
{
  "formatVersion": 1,
  "nodes": [
    {
      "name": "customer",
      "type": "Customer",
      "keyColumn": "customer_id",
      "keyProperty": "Id",
      "properties": [
        { "column": "customer_name", "property": "Name" }
      ]
    },
    {
      "name": "order",
      "type": "Order",
      "keyColumn": "order_id",
      "keyProperty": "Id",
      "properties": [
        { "column": "total", "property": "Total" }
      ]
    }
  ],
  "relations": [
    {
      "name": "placed",
      "source": "customer",
      "target": "order",
      "type": "PLACED",
      "directed": true,
      "properties": []
    }
  ]
}
```

Run the default, side-effect-free validation pass:

```bash
nodal import csv \
  --input world-food-delivery.csv \
  --mapping world-food-delivery.mapping.json \
  --evidence import-evidence.json \
  --batch-size 500 \
  --max-operations 5000
```

The evidence records the outcome, source and planned operation counts, batch
count, every mapping decision, record-addressed diagnostics, and aggregated
risk indicators. It deliberately excludes source payloads, credentials, file
paths, and connection details.

### Controlled apply boundary

CSV apply uses two passes. The first pass validates the complete source without
database I/O. Only a successful validation can start the second, bounded apply
pass. Applying property upserts requires explicit approval:

```bash
nodal import csv \
  --input world-food-delivery.csv \
  --mapping world-food-delivery.mapping.json \
  --evidence import-evidence.json \
  --apply true \
  --approve-destructive true
```

`Nodal.Tool` does not reference Neo4j or TigerGraph. The application supplies a
public, parameterless `IGraphMutationExecutor` composition type through
`NODAL_IMPORT_HOST_ASSEMBLY` and `NODAL_IMPORT_HOST_TYPE`. That host owns
provider selection, pooled connections, authentication, and secret retrieval.
Each batch receives the provider's atomicity guarantee; the complete multi-batch
file is intentionally not presented as one distributed transaction.

An invalid record, missing identity, operation-limit violation, or missing
destructive approval prevents the apply pass from starting. A provider failure
during apply stops subsequent batches and remains a deployment-level recovery
concern; continuous synchronization, reconciliation, and resumable enterprise
ingestion are outside this free onboarding slice.

## SQL Server and PostgreSQL sources

`Nodal.Import.Relational` includes provider-family adapters without taking a
dependency on `Microsoft.Data.SqlClient` or `Npgsql`. The application creates
and pools its normal ADO.NET connection; Nodal neither owns nor closes it:

```bash
dotnet add package Nodal.Import.Relational --prerelease
```

```csharp
IRelationalSourceAdapter source = new PostgreSqlRelationalSourceAdapter();

RelationalSchemaSnapshot schema = await source.ReadAsync(connection, cancellationToken);

var request = new RelationalReadRequest(
    Schema: "delivery",
    Table: "orders",
    Columns: ["order_id", "customer_id", "total"],
    OrderByColumns: ["order_id"],
    MaxRows: 10_000,
    CommandTimeoutSeconds: 30);

await foreach (RelationalRow row in source.ReadRowsAsync(connection, request, cancellationToken))
{
    Console.WriteLine(row["order_id"]);
}
```

Use `SqlServerRelationalSourceAdapter` for SQL Server. Both implementations
return the same table, column, primary-key, and foreign-key metadata model.
They preserve provider type names as evidence rather than guessing lossy CLR
conversions.

## Relational Interaction Model

Schema discovery can be converted into an open, deterministic model before any
domain interpretation is attempted:

```csharp
var schema = await source.ReadAsync(connection, cancellationToken);
var model = RelationalInteractionModelBuilder.Build(schema, source.ProviderName);

string json = RelationalInteractionModelJson.Serialize(model);
await File.WriteAllTextAsync("northwind.nodalmodel.json", json, cancellationToken);

await using var stream = File.CreateText("northwind.graphml");
RelationalInteractionModelExporter.Write(
    model,
    RelationalInteractionExportFormat.GraphMl,
    stream);
```

The canonical JSON records:

- tables, views, columns, provider type names, nullability, ordinals, and keys;
- ordered column pairs for single-column and composite foreign keys;
- delete and update referential actions;
- physical source and target endpoints for every relationship;
- a stable SHA-256 schema fingerprint and deterministic ordering;
- explicit diagnostics and external endpoint stubs for partial discovery.

The model classifies structural roles such as entity, association, view, and
external object. It may also attach a readable display direction and a neutral
label such as `HAS_ORDER_DETAIL` or `REFERENCES_PRODUCT`. Every such suggestion
is marked `RequiresReview`; it never claims that a foreign key means `SELLS`,
`BUYS`, or another business concept.

JSON is the lossless interchange format. GraphML, GEXF, and DOT are display
projections intended for graph visualization and exploration, including tools
such as Gephi. They use the suggested display direction while the canonical
model continues to retain the physical foreign-key direction.

No LLM client, semantic inference service, or hosted dependency is included in
the free package. Turning a relational interaction network into a curated
knowledge network is deliberately left to the consuming application and its
domain experts.

### Command-line inspection

`Nodal.Tool` can produce all review artifacts from a trusted application-owned
inspection host:

```bash
nodal import relational \
  --output northwind.nodalmodel.json \
  --graphml northwind.graphml \
  --gexf northwind.gexf \
  --dot northwind.dot
```

The canonical JSON output is required. Visualization outputs are optional and
their destinations must be distinct. Metadata discovery runs once regardless
of how many formats are requested.

Implement the small composition boundary in an application assembly that
references its normal ADO.NET provider:

```csharp
using Microsoft.Data.SqlClient;
using Nodal.Import.Relational;

public sealed class NorthwindInspectionHost : IRelationalInspectionHost
{
    public string ProviderName => "SqlServer";

    public async ValueTask<RelationalSchemaSnapshot> InspectAsync(
        CancellationToken cancellationToken = default)
    {
        string connectionString = Environment.GetEnvironmentVariable("NORTHWIND_CONNECTION")
            ?? throw new InvalidOperationException("The database connection is not configured.");
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return await new SqlServerRelationalSourceAdapter()
            .ReadAsync(connection, cancellationToken);
    }
}
```

For PostgreSQL, use the application's `NpgsqlConnection` and
`PostgreSqlRelationalSourceAdapter`. Then point the tool at the compiled host:

```powershell
$env:NODAL_RELATIONAL_HOST_ASSEMBLY = "C:\apps\Northwind.Host.dll"
$env:NODAL_RELATIONAL_HOST_TYPE = "NorthwindInspectionHost"
nodal import relational --output northwind.nodalmodel.json --graphml northwind.graphml
```

Connection strings remain in the application's secret source and are never
accepted as command arguments, written to artifacts, or printed in summaries.
The host is also the boundary for pooling and managed-identity authentication.

### Relational performance contract

- Catalog discovery is one set-based command with two normalized result sets,
  not an N+1 query per table or column.
- Data reads use forward-only `SequentialAccess | SingleResult` readers.
- SQL contains a server-side parameterized row limit; the client does not read
  and discard an unbounded result.
- At least one selected `ORDER BY` column is mandatory, keeping batches
  reproducible across runs.
- Column names and name-to-ordinal lookup are cached once per result set.
- Each yielded row allocates only its value array; schema and ordinal state are
  shared by every row in that result.
- Identifiers are provider-quoted, values remain parameters, cancellation flows
  to command execution and every asynchronous row read.

The adapter intentionally does not infer pagination tokens or silently execute
all tables. The caller selects a bounded table slice, reviews the discovered
schema, and maps the streamed rows into `Nodal.Import` batches.

## Current scope

This slice covers explicit mapping, bounded planning, streaming CSV input,
deterministic CLI evidence, and a trusted provider execution boundary.
SQL Server/PostgreSQL metadata, bounded streaming adapters, canonical relational
interaction models, and visualization exports are available in
`Nodal.Import.Relational`. Clean-room package samples and live provider
acceptance tests remain separate beta-readiness slices.
