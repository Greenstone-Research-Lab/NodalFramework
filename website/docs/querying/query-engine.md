---
title: Query engine
description: Compose parameterized filtering, sorting, paging, projections, and aggregate graph queries.
---

# Query engine

Nodal translates supported expression trees into provider-native, parameterized queries.

```csharp
string[] ids = ["person-42", "person-84"];

var page = await context.People.Query()
    .Where(person => ids.Contains(person.Id))
    .Where(person => person.Name.StartsWith("Ad"))
    .OrderBy(person => person.Name)
    .ThenByDescending(person => person.Id)
    .Skip(20)
    .Take(10)
    .Distinct()
    .AsNoTracking()
    .Select(person => new { person.Id, person.Name })
    .ToListAsync();
```

Terminal operations include `FirstAsync`, `SingleAsync`, nullable variants, `AnyAsync`, and `CountAsync`. Server-side aggregation is used when it preserves the query's LINQ semantics. Unsupported expression shapes fail during compilation instead of silently moving expensive work into memory.
