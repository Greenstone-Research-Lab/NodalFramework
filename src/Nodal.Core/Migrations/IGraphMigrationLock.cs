namespace Nodal.Core.Migrations;

/// <summary>
/// Provides an exclusive lease for one migration scope.
/// </summary>
public interface IGraphMigrationLock
{
    /// <summary>
    /// Acquires an exclusive migration lease for the specified scope.
    /// </summary>
    /// <param name="scope">
    /// A stable provider/database/graph scope identifier.
    /// </param>
    /// <param name="cancellationToken">
    /// Token used to cancel lock acquisition.
    /// </param>
    /// <returns>
    /// An asynchronous disposable lease. The lease must be disposed after
    /// migration planning or execution completes.
    /// </returns>
    ValueTask<IAsyncDisposable> AcquireAsync(
        string scope,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Exposes migration locking as an optional provider capability.
/// </summary>
/// <remarks>
/// Providers that cannot guarantee an exclusive migration lock must not
/// implement this interface.
/// </remarks>
public interface IGraphMigrationLockProvider
{
    /// <summary>
    /// Gets the provider-specific migration lock scope.
    /// </summary>
    /// <remarks>
    /// The scope should identify the provider, database, and graph target.
    /// Different databases or graphs must not share the same scope.
    /// </remarks>
    string MigrationLockScope { get; }

    /// <summary>
    /// Gets the migration lock implementation for the provider.
    /// </summary>
    IGraphMigrationLock MigrationLock { get; }
}
