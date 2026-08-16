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
        "NODAL_TIGERGRAPH_ACCESS_TOKEN",
        "NODAL_TIGERGRAPH_GRAPH",
    ];

    public TigerGraphIntegrationFactAttribute()
    {
        if (RequiredVariables.Any(variable =>
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable))))
        {
            Skip = "Set the NODAL_TIGERGRAPH_* variables to run live TigerGraph tests.";
        }
    }
}
