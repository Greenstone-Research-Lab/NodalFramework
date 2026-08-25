---
title: Migrations
description: Declare portable graph schema intent and apply it with provider-specific dialects.
---

# Migrations

A migration expresses portable schema intent and owns a stable history identifier:

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

Planning is side-effect free. Plans contain deterministic checksums and provider-specific commands:

```csharp
MigrationPlan preview = await context.Database.PlanMigrationsAsync(migrations);
MigrationPlan applied = await context.Database.MigrateAsync(migrations);
```

Neo4j commits one homogeneous schema-command batch transactionally, then records
its migration state in a separate graph-write transaction. This separation is
required by Neo4j: schema modifications and graph writes cannot share one
transaction. Nodal writes `Applying` first and uses idempotent DDL plus
`Applied`/`Failed` history to make an interrupted migration reviewable and
retryable. TigerGraph compiles schema operations into a deterministic job and
requires an explicit administrative transport.

## Schema snapshots and reviewable diffs

`NodalSchemaSnapshotFactory` captures the registered model without connecting to a
database. Snapshots have their own wire-format version, deterministic ordering and a
stable SHA-256 hash. Provider introspectors can capture the live Neo4j or TigerGraph
schema into the same contract while preserving provider name, version and storage
types.

```csharp
NodalSchemaSnapshot desired = NodalSchemaSnapshotFactory.FromModel(context.Model);
NodalSchemaSnapshot current = await provider.SchemaIntrospector.CaptureAsync();

NodalSchemaMigrationPlan plan = NodalSchemaMigrationMapper.Map(current, desired);
string review = NodalSchemaMigrationPlanSerializer.ToMarkdown(plan);
string automation = NodalSchemaMigrationPlanSerializer.Serialize(plan);
```

Property renames are never inferred. Supply an explicit rename hint such as
`node:people:name -> display_name`; otherwise the diff remains an add/drop pair.
Relation shape changes, changed schema-object definitions and compound indexes that
cannot be represented safely by the portable M1 operations are placed in
`ManualReview`. Unknown snapshot format versions fail with
`NodalSchemaSnapshotVersionException`; a future format must provide an explicit
upgrade path instead of silently reinterpreting persisted metadata.

## Safe backfills and recovery

Backfills must be bounded. Use `IMigrationBackfillCheckpointStore` when a backfill
can outlive one process or request. The executor persists a checkpoint only after
the callback reports a successful batch, resumes from that token, and removes the
checkpoint after completion.

Each `MigrationBackfillContext` exposes a deterministic `IdempotencyKey`. A provider
callback should pass this key to its write transaction or durable deduplication
record. If a process fails after the provider write but before the checkpoint save,
the retry receives the same key and must treat the batch as already applied.

### Recovery procedure

1. Stop the migration worker and inspect migration history and the backfill checkpoint.
2. Verify the provider and server version recorded by the deployment.
3. Resume with the same migration name, batch size, and callback contract.
4. For a type rewrite, validate source and target counts before cleanup.
5. Remove old properties, indexes, or constraints only after validation succeeds.
6. If recovery is not safe, revert the reversible schema migration and restore from
   the provider backup before retrying.

Neo4j checkpoints are stored as `__NodalBackfillCheckpoint` metadata nodes. TigerGraph
uses `ITigerGraphBackfillCheckpointTransport`, because the supported administrative
channel differs between self-managed and managed deployments. Non-transactional
provider commands are surfaced during preflight with a warning and require the
recorded history state plus the provider recovery procedure.

## Neo4j schema boundaries

Neo4j is schema-optional. Node labels, relationship types and ordinary properties
can exist without DDL. Nodal therefore treats `CreateNode`, `CreateRelation` and
flexible property add/remove operations as model metadata and emits no schema
command for them. Indexes and uniqueness constraints are physical schema objects
with deterministic Nodal names.

The certified baseline is Neo4j 5.26 Community. Property-existence and
property-type constraints are Enterprise Edition features, so the Community
provider does not advertise or silently emulate them. Application-level validation
is not presented as a database constraint. Enterprise deployments opt in explicitly:

```csharp
var provider = new Neo4jProvider(new Neo4jOptions
{
    Endpoint = new Uri("neo4j://localhost:7687"),
    Username = "neo4j",
    Password = configuration["Neo4j:Password"]!,
    EnterpriseSchemaConstraintsEnabled = true,
});
```

The portable migration API can then declare node or relationship constraints:

```csharp
migration
    .CreateNodePropertyExistenceConstraint<Person, string>(person => person.Email)
    .CreateNodePropertyTypeConstraint<Person, string>(person => person.Email)
    .CreateRelationPropertyExistenceConstraint<Knows, DateTime>(relation => relation.Since);
```

Without the explicit capability, preflight reports the operation as unsupported
before any command reaches Neo4j. This preserves portable intent while preventing
Community and Enterprise deployments from silently diverging.
