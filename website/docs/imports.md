---
title: Import planning
description: Build bounded, provider-neutral graph mutation plans with reviewable dry-run evidence.
---

# Import planning

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

## Current scope

This slice covers mapping and planning. Streaming CSV parsing is available in
`Nodal.Import.Csv`, while relational metadata discovery and graph draft
proposals are available in `Nodal.Import.Relational`. CLI import execution,
SQL Server/PostgreSQL data adapters, clean-room package samples, and live
provider acceptance tests remain separate beta-readiness slices.
