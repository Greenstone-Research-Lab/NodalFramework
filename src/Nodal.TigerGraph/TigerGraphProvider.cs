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
    IGraphMigrationLockProvider, IGraphAnalyticsProvider, IGraphAnalyticsRuntimeProvider, IGraphSchemaIntrospectionProvider,
    IGraphAnalyticsScopeCapabilityProvider
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
        QueryCapabilities = CreateQueryCapabilities(GraphQueryCapability.None);
        MutationExecutor = new TigerGraphMutationExecutor(httpClient, options, graphName);
        ResultMaterializer = new JsonGraphResultMaterializer();
        MigrationDialect = new TigerGraphMigrationDialect(graphName);
        AnalyticsCompiler = CreateAnalyticsCompiler(options, null);
        var configuredAlgorithms = options.AnalyticsQueries.Keys
            .Concat(options.AnalyticsBindingManifest?.Bindings.Values.Select(binding => binding.Algorithm) ?? [])
            .ToHashSet();
        if (options.AnalyticsProvisioningMode != TigerGraphAnalyticsProvisioningMode.ValidateOnly)
        {
            configuredAlgorithms.Add(GraphAnalyticsAlgorithm.PageRank);
        }
        AnalyticsCapabilities = new GraphAnalyticsCapabilities
        {
            ProviderName = "TigerGraph",
            TestedProviderVersion = "4.2.4 Community",
            ClientVersion = "REST++ / GSQL 4.2.4 baseline",
            Algorithms = configuredAlgorithms,
            SupportsWeightedRelationships = options.WeightedAnalyticsAlgorithms.Count > 0,
            SupportsProjectionManagement = false,
            AlgorithmDetails = configuredAlgorithms.ToDictionary(
                algorithm => algorithm,
                algorithm => new GraphAlgorithmCapability(
                    algorithm,
                    GraphAnalyticsAvailability.InstalledQuery,
                    GraphCapabilityVerification.Compiler,
                    options.AnalyticsQueries.TryGetValue(algorithm, out var queryName)
                        ? $"Installed TigerGraph query '{queryName}'."
                        : "Verified scope binding or explicitly enabled Nodal-managed installed query.",
                    SupportsWeights: options.WeightedAnalyticsAlgorithms.Contains(algorithm))),
        };
        AnalyticsRuntime = new TigerGraphAnalyticsRuntime(options.AnalyticsQueries, options.AnalyticsBindingManifest);
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
        var requiresGeneratedCatalog = options.GeneratedQueryExtensions.Contains(TigerGraphQueryExtensionFeature.CorrelatedExistence) ||
            options.AnalyticsProvisioningMode != TigerGraphAnalyticsProvisioningMode.ValidateOnly;
        if (requiresGeneratedCatalog)
        {
            var installedQueries = new TigerGraphInstalledQueryCatalog(graphName);
            if (options.GeneratedQueryExtensions.Contains(TigerGraphQueryExtensionFeature.CorrelatedExistence))
            {
                QueryCompiler = new TigerGraphQueryCompiler(graphName, installedQueries);
                QueryCapabilities = CreateQueryCapabilities(GraphQueryCapability.CorrelatedSubquery);
            }
            AnalyticsCompiler = CreateAnalyticsCompiler(options, installedQueries);
            CommandExecutor = new TigerGraphCommandExecutor(
                httpClient,
                options,
                installedQueries,
                new TigerGraphInstalledQueryInstaller(administrativeTransport, graphName));
        }
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
    public IGraphQueryCompiler QueryCompiler { get; private set; }

    /// <summary>Gets the explicitly configured installed-query extension manifest, when present.</summary>
    public TigerGraphQueryExtensionManifest? QueryExtensions { get; }

    /// <summary>
    /// Gets the installed-query extension contract verified during asynchronous provider creation.
    /// A direct constructor does not perform network I/O and therefore leaves this value unset.
    /// </summary>
    public TigerGraphQueryExtensionSnapshot? VerifiedQueryExtensions { get; private set; }

    /// <inheritdoc />
    public IGraphCommandExecutor CommandExecutor { get; private set; }

    /// <inheritdoc />
    public IGraphResultMaterializer ResultMaterializer { get; }

    /// <inheritdoc />
    public GraphQueryCapabilities QueryCapabilities { get; private set; }

    private static GraphQueryCapabilities CreateQueryCapabilities(GraphQueryCapability extensions) => new()
    {
        ProviderName = "TigerGraph",
        TestedProviderVersion = "4.2.4 Community",
        Features = GraphQueryCapability.VariableLengthTraversal |
            GraphQueryCapability.Distinct |
            GraphQueryCapability.SimplePath |
            GraphQueryCapability.ServerSideProjection |
            GraphQueryCapability.Aggregation |
            extensions,
    };

    /// <inheritdoc />
    public IGraphAnalyticsCompiler AnalyticsCompiler { get; private set; }

    /// <inheritdoc />
    public GraphAnalyticsCapabilities AnalyticsCapabilities { get; }

    /// <inheritdoc />
    public void ValidateAnalyticsScope(GraphAnalyticsQueryModel query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (AnalyticsCompiler is TigerGraphAnalyticsCompiler compiler)
        {
            compiler.Validate(query);
        }
    }

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

    internal void SetVerifiedQueryExtensions(TigerGraphQueryExtensionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        VerifiedQueryExtensions = snapshot;
    }

    private TigerGraphAnalyticsCompiler CreateAnalyticsCompiler(
        TigerGraphOptions options,
        TigerGraphInstalledQueryCatalog? generatedQueries) => new(
            graphName,
            options.AnalyticsQueries,
            options.AnalyticsBindingManifest,
            options.AnalyticsProvisioningMode,
            options.AnalyticsContractVersion,
            generatedQueries);

}

[ExcludeFromCodeCoverage]
internal sealed class UnavailableTigerGraphSchemaIntrospector : IGraphSchemaIntrospector
{
    public ValueTask<NodalSchemaSnapshot> CaptureAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromException<NodalSchemaSnapshot>(new NotSupportedException(
            "TigerGraph schema introspection requires an ITigerGraphSchemaIntrospectionTransport."));
}
