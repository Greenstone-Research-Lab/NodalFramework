using Nodal.Analytics.Observations;

namespace Nodal.Analytics.DerivedNetworks;

/// <summary>
/// Computes transparent baseline metrics over an already bounded canonical observation.
/// This analyzer never replaces provider-native analytics for database-resident graphs.
/// </summary>
public static class GraphObservationNetworkAnalyzer
{
    /// <summary>Computes degree, weak-component, and PageRank metrics.</summary>
    /// <param name="observation">A bounded canonical observation.</param>
    /// <param name="options">Optional relation selection and PageRank settings.</param>
    /// <returns>Deterministic metrics in observation node order.</returns>
    public static DerivedNetworkAnalysis Analyze(
        GraphObservation observation,
        DerivedNetworkAnalysisOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(observation);
        options ??= new DerivedNetworkAnalysisOptions();
        Validate(options);
        var relationTypes = CopyRelationTypes(options.RelationTypes);
        var nodes = observation.Nodes.Select(node => node.Identity).ToArray();
        if (nodes.Length == 0)
        {
            return new DerivedNetworkAnalysis([], 0, 0, true);
        }

        var indices = nodes.Select((identity, index) => (identity, index))
            .ToDictionary(item => item.identity, item => item.index);
        var edges = observation.Relations
            .Where(relation => relationTypes.Count == 0 || relationTypes.Contains(relation.Type))
            .Select(relation => Resolve(relation, indices))
            .ToArray();
        var incoming = new int[nodes.Length];
        var outgoing = new int[nodes.Length];
        var adjacency = Enumerable.Range(0, nodes.Length).Select(_ => new List<int>()).ToArray();
        foreach (var edge in edges)
        {
            outgoing[edge.Source]++;
            incoming[edge.Target]++;
            adjacency[edge.Source].Add(edge.Target);
            if (options.TreatAsUndirected && edge.Source != edge.Target)
            {
                outgoing[edge.Target]++;
                incoming[edge.Source]++;
                adjacency[edge.Target].Add(edge.Source);
            }
        }

        var components = Components(nodes.Length, edges);
        var (ranks, iterations, converged) = PageRank(adjacency, options);
        var metrics = nodes.Select((node, index) => new DerivedNodeMetrics(
            node,
            incoming[index],
            outgoing[index],
            incoming[index] + outgoing[index],
            ranks[index],
            components[index])).ToArray();
        return new DerivedNetworkAnalysis(Array.AsReadOnly(metrics), edges.Length, iterations, converged);
    }

    private static (double[] Ranks, int Iterations, bool Converged) PageRank(
        List<int>[] adjacency,
        DerivedNetworkAnalysisOptions options)
    {
        var count = adjacency.Length;
        var ranks = Enumerable.Repeat(1d / count, count).ToArray();
        var next = new double[count];
        for (var iteration = 1; iteration <= options.MaxIterations; iteration++)
        {
            Array.Fill(next, (1d - options.DampingFactor) / count);
            var dangling = 0d;
            for (var source = 0; source < count; source++)
            {
                if (adjacency[source].Count == 0)
                {
                    dangling += ranks[source];
                    continue;
                }

                var contribution = options.DampingFactor * ranks[source] / adjacency[source].Count;
                foreach (var target in adjacency[source])
                {
                    next[target] += contribution;
                }
            }

            var danglingContribution = options.DampingFactor * dangling / count;
            var delta = 0d;
            for (var index = 0; index < count; index++)
            {
                next[index] += danglingContribution;
                delta += Math.Abs(next[index] - ranks[index]);
            }

            (ranks, next) = (next, ranks);
            if (delta <= options.Tolerance)
            {
                return (ranks, iteration, true);
            }
        }

        return (ranks, options.MaxIterations, false);
    }

    private static int[] Components(int nodeCount, IReadOnlyList<(int Source, int Target)> edges)
    {
        var neighbors = Enumerable.Range(0, nodeCount).Select(_ => new List<int>()).ToArray();
        foreach (var edge in edges)
        {
            neighbors[edge.Source].Add(edge.Target);
            if (edge.Source != edge.Target)
            {
                neighbors[edge.Target].Add(edge.Source);
            }
        }

        var components = Enumerable.Repeat(-1, nodeCount).ToArray();
        var queue = new Queue<int>();
        var component = 0;
        for (var start = 0; start < nodeCount; start++)
        {
            if (components[start] >= 0)
            {
                continue;
            }

            components[start] = component;
            queue.Enqueue(start);
            while (queue.TryDequeue(out var node))
            {
                foreach (var neighbor in neighbors[node])
                {
                    if (components[neighbor] < 0)
                    {
                        components[neighbor] = component;
                        queue.Enqueue(neighbor);
                    }
                }
            }

            component++;
        }

        return components;
    }

    private static (int Source, int Target) Resolve(
        GraphObservationRelation relation,
        Dictionary<GraphObservationNodeIdentity, int> indices)
    {
        if (!indices.TryGetValue(relation.Source, out var source) || !indices.TryGetValue(relation.Target, out var target))
        {
            throw new ArgumentException("The observation relation references an unknown node.", nameof(relation));
        }

        return (source, target);
    }

    private static HashSet<string> CopyRelationTypes(IReadOnlySet<string> relationTypes)
    {
        ArgumentNullException.ThrowIfNull(relationTypes);
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relationType in relationTypes)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(relationType);
            result.Add(relationType);
        }

        return result;
    }

    private static void Validate(DerivedNetworkAnalysisOptions options)
    {
        if (options.DampingFactor is <= 0 or >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Damping factor must be between zero and one.");
        }

        if (options.MaxIterations <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Maximum iterations must be positive.");
        }

        if (!double.IsFinite(options.Tolerance) || options.Tolerance <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Tolerance must be a finite positive value.");
        }
    }
}
