namespace Nodal.Core.Mutations;

/// <summary>
/// Contains an ordered, provider-neutral unit of graph work.
/// </summary>
/// <param name="Operations">Operations in dependency-safe execution order.</param>
public sealed record GraphMutationPlan(IReadOnlyList<GraphMutationOperation> Operations)
{
    /// <summary>Gets whether this plan contains no work.</summary>
    public bool IsEmpty => Operations.Count == 0;
}

/// <summary>Describes the provider-confirmed outcome of a mutation plan.</summary>
/// <param name="AffectedNodes">The number of affected nodes.</param>
/// <param name="AffectedRelations">The number of affected relationships.</param>
/// <param name="IsAtomic">Whether the provider committed the complete plan atomically.</param>
public sealed record GraphMutationResult(int AffectedNodes, int AffectedRelations, bool IsAtomic);

/// <summary>Contains immutable graph changes emitted by a successful unit of work.</summary>
/// <param name="Operations">The committed provider-neutral operations.</param>
public sealed record GraphChangeSet(IReadOnlyList<GraphMutationOperation> Operations);

/// <summary>Reports a successful <c>SaveChangesAsync</c> operation.</summary>
/// <param name="AffectedNodes">The provider-confirmed affected node count.</param>
/// <param name="AffectedRelations">The provider-confirmed affected relationship count.</param>
/// <param name="IsAtomic">Whether all operations were committed atomically.</param>
/// <param name="Changes">The committed immutable change set.</param>
public sealed record GraphSaveResult(
    int AffectedNodes,
    int AffectedRelations,
    bool IsAtomic,
    GraphChangeSet Changes);
