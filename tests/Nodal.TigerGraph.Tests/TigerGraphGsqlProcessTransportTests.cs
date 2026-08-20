using Nodal.Core.Migrations;

namespace Nodal.TigerGraph.Tests;

public sealed class TigerGraphGsqlProcessTransportTests
{
    [Fact]
    public void ConstructorValidatesExecutableAndCredentialPair()
    {
        Assert.Throws<ArgumentException>(() => new TigerGraphGsqlProcessTransport(
            new TigerGraphGsqlProcessOptions { FileName = " " }));
        Assert.Throws<ArgumentException>(() => new TigerGraphGsqlProcessTransport(
            new TigerGraphGsqlProcessOptions { FileName = "gsql", Username = "user" }));
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
}
