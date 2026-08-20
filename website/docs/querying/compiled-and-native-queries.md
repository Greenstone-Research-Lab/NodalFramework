---
title: Compiled and native queries
description: Reuse hot query factories and use parameterized provider-native escape hatches.
---

# Compiled and native queries

Compile a frequently used query factory once:

```csharp
var personById = NodalCompiledQuery.Compile(
    (SocialGraphContext database, string id) =>
        database.People.Match(person => person.Id == id));

var ada = await personById(context, "person-42").SingleAsync();
```

When an operation genuinely requires native syntax, execute it through the database facade. Parameters remain separate from command text and results still use Nodal's normalized materializer.

```csharp
var people = await context.Database.QueryRawAsync<Person>(
    "MATCH (`node`:`Person`) WHERE `node`.`person_id` = $id RETURN `node`",
    new Dictionary<string, object?> { ["id"] = "person-42" });
```

Native query text is intentionally provider-specific. Keep it behind an application boundary if database portability is required.
