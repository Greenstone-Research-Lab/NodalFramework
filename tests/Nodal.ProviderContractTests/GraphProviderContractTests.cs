using Nodal.Core.Analytics;
using Nodal.Core.Execution;
using Nodal.Core.Migrations;
using Nodal.Core.Mutations;
using Nodal.Core.Query;

namespace Nodal.ProviderContractTests;

public abstract class GraphProviderContractTests
{
    protected abstract string ExpectedProviderName { get; }

    protected abstract GraphTransactionScope ExpectedTransactionScope { get; }

    protected abstract ProviderContractFixture CreateProvider();

    [Fact]
    public async Task CoreServicesAreStableAndNonNull()
    {
        await using var fixture = CreateProvider();
        var provider = fixture.Provider;

        Assert.Same(provider.QueryCompiler, provider.QueryCompiler);
        Assert.Same(provider.CommandExecutor, provider.CommandExecutor);
        Assert.Same(provider.ResultMaterializer, provider.ResultMaterializer);
    }

    [Fact]
    public async Task ProviderImplementsEveryPortableOptionalContract()
    {
        await using var fixture = CreateProvider();
        var provider = fixture.Provider;

        Assert.IsType<IGraphQueryCapabilityProvider>(provider, exactMatch: false);
        Assert.IsType<IGraphMutationProvider>(provider, exactMatch: false);
        Assert.IsType<IGraphMigrationProvider>(provider, exactMatch: false);
        Assert.IsType<IGraphMigrationHistoryProvider>(provider, exactMatch: false);
        Assert.IsType<IGraphMigrationLockProvider>(provider, exactMatch: false);
        Assert.IsType<IGraphAnalyticsProvider>(provider, exactMatch: false);
        Assert.IsType<IGraphAnalyticsRuntimeProvider>(provider, exactMatch: false);
        Assert.IsType<IGraphSchemaIntrospectionProvider>(provider, exactMatch: false);
    }

    [Fact]
    public async Task CapabilityMetadataIsNamedVersionedAndInternallyConsistent()
    {
        await using var fixture = CreateProvider();
        var queryProvider = Assert.IsType<IGraphQueryCapabilityProvider>(fixture.Provider, exactMatch: false);
        var mutationProvider = Assert.IsType<IGraphMutationProvider>(fixture.Provider, exactMatch: false);
        var analyticsProvider = Assert.IsType<IGraphAnalyticsProvider>(fixture.Provider, exactMatch: false);

        Assert.Equal(ExpectedProviderName, queryProvider.QueryCapabilities.ProviderName);
        Assert.False(string.IsNullOrWhiteSpace(queryProvider.QueryCapabilities.TestedProviderVersion));
        Assert.Equal(ExpectedProviderName, analyticsProvider.AnalyticsCapabilities.ProviderName);
        Assert.False(string.IsNullOrWhiteSpace(analyticsProvider.AnalyticsCapabilities.TestedProviderVersion));
        Assert.Equal(ExpectedTransactionScope, mutationProvider.Capabilities.TransactionScope);
        Assert.Equal(
            mutationProvider.Capabilities.SupportsTransactions,
            mutationProvider.Capabilities.TransactionScope != GraphTransactionScope.None);
        Assert.False(
            mutationProvider.Capabilities.SupportsAtomicBatch &&
            !mutationProvider.Capabilities.SupportsTransactions);
    }

    [Fact]
    public async Task PortableQueryCompilationIsDeterministicAndParameterized()
    {
        const string identity = "contract-person-sensitive-value";
        await using var fixture = CreateProvider();
        var capabilityProvider = Assert.IsType<IGraphQueryCapabilityProvider>(fixture.Provider, exactMatch: false);
        var model = new GraphSet<ContractPerson>()
            .Match(person => person.Id == identity)
            .Distinct()
            .Take(3)
            .ToQueryModel();

        GraphQueryPreflight.Validate(model, capabilityProvider.QueryCapabilities);
        var first = fixture.Provider.QueryCompiler.Compile(model);
        var second = fixture.Provider.QueryCompiler.Compile(model);

        Assert.Equal(first.Text, second.Text);
        Assert.Equal(first.Parameters, second.Parameters);
        Assert.DoesNotContain(identity, first.Text, StringComparison.Ordinal);
        Assert.Contains(first.Parameters, parameter => Equals(parameter.Value, identity));
    }

    [Fact]
    public async Task MigrationExecutionAvailabilityMatchesRuntimeAccess()
    {
        await using var fixture = CreateProvider();
        var migrationProvider = Assert.IsType<IGraphMigrationProvider>(fixture.Provider, exactMatch: false);

        if (migrationProvider.SupportsMigrationExecution)
        {
            Assert.NotNull(migrationProvider.MigrationExecutor);
            return;
        }

        Assert.Throws<NotSupportedException>(() => migrationProvider.MigrationExecutor);
    }
}
