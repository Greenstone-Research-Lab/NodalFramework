using Nodal.Core.ChangeTracking;

namespace Nodal.Core.Mutations;

internal static class GraphMutationPlanner
{
    public static GraphMutationPlan Create(IReadOnlyList<GraphEntry> entries)
    {
        var operations = new List<GraphMutationOperation>();
        AddNodes(entries, GraphEntryState.Added, operations);
        AddRelations(entries, GraphEntryState.Added, operations);
        AddNodes(entries, GraphEntryState.Modified, operations);
        AddRelations(entries, GraphEntryState.Modified, operations);
        AddRelations(entries, GraphEntryState.Deleted, operations);
        AddNodes(entries, GraphEntryState.Deleted, operations);
        return new GraphMutationPlan(operations);
    }

    private static void AddNodes(
        IEnumerable<GraphEntry> entries,
        GraphEntryState state,
        List<GraphMutationOperation> operations)
    {
        foreach (var entry in entries.Where(entry => entry.State == state))
        {
            if (entry is not IGraphNodeEntry nodeEntry)
            {
                continue;
            }

            operations.Add(state switch
            {
                GraphEntryState.Added => new CreateNodeOperation(nodeEntry.Identity, nodeEntry.ReadProperties()),
                GraphEntryState.Modified => new UpdateNodeOperation(nodeEntry.Identity, nodeEntry.ReadProperties()),
                GraphEntryState.Deleted => new DeleteNodeOperation(nodeEntry.Identity),
                _ => throw new InvalidOperationException($"State '{state}' is not a node mutation state."),
            });
        }
    }

    private static void AddRelations(
        IEnumerable<GraphEntry> entries,
        GraphEntryState state,
        List<GraphMutationOperation> operations)
    {
        foreach (var entry in entries.Where(entry => entry.State == state))
        {
            if (entry is not IGraphRelationEntry relationEntry)
            {
                continue;
            }

            operations.Add(state switch
            {
                GraphEntryState.Added => new CreateRelationOperation(
                    relationEntry.SourceIdentity,
                    relationEntry.Metadata.Name,
                    relationEntry.TargetIdentity,
                    relationEntry.Metadata.Directed,
                    relationEntry.ReadProperties()),
                GraphEntryState.Modified => new UpdateRelationOperation(
                    relationEntry.SourceIdentity,
                    relationEntry.Metadata.Name,
                    relationEntry.TargetIdentity,
                    relationEntry.Metadata.Directed,
                    relationEntry.ReadProperties(),
                    relationEntry.ProviderId),
                GraphEntryState.Deleted => new DeleteRelationOperation(
                    relationEntry.SourceIdentity,
                    relationEntry.Metadata.Name,
                    relationEntry.TargetIdentity,
                    relationEntry.Metadata.Directed),
                _ => throw new InvalidOperationException($"State '{state}' is not a relationship mutation state."),
            });
        }
    }
}
