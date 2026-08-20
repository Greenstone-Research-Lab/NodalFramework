using Nodal.Samples.SocialGraph;
using Nodal.TigerGraph;

var endpoint = new Uri(ReadSetting("NODAL_TIGERGRAPH_ENDPOINT", "http://localhost:14240/"));
var accessToken = Environment.GetEnvironmentVariable("NODAL_TIGERGRAPH_ACCESS_TOKEN");
using var httpClient = new HttpClient { BaseAddress = endpoint };
var provider = new TigerGraphProvider(
    httpClient,
    new TigerGraphOptions
    {
        Endpoint = endpoint,
        AccessToken = accessToken,
        Username = ReadSetting("NODAL_TIGERGRAPH_USERNAME", "tigergraph"),
        Password = ReadSetting("NODAL_TIGERGRAPH_PASSWORD", "tigergraph"),
    },
    ReadSetting("NODAL_TIGERGRAPH_GRAPH", "NodalQa"));

var result = await SocialGraphDemo.RunAsync(provider);

Console.WriteLine("Nodal Framework / TigerGraph demo completed.");
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
