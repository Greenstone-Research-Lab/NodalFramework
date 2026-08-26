using Nodal.Core.Migrations;

namespace Nodal.TigerGraph.Tests;

public sealed class TigerGraphGsqlProcessTransportTests
{
    [Fact]
    public void ConstructorValidatesExecutableAndCredentialPair()
    {
        Assert.Throws<ArgumentNullException>(() => new TigerGraphGsqlProcessTransport(null!));
        Assert.Throws<ArgumentException>(() => new TigerGraphGsqlProcessTransport(
            new TigerGraphGsqlProcessOptions { FileName = " " }));
        Assert.Throws<ArgumentException>(() => new TigerGraphGsqlProcessTransport(
            new TigerGraphGsqlProcessOptions { FileName = "gsql", Username = "user" }));
    }

    [Fact]
    public async Task ControlPlaneValidatesIdentifiersBeforeStartingProcess()
    {
        var transport = new TigerGraphGsqlProcessTransport(new TigerGraphGsqlProcessOptions
        {
            FileName = "missing-nodal-gsql",
        });

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await transport.DiscoverCapabilitiesAsync("bad;graph"));
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await transport.SchemaJobExistsAsync("Graph", "bad job"));
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await transport.AcquireMigrationLockAsync(" "));
    }

    [Fact]
    public async Task ProcessMigrationLockIsGraphScopedCancellationAwareAndIdempotentlyReleased()
    {
        var transport = new TigerGraphGsqlProcessTransport(new TigerGraphGsqlProcessOptions
        {
            FileName = "dotnet",
        });
        var first = await transport.AcquireMigrationLockAsync("LockGraph");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await transport.AcquireMigrationLockAsync("LockGraph", cancellation.Token));
        await first.DisposeAsync();
        await first.DisposeAsync();
        await using var second = await transport.AcquireMigrationLockAsync("LockGraph");
    }

    [Fact]
    public async Task PortableLockAdaptsPrefixedAndPlainGraphScopes()
    {
        var controlPlane = new LockRecordingControlPlane();
        var migrationLock = new TigerGraphMigrationLock(controlPlane);

        await using var first = await migrationLock.AcquireAsync("tigergraph:SocialGraph");
        await using var second = await migrationLock.AcquireAsync("OtherGraph");

        Assert.Equal(["SocialGraph", "OtherGraph"], controlPlane.GraphNames);
    }

    [Fact]
    public void CapabilitiesRejectIncompleteAdministrativeBoundary()
    {
        var complete = new TigerGraphAdministrativeCapabilities(
            "4.2.4", true, true, true, true, TigerGraphMigrationLockScope.Process);
        complete.EnsureMigrationSupport();

        var incomplete = complete with { CanCleanupJobs = false };
        var exception = Assert.Throws<NodalCapabilityNotSupportedException>(
            incomplete.EnsureMigrationSupport);
        Assert.Contains("NODAL-TIGERGRAPH-MIGRATION-CONTROL-PLANE", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteReportsProcessFailureWithExitCode()
    {
        var transport = new TigerGraphGsqlProcessTransport(new TigerGraphGsqlProcessOptions
        {
            FileName = "dotnet",
            PrefixArguments = ["--nodal-invalid-option"],
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await transport.ExecuteAsync(new MigrationCommand("LS", false)));

        Assert.Contains("exit code", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class LockRecordingControlPlane : ITigerGraphAdministrativeControlPlane
    {
        public List<string> GraphNames { get; } = [];

        public ValueTask<IAsyncDisposable> AcquireMigrationLockAsync(
            string graphName,
            CancellationToken cancellationToken = default)
        {
            GraphNames.Add(graphName);
            return ValueTask.FromResult<IAsyncDisposable>(new Lease());
        }

        public ValueTask<TigerGraphAdministrativeCapabilities> DiscoverCapabilitiesAsync(
            string graphName,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<bool> SchemaJobExistsAsync(
            string graphName,
            string jobName,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask ExecuteAsync(
            MigrationCommand command,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Lease : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
