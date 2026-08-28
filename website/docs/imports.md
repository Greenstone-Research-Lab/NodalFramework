---
title: Import and onboarding
description: Stream CSV data into bounded, provider-neutral mutation plans with reviewable JSON evidence.
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
SQL Server/PostgreSQL metadata and bounded streaming source adapters are
available in `Nodal.Import.Relational`. Clean-room package samples and live
provider acceptance tests remain separate beta-readiness slices.
