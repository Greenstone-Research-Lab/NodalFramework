namespace Nodal.Core.Mutations;

/// <summary>Describes the transaction boundary exposed by a graph provider.</summary>
public enum GraphTransactionScope
{
    /// <summary>The provider does not expose an atomic mutation boundary.</summary>
    None,

    /// <summary>One complete request or installed query is the transaction boundary.</summary>
    RequestOrQuery,

    /// <summary>The client can execute multiple commands inside an explicit transaction.</summary>
    ClientManaged,
}

/// <summary>Describes mutation guarantees implemented by a Nodal provider.</summary>
public sealed record GraphProviderCapabilities
{
    /// <summary>Gets whether successful mutation execution is transactional.</summary>
    public required bool SupportsTransactions { get; init; }

    /// <summary>Gets whether a complete Nodal mutation plan can be submitted atomically.</summary>
    public required bool SupportsAtomicBatch { get; init; }

    /// <summary>Gets the transaction boundary implemented by the provider.</summary>
    public required GraphTransactionScope TransactionScope { get; init; }

    /// <summary>Gets whether nested savepoints are implemented by the provider.</summary>
    public bool SupportsSavepoints { get; init; }

    /// <summary>Gets whether optimistic concurrency checks are implemented by the provider.</summary>
    public bool SupportsOptimisticConcurrency { get; init; }
}
