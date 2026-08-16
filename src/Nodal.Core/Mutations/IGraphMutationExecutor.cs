namespace Nodal.Core.Mutations;

/// <summary>Executes an ordered provider-neutral mutation plan.</summary>
public interface IGraphMutationExecutor
{
    /// <summary>Executes a complete graph unit of work.</summary>
    ValueTask<GraphMutationResult> ExecuteAsync(
        GraphMutationPlan plan,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Marks a graph provider that supports writes in addition to the read pipeline.
/// </summary>
public interface IGraphMutationProvider
{
    /// <summary>Gets the mutation and transaction guarantees implemented by this provider.</summary>
    GraphProviderCapabilities Capabilities { get; }

    /// <summary>Gets the provider-specific mutation executor.</summary>
    IGraphMutationExecutor MutationExecutor { get; }
}
