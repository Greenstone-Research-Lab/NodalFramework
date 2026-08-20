namespace Nodal.IntegrationTests;

internal sealed class Neo4jIntegrationFactAttribute : FactAttribute
{
    private static readonly string[] RequiredVariables =
    [
        "NODAL_NEO4J_ENDPOINT",
        "NODAL_NEO4J_USERNAME",
        "NODAL_NEO4J_PASSWORD",
    ];

    public Neo4jIntegrationFactAttribute()
    {
        if (RequiredVariables.Any(variable =>
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable))))
        {
            Skip = "Set the NODAL_NEO4J_* variables to run live Neo4j tests.";
        }
    }
}

internal sealed class TigerGraphIntegrationFactAttribute : FactAttribute
{
    private static readonly string[] RequiredVariables =
    [
        "NODAL_TIGERGRAPH_ENDPOINT",
        "NODAL_TIGERGRAPH_GRAPH",
    ];

    public TigerGraphIntegrationFactAttribute()
    {
        var missingConnectionSetting = RequiredVariables.Any(variable =>
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable)));
        var hasAccessToken = !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("NODAL_TIGERGRAPH_ACCESS_TOKEN"));
        var hasUserCredentials = !string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("NODAL_TIGERGRAPH_USERNAME")) &&
            Environment.GetEnvironmentVariable("NODAL_TIGERGRAPH_PASSWORD") is not null;

        if (missingConnectionSetting || (!hasAccessToken && !hasUserCredentials))
        {
            Skip = "Set the TigerGraph endpoint and graph plus either token or user credentials.";
        }
    }
}
