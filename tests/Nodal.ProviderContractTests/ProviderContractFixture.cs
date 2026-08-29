using Nodal.Core.Execution;

namespace Nodal.ProviderContractTests;

public sealed class ProviderContractFixture(
    IGraphProvider provider,
    Func<ValueTask>? disposeAsync = null) : IAsyncDisposable
{
    public IGraphProvider Provider { get; } = provider;

    public ValueTask DisposeAsync() => disposeAsync?.Invoke() ?? ValueTask.CompletedTask;
}
