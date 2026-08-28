using Nodal.Core.Execution;
using Nodal.Core.Migrations;
using Nodal.Core.Mutations;
using Nodal.Core.Providers;
using Nodal.Core.Query;
using Nodal.Neo4j;

namespace WorldFoodDelivery.Infrastructure;

internal sealed class ConsumerSmokeProvider : IGraphProvider, IGraphMutationProvider
{
    public IGraphQueryCompiler QueryCompiler { get; } = new Neo4jQueryCompiler();
    public IGraphCommandExecutor CommandExecutor { get; } = new NoopExecutor();
    public IGraphResultMaterializer ResultMaterializer { get; } = new JsonGraphResultMaterializer();
    public IGraphMutationExecutor MutationExecutor { get; } = new RecordingMutationExecutor();

    public GraphProviderCapabilities Capabilities { get; } = new()
    {
        SupportsTransactions = true,
        SupportsAtomicBatch = true,
        TransactionScope = GraphTransactionScope.RequestOrQuery,
    };

    private sealed class NoopExecutor : IGraphCommandExecutor
    {
        public ValueTask<GraphQueryResult> ExecuteAsync(
            GraphCommand command,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new GraphQueryResult([]));
    }

    private sealed class RecordingMutationExecutor : IGraphMutationExecutor
    {
        public ValueTask<GraphMutationResult> ExecuteAsync(
            GraphMutationPlan plan,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new GraphMutationResult(
                plan.Operations.Count(operation => operation is not CreateRelationOperation),
                plan.Operations.Count(operation => operation is CreateRelationOperation),
                IsAtomic: true));
    }
}
