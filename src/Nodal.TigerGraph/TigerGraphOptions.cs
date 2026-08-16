namespace Nodal.TigerGraph;

/// <summary>
/// Configures TigerGraph HTTP and GSQL access for a Nodal provider.
/// </summary>
public sealed class TigerGraphOptions
{
    /// <summary>Gets or sets the TigerGraph server base address.</summary>
    public required Uri Endpoint { get; init; }

    /// <summary>Gets or sets the optional GSQL user name used for Basic authentication.</summary>
    public string? Username { get; init; }

    /// <summary>Gets or sets the optional GSQL password used for Basic authentication.</summary>
    public string? Password { get; init; }

    /// <summary>Gets or sets the preferred REST++ bearer access token.</summary>
    public string? AccessToken { get; init; }
}
