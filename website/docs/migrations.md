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

Neo4j uses transactional Cypher and its migration history record. TigerGraph compiles schema operations into a deterministic job and requires an explicit administrative transport.

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
