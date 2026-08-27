using System.Diagnostics.CodeAnalysis;
using Nodal.Core.Analytics;
using Nodal.Core.Execution;
using Nodal.Core.Migrations;
using Nodal.Core.Mutations;
using Nodal.Core.Providers;
using Nodal.Core.Query;
using Nodal.TigerGraph.Extensions;

namespace Nodal.TigerGraph;

/// <summary>
/// Provides the complete Nodal query pipeline for a TigerGraph graph.
/// </summary>
public sealed class TigerGraphProvider : IGraphProvider, IGraphQueryCapabilityProvider, IGraphMutationProvider, IGraphMigrationProvider, IGraphMigrationHistoryProvider,
    IGraphMigrationLockProvider, IGraphAnalyticsProvider, IGraphAnalyticsRuntimeProvider, IGraphSchemaIntrospectionProvider
{
    private readonly IGraphMigrationExecutor? migrationExecutor;
    private readonly IGraphMigrationHistoryStore? migrationHistory;
    private readonly IGraphMigrationLock? migrationLock;
    private readonly TigerGraphMigrationRecovery? migrationRecovery;
    private readonly string graphName;


    /// <summary>
    /// Initializes a provider using an externally managed HTTP client.
    /// </summary>
    public TigerGraphProvider(HttpClient httpClient, TigerGraphOptions options, string graphName)
    {
        this.graphName = graphName;
        QueryExtensions = options.QueryExtensions;
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
        SchemaIntrospector = new UnavailableTigerGraphSchemaIntrospector();
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
        if (administrativeTransport is ITigerGraphAdministrativeControlPlane controlPlane)
        {
            var infrastructure = new TigerGraphMigrationInfrastructure(
                httpClient,
                options,
                graphName,
                controlPlane);
            var tigerGraphMigrationExecutor = new TigerGraphMigrationExecutor(
                httpClient,
                options,
                graphName,
                controlPlane,
                infrastructure);
            migrationExecutor = tigerGraphMigrationExecutor;
            migrationHistory = new TigerGraphMigrationHistoryStore(
                httpClient,
                options,
                graphName,
                controlPlane,
                infrastructure);
            migrationLock = new TigerGraphMigrationLock(controlPlane);
            migrationRecovery = new TigerGraphMigrationRecovery(
                tigerGraphMigrationExecutor.Journal);
        }
        SchemaIntrospector = administrativeTransport is ITigerGraphSchemaIntrospectionTransport schemaTransport
            ? new TigerGraphSchemaIntrospector(schemaTransport, graphName)
            : new UnavailableTigerGraphSchemaIntrospector();
    }

    /// <summary>Initializes a provider with separate migration and schema administration channels.</summary>
    public TigerGraphProvider(
        HttpClient httpClient,
        TigerGraphOptions options,
        string graphName,
        ITigerGraphAdministrativeTransport administrativeTransport,
        ITigerGraphSchemaIntrospectionTransport schemaTransport)
        : this(httpClient, options, graphName, administrativeTransport)
    {
        SchemaIntrospector = new TigerGraphSchemaIntrospector(schemaTransport, graphName);
    }

    /// <inheritdoc />
    public IGraphQueryCompiler QueryCompiler { get; }

    /// <summary>Gets the explicitly configured installed-query extension manifest, when present.</summary>
    public TigerGraphQueryExtensionManifest? QueryExtensions { get; }

    /// <inheritdoc />
    public IGraphCommandExecutor CommandExecutor { get; }

    /// <inheritdoc />
    public IGraphResultMaterializer ResultMaterializer { get; }

    /// <inheritdoc />
    public GraphQueryCapabilities QueryCapabilities { get; } = new()
    {
        ProviderName = "TigerGraph",
        TestedProviderVersion = "4.2.4 Community",
        Features = GraphQueryCapability.VariableLengthTraversal |
            GraphQueryCapability.Distinct |
            GraphQueryCapability.SimplePath |
            GraphQueryCapability.ServerSideProjection |
            GraphQueryCapability.Aggregation,
    };

    /// <inheritdoc />
    public IGraphAnalyticsCompiler AnalyticsCompiler { get; }

    /// <inheritdoc />
    public GraphAnalyticsCapabilities AnalyticsCapabilities { get; }

    /// <inheritdoc />
    public IGraphAnalyticsRuntime AnalyticsRuntime { get; }

    /// <inheritdoc />
    public IGraphSchemaIntrospector SchemaIntrospector { get; }

    /// <inheritdoc />
    public IGraphMutationExecutor MutationExecutor { get; private set; }

    /// <inheritdoc />
    public bool SupportsMigrationExecution => migrationExecutor is not null;

    /// <inheritdoc />
    public IGraphMigrationDialect MigrationDialect { get; }

    /// <inheritdoc />
    public IGraphMigrationExecutor MigrationExecutor => migrationExecutor ?? throw new NotSupportedException(
        "TigerGraph migration execution requires an ITigerGraphAdministrativeControlPlane.");


    /// <inheritdoc />
    public IGraphMigrationHistoryStore MigrationHistory =>
        migrationHistory
        ?? throw new NotSupportedException(
            "TigerGraph stateful migration history requires " +
            "an ITigerGraphAdministrativeControlPlane.");

    /// <inheritdoc />
    public string MigrationHistoryScope =>
        $"tigergraph:{graphName}";

    /// <inheritdoc />
    public IGraphMigrationLock MigrationLock => migrationLock ?? throw new NotSupportedException(
        "TigerGraph migration locking requires an ITigerGraphAdministrativeControlPlane.");

    /// <inheritdoc />
    public string MigrationLockScope => $"tigergraph:{graphName}";

    /// <summary>
    /// Gets the explicit TigerGraph recovery surface used to reconcile schema jobs with unknown outcomes.
    /// </summary>
    public TigerGraphMigrationRecovery MigrationRecovery => migrationRecovery ?? throw new NotSupportedException(
        "TigerGraph migration recovery requires an ITigerGraphAdministrativeControlPlane.");

    /// <inheritdoc />
    public GraphProviderCapabilities Capabilities { get; } = new()
    {
        SupportsTransactions = true,
        SupportsAtomicBatch = true,
        TransactionScope = GraphTransactionScope.RequestOrQuery,
    };
}

[ExcludeFromCodeCoverage]
internal sealed class UnavailableTigerGraphSchemaIntrospector : IGraphSchemaIntrospector
{
    public ValueTask<NodalSchemaSnapshot> CaptureAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromException<NodalSchemaSnapshot>(new NotSupportedException(
            "TigerGraph schema introspection requires an ITigerGraphSchemaIntrospectionTransport."));
}
