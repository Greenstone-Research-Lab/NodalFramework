using Neo4j.Driver;
using Nodal.Core.Execution;
using Nodal.Core.Migrations;
using Nodal.Core.Mutations;
using Nodal.Core.Providers;

namespace Nodal.Neo4j;

/// <summary>
/// Provides the complete Nodal query pipeline for Neo4j and owns the pooled driver
/// when constructed from <see cref="Neo4jOptions"/>.
/// </summary>
public sealed class Neo4jProvider : IGraphProvider, IGraphMutationProvider, IGraphMigrationProvider, IAsyncDisposable
{
    private readonly IDriver driver;
    private readonly bool ownsDriver;

    /// <summary>
    /// Initializes a provider and creates a pooled Neo4j driver from the supplied settings.
    /// Dispose the provider once, when the application shuts down.
    /// </summary>
    public Neo4jProvider(Neo4jOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        driver = GraphDatabase.Driver(
            options.Endpoint,
            AuthTokens.Basic(options.Username, options.Password));
        ownsDriver = true;
        QueryCompiler = new Neo4jQueryCompiler();
        CommandExecutor = new Neo4jCommandExecutor(driver, options.Database);
        MutationExecutor = new Neo4jMutationExecutor(driver, options.Database);
        MigrationDialect = new Neo4jMigrationDialect();
        MigrationExecutor = new Neo4jMigrationExecutor(driver, options.Database);
        ResultMaterializer = new JsonGraphResultMaterializer();
    }

    /// <summary>
    /// Initializes a provider using an externally managed, pooled Neo4j driver.
    /// The caller remains responsible for disposing the driver.
    /// </summary>
    public Neo4jProvider(IDriver driver, string? database = null)
    {
        ArgumentNullException.ThrowIfNull(driver);
        this.driver = driver;
        QueryCompiler = new Neo4jQueryCompiler();
        CommandExecutor = new Neo4jCommandExecutor(driver, database);
        MutationExecutor = new Neo4jMutationExecutor(driver, database);
        MigrationDialect = new Neo4jMigrationDialect();
        MigrationExecutor = new Neo4jMigrationExecutor(driver, database);
        ResultMaterializer = new JsonGraphResultMaterializer();
    }

    /// <inheritdoc />
    public IGraphQueryCompiler QueryCompiler { get; }

    /// <inheritdoc />
    public IGraphCommandExecutor CommandExecutor { get; }

    /// <inheritdoc />
    public IGraphResultMaterializer ResultMaterializer { get; }

    /// <inheritdoc />
    public IGraphMutationExecutor MutationExecutor { get; }

    /// <inheritdoc />
    public IGraphMigrationDialect MigrationDialect { get; }

    /// <inheritdoc />
    public IGraphMigrationExecutor MigrationExecutor { get; }

    /// <inheritdoc />
    public bool SupportsMigrationExecution => true;

    /// <inheritdoc />
    public GraphProviderCapabilities Capabilities { get; } = new()
    {
        SupportsTransactions = true,
        SupportsAtomicBatch = true,
        TransactionScope = GraphTransactionScope.ClientManaged,
    };

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (ownsDriver)
        {
            await driver.DisposeAsync().ConfigureAwait(false);
        }
    }
}
