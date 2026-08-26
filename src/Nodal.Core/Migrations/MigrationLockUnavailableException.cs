namespace Nodal.Core.Migrations;

/// <summary>
/// Indicates that an exclusive migration lock could not be acquired.
/// </summary>
public sealed class MigrationLockUnavailableException : Exception
{
    /// <summary>
    /// Initializes a migration lock failure.
    /// </summary>
    /// <param name="scope">The provider/database/graph lock scope.</param>
    /// <param name="message">A safe diagnostic message.</param>
    /// <param name="innerException">
    /// The provider exception that prevented lock acquisition, when available.
    /// </param>
    public MigrationLockUnavailableException(
        string scope,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        Scope = scope;
    }

    /// <summary>
    /// Gets the scope for which the lock could not be acquired.
    /// </summary>
    public string Scope { get; }
}
