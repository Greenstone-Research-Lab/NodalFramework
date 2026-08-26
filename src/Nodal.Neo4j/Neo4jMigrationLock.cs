using Neo4j.Driver;
using Nodal.Core.Migrations;

namespace Nodal.Neo4j;

/// <summary>
/// Provides an exclusive migration lease backed by an open Neo4j transaction.
/// </summary>
public sealed class Neo4jMigrationLock : IGraphMigrationLock
{
    private const string LockLabel = "__NodalMigrationLock";
    private const string ConstraintName = "nodal_migration_lock_scope";

    private readonly IDriver driver;
    private readonly string? database;

    /// <summary>
    /// Initializes a Neo4j migration lock.
    /// </summary>
    /// <param name="driver">The shared Neo4j driver.</param>
    /// <param name="database">The optional Neo4j database name.</param>
    public Neo4jMigrationLock(
        IDriver driver,
        string? database = null)
    {
        ArgumentNullException.ThrowIfNull(driver);

        this.driver = driver;
        this.database = database;
    }

    /// <inheritdoc />
    public async ValueTask<IAsyncDisposable> AcquireAsync(
        string scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await using var bootstrapSession = driver.AsyncSession(
                builder => ConfigureSession(builder, database));

            await EnsureConstraintAsync(
                bootstrapSession,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new MigrationLockUnavailableException(
                scope,
                $"Neo4j migration lock constraint could not be prepared " +
                $"for scope '{scope}'.",
                exception);
        }

        var session = driver.AsyncSession(
            builder => ConfigureSession(builder, database));

        IAsyncTransaction? transaction = null;

        try
        {
            transaction = await session
                .BeginTransactionAsync()
                .ConfigureAwait(false);

            var token = Guid.NewGuid().ToString("N");

            var cursor = await transaction.RunAsync(
            $"CREATE (lock:{LockLabel} {{ " +
            "Scope: $scope, " +
            "Token: $token, " +
            "AcquiredAt: datetime() })",
                new Dictionary<string, object>
                {
                    ["scope"] = scope,
                    ["token"] = token
                }).ConfigureAwait(false);

            await cursor.ConsumeAsync().ConfigureAwait(false);

            return new Neo4jMigrationLockLease(
                session,
                transaction,
                scope,
                token);
        }
        catch (OperationCanceledException)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync().ConfigureAwait(false);
                await transaction.DisposeAsync().ConfigureAwait(false);
            }

            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync().ConfigureAwait(false);
                await transaction.DisposeAsync().ConfigureAwait(false);
            }

            await session.DisposeAsync().ConfigureAwait(false);

            throw new MigrationLockUnavailableException(
                scope,
                $"Neo4j migration lock could not be acquired for scope '{scope}'.",
                exception);
        }
    }

    private static async ValueTask EnsureConstraintAsync(
        IAsyncSession session,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cursor = await session.RunAsync(
            $"CREATE CONSTRAINT {ConstraintName} IF NOT EXISTS " +
            $"FOR (lock:{LockLabel}) " +
            "REQUIRE lock.Scope IS UNIQUE").ConfigureAwait(false);

        await cursor.ConsumeAsync().ConfigureAwait(false);
    }

    private static void ConfigureSession(
        SessionConfigBuilder builder,
        string? database)
    {
        if (!string.IsNullOrWhiteSpace(database))
        {
            builder.WithDatabase(database);
        }
    }

    private sealed class Neo4jMigrationLockLease :
        IAsyncDisposable
    {
        private readonly IAsyncSession session;
        private readonly IAsyncTransaction transaction;
        private readonly string scope;
        private readonly string token;
        private int disposed;

        public Neo4jMigrationLockLease(
            IAsyncSession session,
            IAsyncTransaction transaction,
            string scope,
            string token)
        {
            this.session = session;
            this.transaction = transaction;
            this.scope = scope;
            this.token = token;
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            try
            {
                var cursor = await transaction.RunAsync(
                    $"MATCH (lock:{LockLabel}) " +
                    "WHERE lock.Scope = $scope " +
                    "AND lock.Token = $token " +
                    "DELETE lock",
                    new Dictionary<string, object>
                    {
                        ["scope"] = scope,
                        ["token"] = token
                    }).ConfigureAwait(false);

                await cursor.ConsumeAsync().ConfigureAwait(false);
                await transaction.CommitAsync().ConfigureAwait(false);
            }
            catch
            {
                await transaction.RollbackAsync().ConfigureAwait(false);
                throw;
            }
            finally
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
