using Nodal.Core.Analytics;
using Nodal.Samples.SocialGraph;
using Nodal.TigerGraph;

var endpoint = new Uri(ReadSetting("NODAL_TIGERGRAPH_ENDPOINT", "http://localhost:14240/"));
var accessToken = Environment.GetEnvironmentVariable("NODAL_TIGERGRAPH_ACCESS_TOKEN");
var analyticsQueries = new Dictionary<GraphAnalyticsAlgorithm, string>();
AddAnalyticsQuery(GraphAnalyticsAlgorithm.ShortestPath, "NODAL_TIGERGRAPH_SHORTEST_PATH_QUERY");
AddAnalyticsQuery(GraphAnalyticsAlgorithm.PageRank, "NODAL_TIGERGRAPH_PAGERANK_QUERY");
AddAnalyticsQuery(GraphAnalyticsAlgorithm.Louvain, "NODAL_TIGERGRAPH_LOUVAIN_QUERY");
using var httpClient = new HttpClient { BaseAddress = endpoint };
var provider = new TigerGraphProvider(
    httpClient,
    new TigerGraphOptions
    {
        Endpoint = endpoint,
        AccessToken = accessToken,
        Username = ReadSetting("NODAL_TIGERGRAPH_USERNAME", "tigergraph"),
        Password = ReadSetting("NODAL_TIGERGRAPH_PASSWORD", "tigergraph"),
        AnalyticsQueries = analyticsQueries,
    },
    ReadSetting("NODAL_TIGERGRAPH_GRAPH", "NodalQa"));

var result = await SocialGraphDemo.RunAsync(provider);

Console.WriteLine("Nodal Framework / TigerGraph demo completed.");
PrintResult(result);

var analyticsContext = new SocialGraphContext(provider);
if (analyticsQueries.ContainsKey(GraphAnalyticsAlgorithm.ShortestPath))
{
    var shortest = await analyticsContext.People.Match(person => person.Id == result.SourceId)
        .ShortestPathTo(
            analyticsContext.People.Match(person => person.Id == result.TargetId),
            analyticsContext.Friendships)
        .SingleAsync();
    Console.WriteLine($"Shortest path : {shortest.HopCount} hop(s)");
}
if (analyticsQueries.ContainsKey(GraphAnalyticsAlgorithm.PageRank))
{
    var rows = await analyticsContext.People.Query().Analyze(analyticsContext.Friendships)
        .PageRank(new PageRankOptions()).Top(5).ToListAsync();
    Console.WriteLine($"PageRank rows  : {rows.Count}");
}
if (analyticsQueries.ContainsKey(GraphAnalyticsAlgorithm.Louvain))
{
    var rows = await analyticsContext.People.Query().Analyze(analyticsContext.Friendships)
        .Louvain(new LouvainOptions()).ToListAsync();
    Console.WriteLine($"Louvain rows   : {rows.Count}");
}

static string ReadSetting(string name, string fallback) =>
    Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : fallback;

void AddAnalyticsQuery(GraphAnalyticsAlgorithm algorithm, string variable)
{
    if (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } queryName)
    {
        analyticsQueries[algorithm] = queryName;
    }
}

static void PrintResult(SocialGraphDemoResult result)
{
    Console.WriteLine($"Created       : {result.CreatedNodes} nodes, {result.CreatedRelations} relation");
    Console.WriteLine($"Updated       : {result.UpdatedNodes} node, {result.UpdatedRelations} relation");
    Console.WriteLine($"Verified path : {result.SourceName} ({result.SourceId}) -[{result.SinceYear}]-> {result.TargetName} ({result.TargetId})");
    Console.WriteLine($"Atomic        : {result.IsAtomic}");
}
