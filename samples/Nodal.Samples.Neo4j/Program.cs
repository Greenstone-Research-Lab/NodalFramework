using Nodal.Core.Analytics;
using Nodal.Neo4j;
using Nodal.Samples.SocialGraph;

var gdsEnabled = bool.TryParse(Environment.GetEnvironmentVariable("NODAL_NEO4J_GDS_ENABLED"), out var enabled) && enabled;
var options = new Neo4jOptions
{
    Endpoint = new Uri(ReadSetting("NODAL_NEO4J_ENDPOINT", "neo4j://localhost:7687")),
    Username = ReadSetting("NODAL_NEO4J_USERNAME", "neo4j"),
    Password = ReadSetting("NODAL_NEO4J_PASSWORD", "NodalLocal123!"),
    Database = ReadSetting("NODAL_NEO4J_DATABASE", "neo4j"),
    GraphDataScienceEnabled = gdsEnabled,
};

await using var provider = new Neo4jProvider(options);
var result = await SocialGraphDemo.RunAsync(provider);

Console.WriteLine("Nodal Framework / Neo4j demo completed.");
PrintResult(result);

var analyticsContext = new SocialGraphContext(provider);
var shortest = await analyticsContext.People.Match(person => person.Id == result.SourceId)
    .ShortestPathTo(
        analyticsContext.People.Match(person => person.Id == result.TargetId),
        analyticsContext.Friendships)
    .SingleAsync();
Console.WriteLine($"Shortest path : {shortest.HopCount} hop(s)");

if (gdsEnabled)
{
    const string projectionName = "nodal_social_demo";
    await analyticsContext.Database.GetAnalyticsRuntime().EnsureProjectionAsync(
        new GraphProjectionDefinition(projectionName, "Person", "KNOWS"));
    var pageRank = await analyticsContext.People.Query().Analyze(analyticsContext.Friendships)
        .PageRank(new PageRankOptions()).OnProjection(projectionName).Top(5).ToListAsync();
    var communities = await analyticsContext.People.Query().Analyze(analyticsContext.Friendships)
        .Louvain(new LouvainOptions()).OnProjection(projectionName).ToListAsync();
    Console.WriteLine($"PageRank rows  : {pageRank.Count}");
    Console.WriteLine($"Louvain rows   : {communities.Count}");
    await analyticsContext.Database.GetAnalyticsRuntime().DropProjectionAsync(projectionName);
}

static string ReadSetting(string name, string fallback) =>
    Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : fallback;

static void PrintResult(SocialGraphDemoResult result)
{
    Console.WriteLine($"Created       : {result.CreatedNodes} nodes, {result.CreatedRelations} relation");
    Console.WriteLine($"Updated       : {result.UpdatedNodes} node, {result.UpdatedRelations} relation");
    Console.WriteLine($"Verified path : {result.SourceName} ({result.SourceId}) -[{result.SinceYear}]-> {result.TargetName} ({result.TargetId})");
    Console.WriteLine($"Atomic        : {result.IsAtomic}");
}
