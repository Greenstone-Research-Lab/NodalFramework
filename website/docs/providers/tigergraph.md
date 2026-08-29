---
title: TigerGraph
description: Configure TigerGraph REST++, authentication, GSQL queries, and the optional administration transport.
---

# TigerGraph provider

`Nodal.TigerGraph` accepts an externally managed `HttpClient`. This keeps connection pooling, handler lifetime, proxies, and resilience policies under host control.

```csharp
var provider = new TigerGraphProvider(
    httpClient,
    new TigerGraphOptions
    {
        Endpoint = new Uri("https://example.i.tgcloud.io/"),
        AccessToken = "secret-token"
    },
    graphName: "SocialGraph");
```

Fixed traversals use stable GSQL Syntax V1 paths. Repeated-hop queries switch to Syntax V2 only when required. Atomic upserts use REST++; delete-containing plans use deterministic installed GSQL mutation queries.

Query installation requires `ITigerGraphAdministrativeTransport`. Migration execution has a stricter boundary: an `ITigerGraphAdministrativeControlPlane` must verify schema read/write, job inspection, cleanup, and graph-scoped locking. An execute-only transport never causes the provider to claim migration support.

Self-managed deployments may use `TigerGraphGsqlProcessTransport`. The 4.2.4 Community Docker image does not place `gsql` on PATH, so the verified local prefix is:

```csharp
var administration = new TigerGraphGsqlProcessTransport(new TigerGraphGsqlProcessOptions
{
    FileName = "docker",
    PrefixArguments =
    [
        "exec",
        "nodal-tigergraph",
        "/home/tigergraph/tigergraph/app/4.2.4/cmd/gsql"
    ],
    GraphName = "SocialGraph",
    VerifiedServerVersion = "4.2.4 Community"
});
```

Managed environments can implement the same control-plane contract without exposing their administrative API to provider-neutral code.

Analytics use explicitly configured installed GSQL queries. `TigerGraphOptions.AnalyticsQueries` maps each available `GraphAnalyticsAlgorithm` to its installed query name. This makes the capability set truthful across TigerGraph editions and deployments instead of assuming that every algorithm library has been installed.

Multi-relation scopes use `TigerGraphAnalyticsBindingManifest`. Each binding maps a canonical Nodal fingerprint to one verified installed query, so applications do not maintain procedure names per relation combination:

```csharp
var manifest = new TigerGraphAnalyticsBindingManifest(
[
    new TigerGraphAnalyticsBinding(
        GraphAnalyticsAlgorithm.PageRank,
        Fingerprint: "74b05be431a4afa6",
        QueryName: "verified_author_pagerank",
        SupportsWeights: true),
]);
```

`ValidateOnly` is the default and performs no administrative write. `InstallMissing` requires an explicit `ITigerGraphAdministrativeTransport` and can generate, install, and reuse deterministic unweighted, unit-coefficient PageRank definitions. Unsupported algorithms and richer weight/coefficient shapes require a verified manifest binding and fail before REST++ execution when absent. Generated names include the canonical contract version and fingerprint; user-owned query names are never inferred or replaced.

Installed queries are unweighted by default. Add an algorithm to `WeightedAnalyticsAlgorithms` only when that query's declared parameters accept `nodal_weight_property`. The current live QA baseline is TigerGraph 4.2.4 Community; broader server-version compatibility is not yet a Nodal promise.
