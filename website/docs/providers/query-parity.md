---
sidebar_position: 2
---

# Query parity guide

Nodal's portable query API is a semantic contract, not an agreement to turn
every vendor feature into a least-common-denominator query. A provider may
advertise a query shape only when Nodal has evidence that its compiler,
transport, result materializer, and live server behaviour preserve the Nodal
meaning.

## What parity means

A fluent operation is portable when Neo4j and TigerGraph both execute the same
observable result shape, ordering, duplicate semantics, null behaviour, and
failure behaviour. The framework does not download a graph to emulate a
provider feature that is unavailable at the database.

Some features have a documented native restriction. For example, TigerGraph's
`Skip` requires `Take`; Nodal exposes that restriction as a predictable error
instead of silently changing pagination.

## Current categories

| Category | Neo4j | TigerGraph interpreted GSQL |
| --- | --- | --- |
| Parameterized filters, fixed traversal, ordering, limit, count, fixed paths, subgraphs | Supported | Supported |
| Node-result `Distinct` | Supported | Supported through native vertex-set uniqueness |
| Variable-depth traversal | Supported | Syntax V2, with documented path restrictions |
| Fixed-depth simple paths | Supported | Supported |
| Variable-depth simple paths | Supported | Not supported: repeated-hop aliases are unavailable |
| Scalar rows and aggregate rows | Supported | Supported through SQL-like GSQL Syntax V2 |
| Optional traversal, correlated existence, additional patterns, set operations | Supported | Not part of the portable interpreted route yet |

TigerGraph's row support uses the database's table-producing `SELECT ... INTO`
form. Nodal compiles property columns, `Count`, `Sum`, `Average`, `Min`, `Max`,
automatic grouping for mixed property/aggregate projections, `Having`, row
ordering, and bounded results. The provider normalizes both TigerGraph table
shapes: a multi-row JSON array and a one-row aggregate JSON object. The
baseline includes a live test for property projection, `Count`, `GROUP BY`,
`HAVING`, `ORDER BY`, and `LIMIT` against TigerGraph 4.2.4 Community.

See TigerGraph's [SQL-like SELECT reference](https://docs.tigergraph.com/gsql-ref/3.11/querying/select-statement/sql-like-select-statement)
for the underlying table semantics.

The full supported-version and operational matrix remains in [Compatibility and
capabilities](./compatibility.md).

## Adding a provider

Every new provider starts with the common model and an explicit capability
profile. Do not infer a capability merely because the vendor language has a
similar keyword.

1. Compile a focused `GraphQueryModel` for the shape.
2. Parameterize values and validate identifiers in the provider compiler.
3. Normalize the response into Nodal result records.
4. Add compiler, negative capability, and live-container tests.
5. Record the tested server, driver, extension, and query-language versions in
   the compatibility matrix.
6. Advertise the capability only after the live result semantics are verified.

## Provider extensions

When a feature needs a database-installed procedure or query, the extension is
an explicit provider dependency. It must declare its version, supported query
shapes, request/response contract, and live verification evidence. If it is
not installed, Nodal fails before transport with a capability-specific error.

This keeps application code honest during provider replacement: missing
behaviour never becomes a silent in-memory fallback.
