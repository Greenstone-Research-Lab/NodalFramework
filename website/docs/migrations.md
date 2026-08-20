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
