namespace Nodal.Core.Migrations;

/// <summary>
/// Indicates that a provider cannot execute a requested graph capability.
/// </summary>
public sealed class NodalCapabilityNotSupportedException : NotSupportedException
{
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
    }
}
