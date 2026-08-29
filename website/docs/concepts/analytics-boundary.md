---
title: Analytics boundary
description: Understand the public Nodal analytics contract boundary.
---

# Analytics boundary

`Nodal.Analytics` is an optional public layer above `Nodal.Core` and the
database-provider packages. It provides provider-neutral contracts and
capability-aware integration for analytics that supported graph platforms can
execute.

Database-resident centrality and community requests do not pass through this
package. They belong to `Nodal.Core`'s fluent contract and execute through the
active provider's native analytics engine. `Nodal.Analytics` remains useful for
bounded observations and derived networks created in application memory; it is
never an automatic fallback for a missing provider capability.

It is not a provider, does not become a dependency of provider packages, and
does not promise that every provider offers the same analytics capability.
Applications should inspect the compatibility matrix and handle unavailable
capabilities explicitly.

Advanced analytics implementations are outside the public package and
documentation contract. Their availability, licensing, configuration, and
operational behavior are governed separately and must not be inferred from a
public provider capability declaration.

## Canonical observations

The canonical observation model is the stable hand-off between provider result
normalization and analytics. It preserves node and relationship identity,
relationship direction, provider ordering, and parallel relationships without
retaining a Neo4j or TigerGraph payload.

Install the optional package alongside the provider packages used by the host:

```bash
dotnet add package Nodal.Analytics
```

Materialization starts from a `GraphQueryResult` that has already been
normalized by a Nodal provider:

```csharp
using Nodal.Analytics.Observations;

var options = new GraphObservationOptions
{
    MaxNodes = 1_000,
    MaxRelations = 5_000,
    NodeProperties = new HashSet<string>(StringComparer.Ordinal)
    {
        "name",
        "category",
    },
    RelationProperties = new HashSet<string>(StringComparer.Ordinal)
    {
        "orderedAt",
    },
};

GraphObservation observation = GraphObservationMaterializer.Materialize(
    providerResult,
    options);
```

Properties are opt-in. An empty projection copies no properties, which makes
data minimization the default rather than an application convention. Default
bounds are 10,000 nodes, 50,000 relationships, 10,000 items per projected
collection, and 16 nested property levels. Exceeding a graph-size limit fails
the complete operation with `GraphObservationLimitExceededException`; unsafe
property shapes are rejected during immutable copying.

The materializer performs no I/O, reflection, sorting, provider dispatch, or
client-side algorithm emulation. Direct observation queries for Neo4j and
TigerGraph will be introduced only through versioned provider-capability
specifications.

Canonical keys distinguish strings, GUIDs, and integer identities. A
relationship endpoint that is missing or ambiguous across node types is
rejected, preventing an incomplete snapshot from reaching an analytics model.
