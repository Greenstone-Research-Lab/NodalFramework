using Nodal.Core.Mutations;
using Nodal.TigerGraph;

namespace Nodal.ProviderContractTests;

public sealed class TigerGraphProviderContractTests : GraphProviderContractTests
{
    protected override string ExpectedProviderName => "TigerGraph";

    protected override GraphTransactionScope ExpectedTransactionScope => GraphTransactionScope.RequestOrQuery;

    protected override ProviderContractFixture CreateProvider()
    {
        var httpClient = new HttpClient();
        var provider = new TigerGraphProvider(
            httpClient,
            new TigerGraphOptions { Endpoint = new Uri("https://contract.invalid") },
            "ContractGraph");
        return new ProviderContractFixture(provider, () =>
        {
            httpClient.Dispose();
            return ValueTask.CompletedTask;
        });
    }
}
