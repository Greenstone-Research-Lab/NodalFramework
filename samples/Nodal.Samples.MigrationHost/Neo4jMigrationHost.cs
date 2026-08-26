using Nodal.Migrations;
using Nodal.Neo4j;

namespace Nodal.Samples.MigrationHost;

/// <summary>
/// Demonstrates a trusted Neo4j deployment composition root for <c>Nodal.Tool</c>.
/// </summary>
public sealed class Neo4jMigrationHost : INodalMigrationBundleExecutionHost, IAsyncDisposable
{
    private readonly Neo4jProvider provider;
    private readonly NodalMigrationBundleExecutor executor;

    /// <summary>Loads secret configuration and creates one pooled Neo4j provider.</summary>
    public Neo4jMigrationHost()
    {
        var settings = MigrationHostSettings.Load();
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.Username);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.Password);
        provider = new Neo4jProvider(new Neo4jOptions
        {
            Endpoint = settings.Endpoint,
            Username = settings.Username,
            Password = settings.Password,
            Database = settings.Database,
        });
        executor = new NodalMigrationBundleExecutor(
            provider,
            new NodalMigrationBundleTarget(
                "Neo4j",
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
    public ValueTask DisposeAsync() => provider.DisposeAsync();
}
