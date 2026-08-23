using Nodal.Core.Analytics;
using Nodal.Core.Execution;
using Nodal.Core.Migrations;
using Nodal.Core.Mutations;
using Nodal.Core.Providers;

namespace Nodal.TigerGraph;

/// <summary>
/// Provides the complete Nodal query pipeline for a TigerGraph graph.
/// </summary>
public sealed class TigerGraphProvider : IGraphProvider, IGraphMutationProvider, IGraphMigrationProvider, IGraphMigrationHistoryProvider,
    IGraphAnalyticsProvider, IGraphAnalyticsRuntimeProvider
{
    private readonly IGraphMigrationExecutor? migrationExecutor;
    private readonly IGraphMigrationHistoryStore? migrationHistory;
    private readonly string graphName;


    /// <summary>
    /// Initializes a provider using an externally managed HTTP client.
    /// </summary>
    public TigerGraphProvider(HttpClient httpClient, TigerGraphOptions options, string graphName)
    {
        this.graphName = graphName;
        QueryCompiler = new TigerGraphQueryCompiler(graphName);
        CommandExecutor = new TigerGraphCommandExecutor(httpClient, options);
        MutationExecutor = new TigerGraphMutationExecutor(httpClient, options, graphName);
        ResultMaterializer = new JsonGraphResultMaterializer();
        MigrationDialect = new TigerGraphMigrationDialect(graphName);
        AnalyticsCompiler = new TigerGraphAnalyticsCompiler(graphName, options.AnalyticsQueries);
        AnalyticsCapabilities = new GraphAnalyticsCapabilities
        {
            ProviderName = "TigerGraph",
            TestedProviderVersion = "4.2.4 Community",
            ClientVersion = "REST++ / GSQL 4.2.4 baseline",
            Algorithms = options.AnalyticsQueries.Keys.ToHashSet(),
            SupportsWeightedRelationships = options.WeightedAnalyticsAlgorithms.Count > 0,
            SupportsProjectionManagement = false,
            AlgorithmDetails = options.AnalyticsQueries.ToDictionary(
                item => item.Key,
                item => new GraphAlgorithmCapability(
                    item.Key,
                    GraphAnalyticsAvailability.InstalledQuery,
                    GraphCapabilityVerification.Compiler,
                    $"Installed GSQL query '{item.Value}' returning the Nodal analytics response contract.",
                    SupportsWeights: options.WeightedAnalyticsAlgorithms.Contains(item.Key))),
        };
        AnalyticsRuntime = new TigerGraphAnalyticsRuntime(options.AnalyticsQueries);
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
        migrationHistory = new TigerGraphMigrationHistoryStore(
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
    public IGraphAnalyticsCompiler AnalyticsCompiler { get; }

    /// <inheritdoc />
    public GraphAnalyticsCapabilities AnalyticsCapabilities { get; }

    /// <inheritdoc />
    public IGraphAnalyticsRuntime AnalyticsRuntime { get; }

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
    public IGraphMigrationHistoryStore MigrationHistory =>
        migrationHistory
        ?? throw new NotSupportedException(
            "TigerGraph stateful migration history requires " +
            "an ITigerGraphAdministrativeTransport.");

    /// <inheritdoc />
    public string MigrationHistoryScope =>
        $"tigergraph:{graphName}";

    /// <inheritdoc />
    public GraphProviderCapabilities Capabilities { get; } = new()
    {
        SupportsTransactions = true,
        SupportsAtomicBatch = true,
        TransactionScope = GraphTransactionScope.RequestOrQuery,
    };
}
