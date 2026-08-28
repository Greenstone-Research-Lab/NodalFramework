using Neo4j.Driver;
using Nodal.Core.Mutations;
using Nodal.Neo4j;
using NSubstitute;

namespace Nodal.ProviderContractTests;

public sealed class Neo4jProviderContractTests : GraphProviderContractTests
{
    protected override string ExpectedProviderName => "Neo4j";

    protected override GraphTransactionScope ExpectedTransactionScope => GraphTransactionScope.ClientManaged;

    protected override ProviderContractFixture CreateProvider()
    {
        var provider = new Neo4jProvider(Substitute.For<IDriver>());
        return new ProviderContractFixture(provider, provider.DisposeAsync);
    }
}
