---
title: Query engine
description: Compose parameterized filtering, patterns, sorting, paging, projections, aggregates, and set queries.
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

## Pattern composition

Use a correlated existence pattern when the final node must have, or must not have, a related node that meets a condition. Use `AlsoMatch` for an additional required pattern with its own stable aliases. These are graph equivalents of a constrained subquery and an additional join pattern; they are not client-side joins.

```csharp
var activeCustomers = await context.Customers.Query("customer")
    .WhereExists(context.CustomerOrders, order => order.Total >= 100)
    .AlsoMatch(context.CustomerReferrals, "referral", "referrer")
    .ToListAsync();
```

Aliases must be unique within a query. The selected provider validates this query shape before opening a database transport.

## Server-side aggregate rows

`ToRows()` keeps scalar projections and aggregates inside the provider. A scalar column selected alongside aggregate columns is the grouping key.

```csharp
var orderSummary = await context.Orders.Query()
    .ToRows()
    .Select("customerId", order => order.CustomerId)
    .Count("orderCount")
    .Sum("totalValue", order => order.Total)
    .Having("orderCount", GraphComparisonOperator.GreaterThanOrEqual, 5)
    .OrderByDescending("totalValue")
    .Take(20)
    .ToListAsync();
```

Read values from each returned `GraphQueryRow` with `Get<T>("totalValue")`.

## Compatible set queries

`Union` removes duplicates and `UnionAll` preserves them. Both operands must have the same result node type, result alias, and attached context. Ordering, paging, and limiting occur after the union completes.

```csharp
var people = await context.People.Match(person => person.Active)
    .Union(context.People.Match(person => person.Name.StartsWith("Ada")))
    .OrderBy(person => person.Name)
    .Skip(10)
    .Take(20)
    .ToListAsync();
```

Neo4j currently verifies these composed patterns natively. TigerGraph's interpreted GSQL route rejects correlated patterns, aggregate rows, and set queries before transport; an installed-query provider extension is the appropriate place to add a separately verified implementation.
