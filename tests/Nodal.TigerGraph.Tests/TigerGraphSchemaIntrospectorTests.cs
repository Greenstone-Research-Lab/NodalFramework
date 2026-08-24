using Nodal.Core.Migrations;

namespace Nodal.TigerGraph.Tests;

public sealed class TigerGraphSchemaIntrospectorTests
{
    [Fact]
    public async Task CapturesAndNormalizesSchemaThroughExplicitTransport()
    {
        var expected = new NodalSchemaSnapshot(
            1,
            [new NodalNodeSnapshot("Person", "System.Object", "Id", [])],
            [],
            "ignored",
            "4.2");
        var transport = new RecordingSchemaTransport(expected);
        var snapshot = await new TigerGraphSchemaIntrospector(transport, "SocialGraph").CaptureAsync();

        Assert.Equal("TigerGraph", snapshot.ProviderName);
        Assert.Equal("SocialGraph", transport.GraphName);
    }

    private sealed class RecordingSchemaTransport(NodalSchemaSnapshot snapshot) : ITigerGraphSchemaIntrospectionTransport
    {
        public string? GraphName { get; private set; }

        public ValueTask<NodalSchemaSnapshot> CaptureSchemaAsync(string graphName, CancellationToken cancellationToken = default)
        {
            GraphName = graphName;
            return ValueTask.FromResult(snapshot);
        }
    }
}
