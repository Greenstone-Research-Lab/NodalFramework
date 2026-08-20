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

Schema migrations and query installation require an `ITigerGraphAdministrativeTransport`. Self-managed deployments may use `TigerGraphGsqlProcessTransport`; managed environments can supply a transport suited to their control plane.
