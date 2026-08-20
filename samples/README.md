# Nodal Framework samples

The samples run one provider-neutral social graph workflow through two independent console hosts:

- `Nodal.Samples.SocialGraph` contains the POCO model, `NodalContext`, fluent traversal, and unit-of-work scenario.
- `Nodal.Samples.Neo4j` configures the Neo4j Bolt provider and connection pool.
- `Nodal.Samples.TigerGraph` configures the TigerGraph HTTP provider and externally managed `HttpClient`.

Start the local databases and run both demos from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./eng/run-local-demos.ps1
```

Each run creates a uniquely identified `Ada -> KNOWS -> Alan` path, reads it through the fluent API, updates the node and relationship payload, and verifies the result. The data is intentionally retained so it can be inspected in Neo4j Browser or TigerGraph GraphStudio.

Run a provider independently with the local Docker defaults:

```powershell
dotnet run --project ./samples/Nodal.Samples.Neo4j
dotnet run --project ./samples/Nodal.Samples.TigerGraph
```

All connection settings can be overridden with the same `NODAL_NEO4J_*` and `NODAL_TIGERGRAPH_*` environment variables used by the live integration suite. TigerGraph accepts either `NODAL_TIGERGRAPH_ACCESS_TOKEN` or the username and password variables.
