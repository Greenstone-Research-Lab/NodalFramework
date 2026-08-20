using Nodal.Core.Analytics;
using Nodal.Core.Execution;
using Nodal.Core.Migrations;
using Nodal.Core.Providers;

namespace Nodal.Core;

/// <summary>Exposes database-wide provider services without leaking transport-specific objects.</summary>
public sealed class NodalDatabaseFacade
{
    private readonly IGraphProvider provider;

    internal NodalDatabaseFacade(IGraphProvider provider) => this.provider = provider;

    /// <summary>Gets whether the configured provider implements migration execution.</summary>
    public bool SupportsMigrations => provider is IGraphMigrationProvider migrationProvider &&
        migrationProvider.SupportsMigrationExecution;

    /// <summary>Gets whether the provider exposes runtime analytics discovery and lifecycle services.</summary>
    public bool SupportsAnalyticsRuntime => provider is IGraphAnalyticsRuntimeProvider;

    /// <summary>Gets runtime analytics discovery and lifecycle services.</summary>
    public IGraphAnalyticsRuntime GetAnalyticsRuntime() =>
        provider is IGraphAnalyticsRuntimeProvider runtimeProvider
            ? runtimeProvider.AnalyticsRuntime
            : throw new NotSupportedException(
                $"Graph provider '{provider.GetType().Name}' does not expose an analytics runtime.");

    /// <summary>Gets the migration provider or reports that this provider is query-only.</summary>
    public IGraphMigrationProvider GetMigrationProvider()
    {
        if (provider is not IGraphMigrationProvider migrationProvider ||
            !migrationProvider.SupportsMigrationExecution)
        {
            throw new NotSupportedException(
                $"Graph provider '{provider.GetType().Name}' does not have migration execution configured.");
        }

        return migrationProvider;
    }

    /// <summary>Executes a safely parameterized provider-native read command.</summary>
    public ValueTask<GraphQueryResult> ExecuteRawAsync(
        string commandText,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandText);
        return provider.CommandExecutor.ExecuteAsync(
            new GraphCommand(commandText, parameters ?? new Dictionary<string, object?>()),
            cancellationToken);
    }

    /// <summary>Executes provider-native query text and materializes its normalized node records.</summary>
    public async ValueTask<IReadOnlyList<TNode>> QueryRawAsync<TNode>(
        string commandText,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var result = await ExecuteRawAsync(commandText, parameters, cancellationToken).ConfigureAwait(false);
        return provider.ResultMaterializer.Materialize<TNode>(result);
    }

    /// <summary>Executes a parameterized Cypher query through the configured provider transport.</summary>
    public ValueTask<GraphQueryResult> CypherAsync(
        string cypher,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default) =>
        ExecuteRawAsync(cypher, parameters, cancellationToken);

    /// <summary>Executes a parameterized GSQL query through the configured provider transport.</summary>
    public ValueTask<GraphQueryResult> GsqlAsync(
        string gsql,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default) =>
        ExecuteRawAsync(gsql, parameters, cancellationToken);
}
