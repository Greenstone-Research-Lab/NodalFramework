using Nodal.Core.Execution;
using Nodal.Core.Metadata;
using Nodal.Core.Migrations;
using Nodal.Core.Providers;
using Nodal.Core.Query;

namespace Nodal.Core.Tests;

public sealed class GraphQueryExecutionTests
{
    [Fact]
    public async Task TerminalProjectionStreamingCountAndSubgraphApisExecute()
    {
        var provider = new QueryProvider();
        var context = new QueryContext(provider);

        Assert.Equal("Ada", (await context.People.Query().FirstAsync()).Name);
        Assert.Equal("Ada", (await context.People.Query().FirstOrDefaultAsync())!.Name);
        Assert.True(await context.People.Query().AnyAsync());
        Assert.Equal(2, await context.People.Query().CountAsync());
        Assert.Equal(1, await context.People.Query().Take(1).CountAsync());
        var onlyAda = await context.People.Match(person => person.Id == "person-1").SingleAsync();
        Assert.Equal("Ada", onlyAda.Name);
        Assert.Equal("Ada", (await context.People.Match(person => person.Id == "person-1")
            .SingleOrDefaultAsync())!.Name);

        var projected = await context.People.Query().Select(person => person.Name).ToListAsync();
        Assert.Equal(["Ada", "Alan"], projected);
        var projectedStream = new List<string>();
        await foreach (var name in context.People.Query().Select(person => person.Name).AsAsyncEnumerable())
        {
            projectedStream.Add(name);
        }
        Assert.Equal(["Ada", "Alan"], projectedStream);

        var stream = new List<string>();
        await foreach (var person in context.People.Query().AsAsyncEnumerable())
        {
            stream.Add(person.Name);
        }
        Assert.Equal(["Ada", "Alan"], stream);

        var subgraph = await context.People.Query().ToSubgraphAsync();
        Assert.Single(subgraph.RelationRecords);
        Assert.Equal(2, subgraph.Nodes.Count);
    }

