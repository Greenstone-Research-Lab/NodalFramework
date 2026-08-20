using Neo4j.Driver;
using Nodal.Core.Analytics;
using Nodal.Core.Execution;
using Nodal.Core.Migrations;
using Nodal.Core.Mutations;
using Nodal.Core.Providers;

namespace Nodal.Neo4j;

/// <summary>
/// Provides the complete Nodal query pipeline for Neo4j and owns the pooled driver
/// when constructed from <see cref="Neo4jOptions"/>.
/// </summary>
public sealed class Neo4jProvider : IGraphProvider, IGraphMutationProvider, IGraphMigrationProvider,
    IGraphAnalyticsProvider, IGraphAnalyticsRuntimeProvider, IAsyncDisposable
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
        AnalyticsCompiler = new Neo4jAnalyticsCompiler();
        AnalyticsCapabilities = CreateAnalyticsCapabilities(
            options.GraphDataScienceEnabled,
            options.AnalyticsAlgorithms);
        AnalyticsRuntime = new Neo4jAnalyticsRuntime(
            driver, options.Database, AnalyticsCapabilities.Algorithms, options.AnalyticsDiscoveryCacheDuration);
    }

    /// <summary>
    /// Initializes a provider using an externally managed, pooled Neo4j driver.
    /// The caller remains responsible for disposing the driver.
    /// </summary>
    public Neo4jProvider(
        IDriver driver,
        string? database = null,
        bool graphDataScienceEnabled = false,
        IReadOnlySet<GraphAnalyticsAlgorithm>? analyticsAlgorithms = null)
    {
        ArgumentNullException.ThrowIfNull(driver);
        this.driver = driver;
        QueryCompiler = new Neo4jQueryCompiler();
        CommandExecutor = new Neo4jCommandExecutor(driver, database);
        MutationExecutor = new Neo4jMutationExecutor(driver, database);
        MigrationDialect = new Neo4jMigrationDialect();
        MigrationExecutor = new Neo4jMigrationExecutor(driver, database);
        ResultMaterializer = new JsonGraphResultMaterializer();
        AnalyticsCompiler = new Neo4jAnalyticsCompiler();
        AnalyticsCapabilities = CreateAnalyticsCapabilities(graphDataScienceEnabled, analyticsAlgorithms);
        AnalyticsRuntime = new Neo4jAnalyticsRuntime(
            driver, database, AnalyticsCapabilities.Algorithms, TimeSpan.FromMinutes(5));
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
        ((Neo4jAnalyticsRuntime)AnalyticsRuntime).Dispose();
        if (ownsDriver)
        {
            await driver.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static GraphAnalyticsCapabilities CreateAnalyticsCapabilities(
        bool enabled,
        IReadOnlySet<GraphAnalyticsAlgorithm>? configuredAlgorithms)
    {
        var algorithms = new HashSet<GraphAnalyticsAlgorithm>
        {
            GraphAnalyticsAlgorithm.ShortestPath,
            GraphAnalyticsAlgorithm.AllShortestPaths,
        };
        if (enabled)
        {
            algorithms.UnionWith(configuredAlgorithms ?? Enum.GetValues<GraphAnalyticsAlgorithm>()
                .Where(algorithm => algorithm is not GraphAnalyticsAlgorithm.ShortestPath and
                    not GraphAnalyticsAlgorithm.AllShortestPaths)
                .ToHashSet());
        }
        return new GraphAnalyticsCapabilities
        {
            ProviderName = "Neo4j",
            TestedProviderVersion = "5.26 Community",
            ClientVersion = "Neo4j.Driver 6.3.0",
            Algorithms = algorithms,
            SupportsWeightedRelationships = enabled,
            SupportsProjectionManagement = enabled,
            AlgorithmDetails = algorithms.ToDictionary(
                algorithm => algorithm,
                algorithm => new GraphAlgorithmCapability(
                    algorithm,
                    algorithm is GraphAnalyticsAlgorithm.ShortestPath or GraphAnalyticsAlgorithm.AllShortestPaths
                        ? GraphAnalyticsAvailability.Native
                        : GraphAnalyticsAvailability.Extension,
                    GraphCapabilityVerification.Compiler,
                    algorithm is GraphAnalyticsAlgorithm.ShortestPath or GraphAnalyticsAlgorithm.AllShortestPaths
                        ? "Neo4j Cypher shortest-path support."
                        : "A Neo4j GDS version compatible with the Neo4j server and an existing named projection.",
                    SupportsWeights(algorithm))),
        };
    }

    private static bool SupportsWeights(GraphAnalyticsAlgorithm algorithm) => algorithm is
        GraphAnalyticsAlgorithm.ArticleRank or
        GraphAnalyticsAlgorithm.BetweennessCentrality or
        GraphAnalyticsAlgorithm.CelfInfluenceMaximization or
        GraphAnalyticsAlgorithm.ClosenessCentrality or
        GraphAnalyticsAlgorithm.DegreeCentrality or
        GraphAnalyticsAlgorithm.EigenvectorCentrality or
        GraphAnalyticsAlgorithm.PageRank or
        GraphAnalyticsAlgorithm.Conductance or
        GraphAnalyticsAlgorithm.LabelPropagation or
        GraphAnalyticsAlgorithm.Leiden or
        GraphAnalyticsAlgorithm.Louvain or
        GraphAnalyticsAlgorithm.Modularity or
        GraphAnalyticsAlgorithm.ModularityOptimization or
        GraphAnalyticsAlgorithm.Dijkstra or
        GraphAnalyticsAlgorithm.AStar or
        GraphAnalyticsAlgorithm.YenKShortestPaths;
}
