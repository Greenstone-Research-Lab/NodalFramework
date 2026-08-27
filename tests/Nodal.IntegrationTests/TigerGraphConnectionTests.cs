using System.Net.Http.Headers;
using System.Text.Json;
using Nodal.Core;
using Nodal.Core.Metadata;
using Nodal.Core.Migrations;
using Nodal.Core.Query;
using Nodal.TigerGraph;
using Nodal.TigerGraph.Extensions;

namespace Nodal.IntegrationTests;

public sealed class TigerGraphConnectionTests
{
    [TigerGraphMigrationIntegrationFact]
    [Trait("Category", "Integration")]
    [Trait("Provider", "TigerGraph")]
    public async Task MigrationJobIsAppliedCleanedRestartSafeAndRevertedOnLiveServer()
    {
        var endpoint = new Uri(Environment.GetEnvironmentVariable("NODAL_TIGERGRAPH_ENDPOINT")!, UriKind.Absolute);
        var graphName = Environment.GetEnvironmentVariable("NODAL_TIGERGRAPH_GRAPH")!;
        var options = CreateOptions(endpoint);
        using var faultHandler = new MigrationHistoryFaultHandler(new HttpClientHandler());
        using var httpClient = new HttpClient(faultHandler) { BaseAddress = endpoint };
        var processOptions = new TigerGraphGsqlProcessOptions
        {
            FileName = Environment.GetEnvironmentVariable("NODAL_TIGERGRAPH_GSQL_FILE")!,
            PrefixArguments = JsonSerializer.Deserialize<string[]>(
                Environment.GetEnvironmentVariable("NODAL_TIGERGRAPH_GSQL_PREFIX")!)!,
            Username = Environment.GetEnvironmentVariable("NODAL_TIGERGRAPH_USERNAME"),
            Password = Environment.GetEnvironmentVariable("NODAL_TIGERGRAPH_PASSWORD"),
            AccessToken = Environment.GetEnvironmentVariable("NODAL_TIGERGRAPH_ACCESS_TOKEN"),
            GraphName = graphName,
            VerifiedServerVersion = "4.2.4 Community",
        };
        var controlPlane = new CountingControlPlane(new TigerGraphGsqlProcessTransport(processOptions));
        var executor = new TigerGraphMigrationExecutor(httpClient, options, graphName, controlPlane);
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var vertexType = $"NodalM4_{suffix}";
        var dialect = new TigerGraphMigrationDialect(graphName);
        var upCommands = dialect.Compile(
            [new CreateNodeTypeOperation(vertexType, "Id", typeof(string), [new GraphSchemaProperty("Score", typeof(double))])]);
        var downCommands = dialect.Compile([new DropNodeTypeOperation(vertexType)]);
        var up = new MigrationExecution($"m4_{suffix}", $"checksum_{suffix}", upCommands);
        var down = new MigrationExecution(up.Id, up.Checksum, downCommands);

        try
        {
            controlPlane.CancelNextRun = true;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await executor.ApplyAsync(up));
            Assert.False(await controlPlane.SchemaJobExistsAsync(graphName, JobName(upCommands)));
            await new TigerGraphMigrationRecovery(executor.Journal)
                .ConfirmSchemaNotAppliedAsync(up.Id);

            faultHandler.FailNextHistoryWrite = true;
            await Assert.ThrowsAsync<HttpRequestException>(
                async () => await executor.ApplyAsync(up));
            Assert.True(await SchemaContainsAsync(httpClient, options, graphName, vertexType));
            Assert.False(await controlPlane.SchemaJobExistsAsync(graphName, JobName(upCommands)));
            var schemaRunsAfterHistoryFailure = controlPlane.ExecutedSchemaRuns;

            await executor.ApplyAsync(up);
            Assert.Equal(schemaRunsAfterHistoryFailure, controlPlane.ExecutedSchemaRuns);
            var callsAfterApply = controlPlane.ExecutedCommands;

            var restarted = new TigerGraphMigrationExecutor(httpClient, options, graphName, controlPlane);
            await restarted.ApplyAsync(up);
            Assert.Equal(callsAfterApply, controlPlane.ExecutedCommands);

            await restarted.RevertAsync(down);
            Assert.False(await SchemaContainsAsync(httpClient, options, graphName, vertexType));
            Assert.False(await controlPlane.SchemaJobExistsAsync(graphName, JobName(downCommands)));
        }
        finally
        {
            if (await SchemaContainsAsync(httpClient, options, graphName, vertexType))
            {
                foreach (var command in downCommands)
                {
                    try { await controlPlane.ExecuteAsync(command); }
                    catch (InvalidOperationException) { }
                }
            }
        }
    }
    [TigerGraphIntegrationFact]
    [Trait("Category", "Integration")]
    [Trait("Provider", "TigerGraph")]
    public async Task UnitOfWorkCreatesReadsAndUpdatesThroughLiveRestConnection()
    {
        var endpoint = new Uri(
            Environment.GetEnvironmentVariable("NODAL_TIGERGRAPH_ENDPOINT")!,
            UriKind.Absolute);
        var options = CreateOptions(endpoint);
        var graphName = Environment.GetEnvironmentVariable("NODAL_TIGERGRAPH_GRAPH")!;
        using var httpClient = new HttpClient { BaseAddress = endpoint };
        var provider = new TigerGraphProvider(
            httpClient,
            options,
            graphName);
        var context = new SocialContext(provider);
        var suffix = Guid.NewGuid().ToString("N");
        var source = new Person($"nodal-source-{suffix}", "Ada");
        var target = new Person($"nodal-target-{suffix}", "Alan");
        var relation = new Knows(2020);

        try
        {
            context.People.Add(source);
            context.People.Add(target);
            context.Friendships.Connect(source, relation, target);
            var result = await context.SaveChangesAsync();

            Assert.True(result.IsAtomic);
            Assert.Equal(2, result.AffectedNodes);
            Assert.Equal(1, result.AffectedRelations);

            var readContext = new SocialContext(provider);
            var storedSource = Assert.Single(await readContext.People
                .Match(person => person.Id == source.Id)
                .ToListAsync());
            var storedPath = Assert.Single(await readContext.People
                .Match(person => person.Id == source.Id)
                .TraversePath(readContext.Friendships)
                .ToListAsync());
            var detachedContext = new SocialContext(provider);
            string[] selectedIds = [source.Id, target.Id];
            var paged = await detachedContext.People.Query()
                .Where(person => selectedIds.Contains(person.Id) && person.Name.StartsWith("Ad"))
                .OrderBy(person => person.Name)
                .Skip(0)
                .Take(1)
                .AsNoTracking()
                .ToListAsync();
            var raw = await detachedContext.Database.QueryRawAsync<Person>(
                $"INTERPRET QUERY (STRING id) FOR GRAPH {graphName} {{ result = SELECT node FROM Person:node WHERE node.Id == id; PRINT result; }}",
                new Dictionary<string, object?> { ["id"] = target.Id });
            var subgraph = await detachedContext.People.Match(person => person.Id == source.Id)
                .Traverse(detachedContext.Friendships)
                .WithoutCycles()
                .ToSubgraphAsync();
            var count = await detachedContext.People.Query()
                .Where(person => selectedIds.Contains(person.Id))
                .CountAsync();
            Assert.Equal("Ada", storedSource.Name);
            Assert.Equal(target.Id, storedPath.Target.Id);
            Assert.Equal(2020, storedPath.Relation.SinceYear);
            Assert.Equal("Ada", Assert.Single(paged).Name);
            Assert.Empty(detachedContext.ChangeTracker.Entries());
            Assert.Equal("Alan", Assert.Single(raw).Name);
            Assert.Equal(2, subgraph.Nodes.Count);
            Assert.Single(subgraph.RelationRecords);
            Assert.Equal(2, count);

            source.Name = "Ada Lovelace";
            relation.SinceYear = 2025;
            context.People.Update(source);
            context.Friendships.Update(source, relation, target);
            var updated = await context.SaveChangesAsync();

            Assert.Equal(1, updated.AffectedNodes);
            Assert.Equal(1, updated.AffectedRelations);
            var verificationContext = new SocialContext(provider);
            var updatedSource = Assert.Single(await verificationContext.People
                .Match(person => person.Id == source.Id)
                .ToListAsync());
            var updatedPath = Assert.Single(await verificationContext.People
                .Match(person => person.Id == source.Id)
                .TraversePath(verificationContext.Friendships)
                .ToListAsync());
            Assert.Equal("Ada Lovelace", updatedSource.Name);
            Assert.Equal(2025, updatedPath.Relation.SinceYear);
        }
        finally
        {
            await DeleteVertexAsync(httpClient, options, graphName, source.Id);
            await DeleteVertexAsync(httpClient, options, graphName, target.Id);
        }
    }

    [TigerGraphIntegrationFact]
    [Trait("Category", "Integration")]
    [Trait("Provider", "TigerGraph")]
    public async Task InvalidEdgeRollsBackVerticesInAtomicRestBatch()
    {
        var endpoint = new Uri(
            Environment.GetEnvironmentVariable("NODAL_TIGERGRAPH_ENDPOINT")!,
            UriKind.Absolute);
        var options = CreateOptions(endpoint);
        var graphName = Environment.GetEnvironmentVariable("NODAL_TIGERGRAPH_GRAPH")!;
        using var httpClient = new HttpClient { BaseAddress = endpoint };
        var provider = new TigerGraphProvider(
            httpClient,
            options,
            graphName);
        var context = new FailureContext(provider);
        var suffix = Guid.NewGuid().ToString("N");
        var source = new Person($"nodal-rollback-source-{suffix}", "Source");
        var target = new Person($"nodal-rollback-target-{suffix}", "Target");

        try
        {
            context.People.Add(source);
            context.People.Add(target);
            context.InvalidRelations.Connect(source, new MissingRelation(), target);

            await Assert.ThrowsAnyAsync<Exception>(async () => await context.SaveChangesAsync());

            Assert.False(await VertexExistsAsync(httpClient, options, graphName, source.Id));
            Assert.False(await VertexExistsAsync(httpClient, options, graphName, target.Id));
        }
        finally
        {
            await DeleteVertexAsync(httpClient, options, graphName, source.Id);
            await DeleteVertexAsync(httpClient, options, graphName, target.Id);
        }
    }

    [TigerGraphIntegrationFact]
    [Trait("Category", "Integration")]
    [Trait("Provider", "TigerGraph")]
    public async Task DistinctNodeQueryUsesTigerGraphVertexSetSemantics()
    {
        var endpoint = new Uri(Environment.GetEnvironmentVariable("NODAL_TIGERGRAPH_ENDPOINT")!, UriKind.Absolute);
        var graphName = Environment.GetEnvironmentVariable("NODAL_TIGERGRAPH_GRAPH")!;
        var options = CreateOptions(endpoint);
        using var httpClient = new HttpClient { BaseAddress = endpoint };
        var provider = new TigerGraphProvider(httpClient, options, graphName);
        var context = new SocialContext(provider);
        var suffix = Guid.NewGuid().ToString("N");
        var first = new Person($"nodal-distinct-first-{suffix}", "First");
        var second = new Person($"nodal-distinct-second-{suffix}", "Second");
        var shared = new Person($"nodal-distinct-shared-{suffix}", "Shared");

        try
        {
            context.People.Add(first);
            context.People.Add(second);
            context.People.Add(shared);
            context.Friendships.Connect(first, new Knows(2020), shared);
            context.Friendships.Connect(second, new Knows(2021), shared);
            await context.SaveChangesAsync();

            string[] sourceIds = [first.Id, second.Id];
            var readContext = new SocialContext(provider);
            var result = await readContext.People.Query()
                .Where(person => sourceIds.Contains(person.Id))
                .Traverse(readContext.Friendships)
                .Distinct()
                .ToListAsync();

            Assert.Equal(shared.Id, Assert.Single(result).Id);
        }
        finally
        {
            await DeleteVertexAsync(httpClient, options, graphName, first.Id);
            await DeleteVertexAsync(httpClient, options, graphName, second.Id);
            await DeleteVertexAsync(httpClient, options, graphName, shared.Id);
        }
    }

    [TigerGraphIntegrationFact]
    [Trait("Category", "Integration")]
    [Trait("Provider", "TigerGraph")]
    public async Task SyntaxV2RowProjectionGroupsAggregatesAndMaterializesRows()
    {
        var endpoint = new Uri(Environment.GetEnvironmentVariable("NODAL_TIGERGRAPH_ENDPOINT")!, UriKind.Absolute);
        var graphName = Environment.GetEnvironmentVariable("NODAL_TIGERGRAPH_GRAPH")!;
        var options = CreateOptions(endpoint);
        using var httpClient = new HttpClient { BaseAddress = endpoint };
        var provider = new TigerGraphProvider(httpClient, options, graphName);
        var context = new SocialContext(provider);
        var suffix = Guid.NewGuid().ToString("N");
        var name = $"Nodal row {suffix}";
        var first = new Person($"nodal-row-first-{suffix}", name);
        var second = new Person($"nodal-row-second-{suffix}", name);

        try
        {
            context.People.Add(first);
            context.People.Add(second);
            await context.SaveChangesAsync();

            var rows = await new SocialContext(provider).People.Query()
                .Where(person => person.Name == name)
                .ToRows()
                .Select("name", person => person.Name)
                .Count("personCount")
                .Having("personCount", GraphComparisonOperator.GreaterThan, 1)
                .OrderByDescending("personCount")
                .Take(1)
                .ToListAsync();

            var row = Assert.Single(rows);
            Assert.Equal(name, row.Get<string>("name"));
            Assert.Equal(2L, row.Get<long>("personCount"));
        }
        finally
        {
            await DeleteVertexAsync(httpClient, options, graphName, first.Id);
            await DeleteVertexAsync(httpClient, options, graphName, second.Id);
        }
    }

    [TigerGraphMigrationIntegrationFact]
    [Trait("Category", "Integration")]
    [Trait("Provider", "TigerGraph")]
    public async Task CorrelatedExistenceExtensionInstallsAndExecutesOnLiveServer()
    {
        var endpoint = new Uri(Environment.GetEnvironmentVariable("NODAL_TIGERGRAPH_ENDPOINT")!, UriKind.Absolute);
        var graphName = Environment.GetEnvironmentVariable("NODAL_TIGERGRAPH_GRAPH")!;
        var baseOptions = CreateOptions(endpoint);
        var options = new TigerGraphOptions
        {
            Endpoint = baseOptions.Endpoint,
            AccessToken = baseOptions.AccessToken,
            Username = baseOptions.Username,
            Password = baseOptions.Password,
            GeneratedQueryExtensions = new HashSet<TigerGraphQueryExtensionFeature>
            {
                TigerGraphQueryExtensionFeature.CorrelatedExistence,
            },
        };
        var processOptions = new TigerGraphGsqlProcessOptions
        {
            FileName = Environment.GetEnvironmentVariable("NODAL_TIGERGRAPH_GSQL_FILE")!,
            PrefixArguments = JsonSerializer.Deserialize<string[]>(
                Environment.GetEnvironmentVariable("NODAL_TIGERGRAPH_GSQL_PREFIX")!)!,
            Username = options.Username,
            Password = options.Password,
            AccessToken = options.AccessToken,
            GraphName = graphName,
            VerifiedServerVersion = "4.2.4 Community",
        };
        var controlPlane = new TigerGraphGsqlProcessTransport(processOptions);
        using var httpClient = new HttpClient { BaseAddress = endpoint };
        var provider = new TigerGraphProvider(httpClient, options, graphName, controlPlane);
        var context = new SocialContext(provider);
        var suffix = Guid.NewGuid().ToString("N");
        var source = new Person($"nodal-exists-source-{suffix}", "Source");
        var target = new Person($"nodal-exists-target-{suffix}", "Target");
        var isolated = new Person($"nodal-exists-isolated-{suffix}", "Isolated");
        var installedQueries = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            context.People.Add(source);
            context.People.Add(target);
            context.People.Add(isolated);
            context.Friendships.Connect(source, new Knows(2020), target);
            await context.SaveChangesAsync();

            var readContext = new SocialContext(provider);
            var exists = readContext.People
                .Match(person => person.Id == source.Id)
                .WhereExists(
                    readContext.Friendships,
                    person => person.Id == target.Id,
                    relation => relation.SinceYear >= 2020);
            var missing = readContext.People
                .Match(person => person.Id == isolated.Id)
                .WhereNotExists(readContext.Friendships, person => person.Id == target.Id);
            installedQueries.Add(provider.QueryCompiler.Compile(exists.ToQueryModel()).Route!.Split('/')[^1]);
            installedQueries.Add(provider.QueryCompiler.Compile(missing.ToQueryModel()).Route!.Split('/')[^1]);

            Assert.Equal(source.Id, Assert.Single(await exists.ToListAsync()).Id);
            Assert.Equal(isolated.Id, Assert.Single(await missing.ToListAsync()).Id);
        }
        finally
        {
            await DeleteVertexAsync(httpClient, options, graphName, source.Id);
            await DeleteVertexAsync(httpClient, options, graphName, target.Id);
            await DeleteVertexAsync(httpClient, options, graphName, isolated.Id);
            foreach (var queryName in installedQueries)
            {
                try
                {
                    await controlPlane.ExecuteAsync(new MigrationCommand(
                        $"DROP QUERY {queryName}",
                        false,
                        MigrationCommandKind.QueryDefinition));
                }
                catch (InvalidOperationException)
                {
                }
            }
        }
    }


    private static TigerGraphOptions CreateOptions(Uri endpoint) => new()
    {
        Endpoint = endpoint,
        AccessToken = Environment.GetEnvironmentVariable("NODAL_TIGERGRAPH_ACCESS_TOKEN"),
        Username = Environment.GetEnvironmentVariable("NODAL_TIGERGRAPH_USERNAME"),
        Password = Environment.GetEnvironmentVariable("NODAL_TIGERGRAPH_PASSWORD"),
    };

    private static string JobName(IReadOnlyList<MigrationCommand> commands) =>
        commands[1].Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Last();

    private static async Task<bool> SchemaContainsAsync(
        HttpClient httpClient,
        TigerGraphOptions options,
        string graphName,
        string typeName)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"gsql/v1/schema?graph={Uri.EscapeDataString(graphName)}");
        ApplyAuthentication(request, options);
        using var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadAsStringAsync();
        return payload.Contains($"\"Name\":\"{typeName}\"", StringComparison.Ordinal);
    }

    private static async Task DeleteVertexAsync(
        HttpClient httpClient,
        TigerGraphOptions options,
        string graphName,
        string identity)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"restpp/graph/{Uri.EscapeDataString(graphName)}/vertices/Person/{Uri.EscapeDataString(identity)}");
        ApplyAuthentication(request, options);
        using var response = await httpClient.SendAsync(request);
        if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }
    }

    private static async Task<bool> VertexExistsAsync(
        HttpClient httpClient,
        TigerGraphOptions options,
        string graphName,
        string identity)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"restpp/graph/{Uri.EscapeDataString(graphName)}/vertices/Person/{Uri.EscapeDataString(identity)}");
        ApplyAuthentication(request, options);
        using var response = await httpClient.SendAsync(request);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(payload);
        return ContainsVertex(document.RootElement, identity);
    }

    private static void ApplyAuthentication(HttpRequestMessage request, TigerGraphOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.AccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.AccessToken);
            return;
        }

        var credentials = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{options.Username}:{options.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    private static bool ContainsVertex(JsonElement element, string identity)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("v_id", out var id) && id.GetString() == identity)
            {
                return true;
            }

            return element.EnumerateObject().Any(property => ContainsVertex(property.Value, identity));
        }

        return element.ValueKind == JsonValueKind.Array &&
            element.EnumerateArray().Any(item => ContainsVertex(item, identity));
    }

    private sealed class SocialContext(TigerGraphProvider provider) : NodalContext(provider)
    {
        public GraphSet<Person> People => Set<Person>();

        public RelationSet<Person, Knows, Person> Friendships => Relations<Person, Knows, Person>();
    }

    private sealed class FailureContext(TigerGraphProvider provider) : NodalContext(provider)
    {
        public GraphSet<Person> People => Set<Person>();

        public RelationSet<Person, MissingRelation, Person> InvalidRelations =>
            Relations<Person, MissingRelation, Person>();
    }

    [GraphNode("Person")]
    private sealed class Person(string id, string name)
    {
        [GraphKey]
        public string Id { get; } = id;

        public string Name { get; set; } = name;
    }

    [GraphRelation("KNOWS")]
    private sealed class Knows(int sinceYear)
    {
        public int SinceYear { get; set; } = sinceYear;
    }

    [GraphRelation("NODAL_INTENTIONALLY_MISSING_EDGE")]
    private sealed class MissingRelation;

    private sealed class CountingControlPlane(ITigerGraphAdministrativeControlPlane inner)
        : ITigerGraphAdministrativeControlPlane
    {
        public int ExecutedCommands { get; private set; }
        public int ExecutedSchemaRuns { get; private set; }
        public bool CancelNextRun { get; set; }

        public ValueTask<TigerGraphAdministrativeCapabilities> DiscoverCapabilitiesAsync(
            string graphName,
            CancellationToken cancellationToken = default) =>
            inner.DiscoverCapabilitiesAsync(graphName, cancellationToken);

        public ValueTask<bool> SchemaJobExistsAsync(
            string graphName,
            string jobName,
            CancellationToken cancellationToken = default) =>
            inner.SchemaJobExistsAsync(graphName, jobName, cancellationToken);

        public ValueTask<IAsyncDisposable> AcquireMigrationLockAsync(
            string graphName,
            CancellationToken cancellationToken = default) =>
            inner.AcquireMigrationLockAsync(graphName, cancellationToken);

        public async ValueTask ExecuteAsync(
            MigrationCommand command,
            CancellationToken cancellationToken = default)
        {
            if (CancelNextRun &&
                command.Text.StartsWith("RUN SCHEMA_CHANGE JOB", StringComparison.Ordinal))
            {
                CancelNextRun = false;
                throw new OperationCanceledException(cancellationToken);
            }

            ExecutedCommands++;
            await inner.ExecuteAsync(command, cancellationToken);
            if (command.Text.StartsWith("RUN SCHEMA_CHANGE JOB", StringComparison.Ordinal))
            {
                ExecutedSchemaRuns++;
            }
        }
    }

    private sealed class MigrationHistoryFaultHandler(HttpMessageHandler innerHandler)
        : DelegatingHandler(innerHandler)
    {
        public bool FailNextHistoryWrite { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (FailNextHistoryWrite &&
                request.Method == HttpMethod.Post &&
                request.Content is not null &&
                (await request.Content.ReadAsStringAsync(cancellationToken))
                    .Contains("__NodalMigration", StringComparison.Ordinal))
            {
                FailNextHistoryWrite = false;
                return new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("injected history failure"),
                };
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }
}