    [Fact]
    public async Task EmptyTerminalsRawFacadeReloadAndCompiledOverloadsAreCovered()
    {
        var provider = new QueryProvider { Empty = true };
        var context = new QueryContext(provider);

        Assert.Null(await context.People.Query().FirstOrDefaultAsync());
        Assert.Null(await context.People.Query().SingleOrDefaultAsync());
        Assert.False(await context.People.Query().AnyAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await context.People.Query().FirstAsync());

        provider.Empty = false;
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await context.People.Query().SingleAsync());
        var raw = await context.Database.QueryRawAsync<Person>("RAW", new Dictionary<string, object?>());
        Assert.Equal(2, raw.Count);
        Assert.Equal(2, (await context.Database.ExecuteRawAsync("RAW")).Nodes.Count);
        Assert.Equal(2, (await context.Database.CypherAsync("RAW")).Nodes.Count);
        Assert.Equal(2, (await context.Database.GsqlAsync("RAW")).Nodes.Count);
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await context.Database.ExecuteRawAsync(" "));

        var tracked = new Person { Id = "person-1", Name = "Old" };
        var reloadContext = new QueryContext(provider);
        reloadContext.People.Attach(tracked);
        provider.Reload = true;
        await reloadContext.People.ReloadAsync(tracked);
        Assert.Equal("Reloaded", tracked.Name);
        Assert.Equal("Reloaded", reloadContext.Entry(tracked).OriginalValues["Name"]);

        var noParameter = NodalCompiledQuery.Compile((QueryContext database) => database.People.Query());
        var twoParameters = NodalCompiledQuery.Compile((QueryContext database, string id, string name) =>
            database.People.Match(person => person.Id == id && person.Name == name));
        Assert.Equal("Person", noParameter(context).ToQueryModel().NodeType);
        Assert.Equal(2, twoParameters(context, "person-1", "Ada").ToQueryModel().Parameters.Count);
        Assert.Throws<ArgumentNullException>(() =>
            NodalCompiledQuery.Compile<QueryContext, Person>(null!));

        provider.MissingCount = true;
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await new QueryContext(provider).People.Query().CountAsync());

        var standalone = new GraphSet<Person>().Query();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await standalone.CountAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await standalone.ToSubgraphAsync());
    }

    [Fact]
    public async Task UnsupportedCorrelatedSubqueryFailsBeforeCompilerOrTransportExecution()
    {
        var provider = new QueryProvider();
        var context = new QueryContext(provider);

        var exception = await Assert.ThrowsAsync<NodalCapabilityNotSupportedException>(async () =>
            await context.People.Query().WhereExists(context.Friendships).ToListAsync());

        Assert.Equal("NODAL-QUERY-CORRELATED-SUBQUERY", exception.CapabilityCode);
        Assert.False(provider.Compiled);
        Assert.False(provider.Executed);
    }

    [Fact]
    public async Task RowProjectionMaterializesNamedProviderSideValues()
    {
        var provider = new QueryProvider
        {
            RowValues = new Dictionary<string, object?>
            {
                ["personCount"] = 2L,
                ["averageScore"] = 4.5d,
            },
        };
        var context = new QueryContext(provider);

        var row = Assert.Single(await context.People.Query()
            .ToRows()
            .Count("personCount")
            .Average("averageScore", person => person.Score)
            .ToListAsync());

        Assert.Equal(2L, row.Get<long>("personCount"));
        Assert.Equal(4.5d, row.Get<double>("averageScore"));
        Assert.Throws<KeyNotFoundException>(() => row.Get<int>("missing"));
    }

    [Fact]
    public async Task RowsConvertProviderValuesAndSetQueriesExecuteThroughTheContext()
    {
        var provider = new QueryProvider
        {
            RowValues = new Dictionary<string, object?>
            {
                ["count"] = 2,
                ["rank"] = 1,
                ["optional"] = null,
            },
        };
        var context = new QueryContext(provider);

        var row = Assert.Single(await context.People.Query().ToRows().Count("count").ToListAsync());
        Assert.Equal(2L, row.Get<long>("count"));
        Assert.Equal(PersonRank.Established, row.Get<PersonRank>("rank"));
        Assert.Null(row.Get<string>("optional"));
        Assert.Equal(3, row.Values.Count);

        var union = await context.People.Match(person => person.Id == "person-1")
            .UnionAll(context.People.Match(person => person.Name == "Alan"))
            .OrderByDescending(person => person.Name)
            .ToListAsync();

        Assert.Equal(2, union.Count);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await new GraphSet<Person>().Query().Union(new GraphSet<Person>().Query()).ToListAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await new GraphSet<Person>().Query().ToRows().Count("count").ToListAsync());
    }

    private sealed class QueryContext(QueryProvider provider) : NodalContext(provider)
    {
        public GraphSet<Person> People => Set<Person>();

        public RelationSet<Person, Knows, Person> Friendships => Relations<Person, Knows, Person>();
    }

    [GraphNode("Person")]
    private sealed class Person
    {
        [GraphKey]
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public double Score { get; set; }
    }

    [GraphRelation("KNOWS", Directed = false)]
    private sealed class Knows;

    private enum PersonRank
    {
        New = 0,
        Established = 1,
    }

    private sealed class QueryProvider : IGraphProvider, IGraphQueryCapabilityProvider, IGraphQueryCompiler, IGraphCommandExecutor
    {
        public bool Empty { get; set; }

        public bool Reload { get; set; }

        public bool MissingCount { get; set; }

        public bool Compiled { get; private set; }

        public bool Executed { get; private set; }

        public IReadOnlyDictionary<string, object?>? RowValues { get; init; }

        private GraphQueryModel? Query { get; set; }

        public IGraphQueryCompiler QueryCompiler => this;

        public IGraphCommandExecutor CommandExecutor => this;

        public IGraphResultMaterializer ResultMaterializer { get; } = new JsonGraphResultMaterializer();

        public GraphQueryCapabilities QueryCapabilities { get; } = new()
        {
            ProviderName = "QueryProvider",
            TestedProviderVersion = "test",
            Features = GraphQueryCapability.ServerSideProjection |
                GraphQueryCapability.Aggregation |
                GraphQueryCapability.SetOperations,
        };

        public GraphCommand Compile(GraphQueryModel query)
        {
            Compiled = true;
            Query = query;
            return new GraphCommand("QUERY", new Dictionary<string, object?>());
        }

        public ValueTask<GraphQueryResult> ExecuteAsync(
            GraphCommand command,
            CancellationToken cancellationToken = default)
        {
            Executed = true;
            if (command.Text == "RAW")
            {
                return ValueTask.FromResult(CreateResult(2));
            }

            if (Query?.Projection == GraphQueryProjection.Count)
            {
                if (MissingCount)
                {
                    return ValueTask.FromResult(new GraphQueryResult([]));
                }
                return ValueTask.FromResult(new GraphQueryResult(
                    [], Scalars: new Dictionary<string, object?> { ["nodal_count"] = Empty ? 0L : 2L }));
            }

            if (Query?.Projection == GraphQueryProjection.Row)
            {
                return ValueTask.FromResult(new GraphQueryResult(
                    [],
                    Rows:
                    [
                        new GraphResultRow(null, RowValues ?? new Dictionary<string, object?>()),
                    ]));
            }

            var count = Empty ? 0 : Query?.Limit is int limit ? Math.Min(limit, 2) : 2;
            if (Query?.Predicate is not null && count > 0)
            {
                count = 1;
            }
            return ValueTask.FromResult(CreateResult(count));
        }

        private GraphQueryResult CreateResult(int count)
        {
            var names = Reload ? new[] { "Reloaded", "Alan" } : new[] { "Ada", "Alan" };
            var nodes = Enumerable.Range(0, count).Select(index => new GraphNodeRecord(
                "Person",
                $"person-{index + 1}",
                new Dictionary<string, object?>
                {
                    ["Id"] = $"person-{index + 1}",
                    ["Name"] = names[index],
                })).ToArray();
            GraphRelationRecord[] relations = count == 0
                ? []
                : new[]
                {
                    new GraphRelationRecord("KNOWS", "edge-1", "person-1", "person-2",
                        new Dictionary<string, object?>()),
                };
            return new GraphQueryResult(nodes, relations);
        }
    }
}
