using Nodal.Core.Execution;
using Nodal.Core.Migrations;
using Nodal.Core.Mutations;
using Nodal.Core.Providers;

namespace Nodal.TigerGraph;

/// <summary>
/// Provides the complete Nodal query pipeline for a TigerGraph graph.
/// </summary>
public sealed class TigerGraphProvider : IGraphProvider, IGraphMutationProvider, IGraphMigrationProvider
{
    private readonly IGraphMigrationExecutor? migrationExecutor;

    /// <summary>
    /// Initializes a provider using an externally managed HTTP client.
    /// </summary>
    public TigerGraphProvider(HttpClient httpClient, TigerGraphOptions options, string graphName)
    {
        QueryCompiler = new TigerGraphQueryCompiler(graphName);
        CommandExecutor = new TigerGraphCommandExecutor(httpClient, options);
        MutationExecutor = new TigerGraphMutationExecutor(httpClient, options, graphName);
        ResultMaterializer = new JsonGraphResultMaterializer();
        MigrationDialect = new TigerGraphMigrationDialect(graphName);
    }

    /// <summary>
    /// Initializes a provider with an explicit supported channel for privileged GSQL administration.
    /// </summary>
    public TigerGraphProvider(
        HttpClient httpClient,
        TigerGraphOptions options,
        string graphName,
        ITigerGraphAdministrativeTransport administrativeTransport)
        : this(httpClient, options, graphName)
    {
        ArgumentNullException.ThrowIfNull(administrativeTransport);
        MutationExecutor = new TigerGraphMutationExecutor(
            httpClient,
            options,
            graphName,
            administrativeTransport);
        migrationExecutor = new TigerGraphMigrationExecutor(
            httpClient,
            options,
            graphName,
            administrativeTransport);
    }

    /// <inheritdoc />
    public IGraphQueryCompiler QueryCompiler { get; }

    /// <inheritdoc />
    public IGraphCommandExecutor CommandExecutor { get; }

    /// <inheritdoc />
    public IGraphResultMaterializer ResultMaterializer { get; }

    /// <inheritdoc />
    public IGraphMutationExecutor MutationExecutor { get; private set; }

    /// <inheritdoc />
    public bool SupportsMigrationExecution => migrationExecutor is not null;

    /// <inheritdoc />
    public IGraphMigrationDialect MigrationDialect { get; }

    /// <inheritdoc />
    public IGraphMigrationExecutor MigrationExecutor => migrationExecutor ?? throw new NotSupportedException(
        "TigerGraph migration execution requires an ITigerGraphAdministrativeTransport.");

    /// <inheritdoc />
    public GraphProviderCapabilities Capabilities { get; } = new()
    {
        SupportsTransactions = true,
        SupportsAtomicBatch = true,
        TransactionScope = GraphTransactionScope.RequestOrQuery,
    };
}
