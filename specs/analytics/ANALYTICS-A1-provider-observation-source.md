---
id: ANALYTICS-A1
title: Provider observation source
status: implemented
type: feature
owners: Nodal maintainers
last-reviewed: 2026-08-30
---

# ANALYTICS-A1 — Provider observation source

## Objective and user value

Execute one bounded provider-neutral subgraph query and return a canonical
`GraphObservation` without exposing provider payloads or accepting an
unbounded client-side graph extraction.

## Non-goals

- Introduce provider SDK dependencies into `Nodal.Analytics`.
- Emulate provider-native analytics.
- Return partial observations after timeout, cancellation, or transport
  failure.
- Add premium pattern-recognition behavior.

## Terminology and invariants

- The request query must use the `Subgraph` projection.
- A defensive server-side node limit is applied before execution. One extra
  record is requested where possible so a provider that honours the limit can
  still prove that the observation bound was exceeded.
- Materialization retains the A0 identity, direction, parallel-edge,
  projection, and immutability invariants.
- Cancellation and timeout flow to the provider executor.
- Failed, cancelled, timed-out, invalid, or over-limit results never return a
  successful partial observation.

## API and usage examples

```csharp
IGraphObservationSource source = new GraphQueryObservationSource(provider);
var request = new GraphObservationRequest(
    subgraphQuery,
    new GraphObservationOptions
    {
        MaxNodes = 1_000,
        MaxRelations = 5_000,
        NodeProperties = new HashSet<string>(StringComparer.Ordinal) { "name" },
    },
    Timeout: TimeSpan.FromSeconds(30));

GraphObservation observation = await source.ObserveAsync(request, cancellationToken);
```

## Provider and version scope

The source accepts the configured `IGraphProvider` used by the application, or
an `IGraphQueryExecutor` for advanced composition and isolated testing. Neo4j
and TigerGraph therefore use their existing compiler, command executor, and
normalized result contracts. Live behavior remains version-gated by the
integration-test environment; the currently certified lines are Neo4j 5.26
Community and TigerGraph 4.2.4 Community.

## Architecture and dependencies

The contracts and implementation live in `Nodal.Analytics`, which depends only
on `Nodal.Core`. Provider packages remain unaware of analytics. Composition
occurs at the application boundary by supplying the configured provider; the
executor overload preserves dependency-inverted testability.

## Operational behavior

The source is stateless and safe for concurrent use when the supplied executor
is safe for concurrent use. It creates a linked cancellation source only when
a finite timeout is configured. It does not retry transport operations.

## Security and privacy

Only explicitly projected properties enter the observation. Exceptions do not
include property values, credentials, or provider payloads.

## Verification strategy

- Unit tests cover limit injection, stricter caller limits, materialization,
  invalid projections, cancellation, timeout, transport failure, and
  over-limit results.
- Existing provider compiler and contract suites verify the normalized
  subgraph boundary.
- Live provider tests remain environment- and version-gated.

## Performance budget

The server-side query limit is at most `MaxNodes + 1`; A0 independently checks
node, relation, collection, and nesting limits before returning success.

## Delivery impact

Additive API in `Nodal.Analytics`; no package, migration, or provider API
change.

## Acceptance criteria and evidence

- [x] Specification accepted before implementation.
- [x] Public source and request contracts compile with XML documentation.
- [x] Bounds, timeout, cancellation, and failure behavior are covered.
- [x] Release build, coverage, architecture, and repository gates pass.
