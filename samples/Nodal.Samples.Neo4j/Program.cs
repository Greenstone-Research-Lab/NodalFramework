using Nodal.Neo4j;
using Nodal.Samples.SocialGraph;

var options = new Neo4jOptions
{
    Endpoint = new Uri(ReadSetting("NODAL_NEO4J_ENDPOINT", "neo4j://localhost:7687")),
    Username = ReadSetting("NODAL_NEO4J_USERNAME", "neo4j"),
    Password = ReadSetting("NODAL_NEO4J_PASSWORD", "NodalLocal123!"),
    Database = ReadSetting("NODAL_NEO4J_DATABASE", "neo4j"),
};

await using var provider = new Neo4jProvider(options);
var result = await SocialGraphDemo.RunAsync(provider);

Console.WriteLine("Nodal Framework / Neo4j demo completed.");
PrintResult(result);

static string ReadSetting(string name, string fallback) =>
    Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : fallback;

static void PrintResult(SocialGraphDemoResult result)
{
    Console.WriteLine($"Created       : {result.CreatedNodes} nodes, {result.CreatedRelations} relation");
    Console.WriteLine($"Updated       : {result.UpdatedNodes} node, {result.UpdatedRelations} relation");
    Console.WriteLine($"Verified path : {result.SourceName} ({result.SourceId}) -[{result.SinceYear}]-> {result.TargetName} ({result.TargetId})");
    Console.WriteLine($"Atomic        : {result.IsAtomic}");
}
