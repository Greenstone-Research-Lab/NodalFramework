---
sidebar_position: 2
title: Install and configure
description: Add Nodal Framework packages to console, worker, and ASP.NET Core applications and configure provider lifetimes safely.
---

# Install and configure

Nodal Framework targets **.NET 10**. Install one database provider package in
the application project. The provider brings `Nodal.Core` transitively; add
`Nodal.Migrations` only when the application owns schema evolution.

## Choose packages

| Application requirement | Package |
| --- | --- |
| Neo4j, Cypher, and pooled Bolt connections | [`Nodal.Neo4j`](https://www.nuget.org/packages/Nodal.Neo4j) |
| TigerGraph, GSQL, and REST++ | [`Nodal.TigerGraph`](https://www.nuget.org/packages/Nodal.TigerGraph) |
| Portable migration definitions and execution | [`Nodal.Migrations`](https://www.nuget.org/packages/Nodal.Migrations) |
| Provider authors and compiler-only tools | [`Nodal.Core`](https://www.nuget.org/packages/Nodal.Core) |

All pre-release package versions move together. Do not mix different Nodal
alpha versions in one application.

## Create a project

```bash title="Neo4j console application"
dotnet new console --framework net10.0 --name SocialGraph
cd SocialGraph
dotnet add package Nodal.Neo4j --prerelease
dotnet add package Nodal.Migrations --prerelease
```

```bash title="TigerGraph ASP.NET Core application"
dotnet new webapi --framework net10.0 --name SocialGraph.Api
cd SocialGraph.Api
dotnet add package Nodal.TigerGraph --prerelease
dotnet add package Nodal.Migrations --prerelease
```

For a reproducible build, pin every Nodal package to the same published
version:

```bash
dotnet add package Nodal.Neo4j --version 0.1.0-alpha.2
dotnet add package Nodal.Migrations --version 0.1.0-alpha.2
```

The equivalent project file is:

```xml title="SocialGraph.csproj"
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="Nodal.Neo4j" Version="0.1.0-alpha.2" />
  <PackageReference Include="Nodal.Migrations" Version="0.1.0-alpha.2" />
</ItemGroup>
```

Replace `Nodal.Neo4j` with `Nodal.TigerGraph` for a TigerGraph host.

## Provider and context lifetimes

| Object | Recommended lifetime | Reason |
| --- | --- | --- |
| `Neo4jProvider` | Application singleton | Owns the official pooled Bolt driver |
| `TigerGraphProvider` | Application singleton | Reuses an externally managed `HttpClient` and its handler pool |
| Your `NodalContext` subclass | Scoped or one unit of work | Owns identity resolution, tracking, and pending mutations |

Do not construct a new Neo4j provider for every request. Do not share one
`NodalContext` across concurrent requests.

## Console and worker hosts

Keep the provider for the application lifetime and create a context for each
unit of work:

```csharp title="Program.cs"
using Nodal.Neo4j;

await using var provider = new Neo4jProvider(new Neo4jOptions
{
    Endpoint = new Uri("neo4j://localhost:7687"),
    Username = "neo4j",
    Password = Environment.GetEnvironmentVariable("NODAL_NEO4J_PASSWORD")
        ?? throw new InvalidOperationException("NODAL_NEO4J_PASSWORD is required."),
    Database = "neo4j"
});

var context = new SocialGraphContext(provider);
var people = await context.People.Query().Take(20).AsNoTracking().ToListAsync();
```

## ASP.NET Core and standard dependency injection

Nodal does not hide provider construction behind a global service locator.
Register providers with the standard .NET container so their lifetimes remain
visible and testable.

```csharp title="Program.cs - Neo4j"
using Nodal.Core.Execution;
using Nodal.Neo4j;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<Neo4jProvider>(_ =>
{
    var section = builder.Configuration.GetRequiredSection("Nodal:Neo4j");
    return new Neo4jProvider(new Neo4jOptions
    {
        Endpoint = new Uri(section["Endpoint"]!),
        Username = section["Username"]!,
        Password = section["Password"]!,
        Database = section["Database"]
    });
});
builder.Services.AddSingleton<IGraphProvider>(services =>
    services.GetRequiredService<Neo4jProvider>());
builder.Services.AddScoped<SocialGraphContext>();
```

The host disposes the singleton Neo4j provider during graceful shutdown. Each
request receives an independent context and change tracker.

```csharp title="Program.cs - TigerGraph"
using Nodal.Core.Execution;
using Nodal.TigerGraph;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient("Nodal.TigerGraph");

builder.Services.AddSingleton<TigerGraphProvider>(services =>
{
    var section = builder.Configuration.GetRequiredSection("Nodal:TigerGraph");
    var client = services.GetRequiredService<IHttpClientFactory>()
        .CreateClient("Nodal.TigerGraph");
    var endpoint = new Uri(section["Endpoint"]!);
    client.BaseAddress = endpoint;

    return new TigerGraphProvider(
        client,
        new TigerGraphOptions
        {
            Endpoint = endpoint,
            AccessToken = section["AccessToken"]
        },
        graphName: section["Graph"]!);
});
builder.Services.AddSingleton<IGraphProvider>(services =>
    services.GetRequiredService<TigerGraphProvider>());
builder.Services.AddScoped<SocialGraphContext>();
```

TigerGraph migration execution additionally requires an
`ITigerGraphAdministrativeTransport`; ordinary query and mutation scenarios do
not require that privileged channel.

## Keep credentials out of source control

Use environment variables, a secret manager, or .NET user secrets during local
development:

```bash
dotnet user-secrets init
dotnet user-secrets set "Nodal:Neo4j:Password" "local-password"
dotnet user-secrets set "Nodal:TigerGraph:AccessToken" "local-token"
```

Non-sensitive endpoints and database names can remain in `appsettings.json`.
Never commit production passwords or access tokens.

## Continue

Define the portable POCO model and execute the first query in
[Getting started](./getting-started). Check the
[compatibility matrix](./providers/compatibility) before enabling
provider-specific analytics or migration administration.
