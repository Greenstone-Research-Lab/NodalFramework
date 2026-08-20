using Nodal.Core.Execution;

namespace Nodal.Samples.SocialGraph;

/// <summary>
/// Runs the same create, traverse, and update workflow against any Nodal graph provider.
/// </summary>
public static class SocialGraphDemo
{
    /// <summary>
    /// Persists two people and their relationship, traverses the resulting path, and updates its payload.
    /// </summary>
    /// <param name="provider">The provider used to execute the workflow.</param>
    /// <param name="cancellationToken">A token used to cancel asynchronous operations.</param>
    /// <returns>A summary of the persisted and verified graph state.</returns>
    public static async Task<SocialGraphDemoResult> RunAsync(
        IGraphProvider provider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var ada = new Person($"ada-{suffix}", "Ada");
        var alan = new Person($"alan-{suffix}", "Alan");
        var knows = new Knows(2020);
        var context = new SocialGraphContext(provider);

        context.People.Add(ada);
        context.People.Add(alan);
        context.Friendships.Connect(ada, knows, alan);
        var created = await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var readContext = new SocialGraphContext(provider);
        var path = await readContext.People
            .Match(person => person.Id == ada.Id)
            .TraversePath(readContext.Friendships)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        path.Source.Name = "Ada Lovelace";
        path.Relation.SinceYear = 2025;
        readContext.People.Update(path.Source);
        readContext.Friendships.Update(path.Source, path.Relation, path.Target);
        var updated = await readContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var verificationContext = new SocialGraphContext(provider);
        var verifiedPath = await verificationContext.People
            .Match(person => person.Id == ada.Id)
            .TraversePath(verificationContext.Friendships)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        return new SocialGraphDemoResult(
            verifiedPath.Source.Id,
            verifiedPath.Source.Name,
            verifiedPath.Target.Id,
            verifiedPath.Target.Name,
            verifiedPath.Relation.SinceYear,
            created.AffectedNodes,
            created.AffectedRelations,
            updated.AffectedNodes,
            updated.AffectedRelations,
            created.IsAtomic && updated.IsAtomic);
    }
}

/// <summary>
/// Summarizes the provider-neutral social graph workflow.
/// </summary>
/// <param name="SourceId">The persisted source node identifier.</param>
/// <param name="SourceName">The verified source node name.</param>
/// <param name="TargetId">The persisted target node identifier.</param>
/// <param name="TargetName">The verified target node name.</param>
/// <param name="SinceYear">The verified relationship year.</param>
/// <param name="CreatedNodes">The number of nodes created by the unit of work.</param>
/// <param name="CreatedRelations">The number of relationships created by the unit of work.</param>
/// <param name="UpdatedNodes">The number of nodes updated by the unit of work.</param>
/// <param name="UpdatedRelations">The number of relationships updated by the unit of work.</param>
/// <param name="IsAtomic">Whether both provider operations reported atomic execution.</param>
public sealed record SocialGraphDemoResult(
    string SourceId,
    string SourceName,
    string TargetId,
    string TargetName,
    int SinceYear,
    int CreatedNodes,
    int CreatedRelations,
    int UpdatedNodes,
    int UpdatedRelations,
    bool IsAtomic);
