namespace Nodal.Neo4j;

/// <summary>
/// Defines the connection settings used to create a pooled Neo4j driver.
/// </summary>
public sealed class Neo4jOptions
{
    /// <summary>Gets or initializes the Bolt endpoint, for example <c>neo4j://localhost:7687</c>.</summary>
    public required Uri Endpoint { get; init; }

    /// <summary>Gets or initializes the Neo4j user name.</summary>
    public required string Username { get; init; }

    /// <summary>Gets or initializes the Neo4j password.</summary>
    public required string Password { get; init; }

    /// <summary>Gets or initializes the optional target database name.</summary>
    public string? Database { get; init; }
}
