namespace Nodal.Core.Migrations;

/// <summary>
/// Indicates that a provider cannot execute a requested graph capability.
/// </summary>
public sealed class NodalCapabilityNotSupportedException : NotSupportedException
{
    /// <summary>Gets the provider dialect that rejected the capability.</summary>
    public string ProviderName { get; }

    /// <summary>Gets the stable capability code.</summary>
    public string CapabilityCode { get; }

    /// <summary>
    /// Initializes a capability-not-supported exception.
    /// </summary>
    /// <param name="message">
    /// A safe explanation of the unsupported capability.
    /// </param>
    public NodalCapabilityNotSupportedException(
        string message)
        : base(message)
    {
        ProviderName = "Unknown";
        CapabilityCode = "NODAL-CAPABILITY-UNSPECIFIED";
    }

    /// <summary>
    /// Initializes a capability-not-supported exception with an inner cause.
    /// </summary>
    /// <param name="message">
    /// A safe explanation of the unsupported capability.
    /// </param>
    /// <param name="innerException">
    /// The provider exception that caused the failure.
    /// </param>
    public NodalCapabilityNotSupportedException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
        ProviderName = "Unknown";
        CapabilityCode = "NODAL-CAPABILITY-UNSPECIFIED";
    }

    /// <summary>
    /// Initializes a provider-specific capability exception.
    /// </summary>
    public NodalCapabilityNotSupportedException(
        string providerName,
        string capabilityCode,
        string message)
        : base(
            $"Provider '{providerName}' does not support " +
            $"capability '{capabilityCode}': {message}")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(capabilityCode);
        ProviderName = providerName;
        CapabilityCode = capabilityCode;
    }
}
