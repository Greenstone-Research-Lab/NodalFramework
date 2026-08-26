using Nodal.Migrations;
using Nodal.TigerGraph;

namespace Nodal.Samples.MigrationHost;

/// <summary>
/// Demonstrates a trusted TigerGraph deployment composition root for <c>Nodal.Tool</c>.
/// </summary>
public sealed class TigerGraphMigrationHost : INodalMigrationBundleExecutionHost, IAsyncDisposable
{
    private readonly HttpClient httpClient;
    private readonly NodalMigrationBundleExecutor executor;

    /// <summary>Loads secret configuration and creates one TigerGraph administrative provider.</summary>
    public TigerGraphMigrationHost()
    {
        var settings = MigrationHostSettings.Load();
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.GraphName);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.GsqlFile);
        httpClient = new HttpClient();
        var options = new TigerGraphOptions
        {
            Endpoint = settings.Endpoint,
            Username = settings.Username,
            Password = settings.Password,
            AccessToken = settings.AccessToken,
        };
        var transport = new TigerGraphGsqlProcessTransport(new TigerGraphGsqlProcessOptions
        {
            FileName = settings.GsqlFile,
            PrefixArguments = settings.GsqlPrefixArguments,
            Username = settings.Username,
            Password = settings.Password,
            AccessToken = settings.AccessToken,
            GraphName = settings.GraphName,
            VerifiedServerVersion = settings.ProviderVersion,
        });
        var provider = new TigerGraphProvider(httpClient, options, settings.GraphName, transport);
        executor = new NodalMigrationBundleExecutor(
            provider,
            new NodalMigrationBundleTarget(
                "TigerGraph",
                settings.ProviderVersion,
                settings.Capabilities.ToHashSet(StringComparer.Ordinal)));
    }

    /// <inheritdoc />
    public ValueTask<NodalMigrationBundleExecutionResult> ApplyAsync(
        NodalMigrationBundle bundle,
        NodalMigrationBundleExecutionOptions? options = null,
        CancellationToken cancellationToken = default) =>
        executor.ApplyAsync(bundle, options, cancellationToken);

    /// <inheritdoc />
    public ValueTask<NodalMigrationBundleExecutionResult> RevertAsync(
        NodalMigrationBundle bundle,
        NodalMigrationBundleExecutionOptions? options = null,
        CancellationToken cancellationToken = default) =>
        executor.RevertAsync(bundle, options, cancellationToken);

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        httpClient.Dispose();
        return ValueTask.CompletedTask;
    }
}
