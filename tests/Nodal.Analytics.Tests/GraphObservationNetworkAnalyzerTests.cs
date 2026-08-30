using Nodal.Analytics.DerivedNetworks;
using Nodal.Analytics.Observations;
using Nodal.Core.Execution;

namespace Nodal.Analytics.Tests;

public sealed class GraphObservationNetworkAnalyzerTests
{
    [Fact]
    public void ComputesDegreeComponentsAndConvergedPageRankDeterministically()
    {
        var observation = Observation(
            [Node("a"), Node("b"), Node("c"), Node("isolated")],
            [Relation("ab", "a", "b", "FLOW"), Relation("bc", "b", "c", "FLOW")]);

        var result = GraphObservationNetworkAnalyzer.Analyze(observation);

        Assert.True(result.Converged);
        Assert.InRange(result.Iterations, 1, 100);
        Assert.Equal(2, result.RelationCount);
        Assert.Equal([0, 0, 0, 1], result.Nodes.Select(node => node.WeakComponentId));
        Assert.Equal([1, 2, 1, 0], result.Nodes.Select(node => node.Degree));
        Assert.Equal(1d, result.Nodes.Sum(node => node.PageRank), 8);
        Assert.True(result.Nodes[2].PageRank > result.Nodes[0].PageRank);
    }

    [Fact]
    public void FiltersRelationTypesAndCanTreatNetworkAsUndirected()
    {
        var observation = Observation(
            [Node("a"), Node("b"), Node("c")],
            [Relation("ab", "a", "b", "FLOW"), Relation("bc", "b", "c", "NOISE")]);
        var result = GraphObservationNetworkAnalyzer.Analyze(observation, new DerivedNetworkAnalysisOptions
        {
            RelationTypes = new HashSet<string>(["FLOW"], StringComparer.Ordinal),
            TreatAsUndirected = true,
        });

        Assert.Equal(1, result.RelationCount);
        Assert.Equal(1, result.Nodes[0].InDegree);
        Assert.Equal(1, result.Nodes[0].OutDegree);
        Assert.Equal(2, result.Nodes[0].Degree);
        Assert.Equal(1, result.Nodes[2].WeakComponentId);
        Assert.Equal(result.Nodes[0].PageRank, result.Nodes[1].PageRank, 8);
    }

    [Fact]
    public void HandlesEmptyObservationAndReportsNonConvergenceBudget()
    {
        var empty = Observation([], []);
        var nonConverged = GraphObservationNetworkAnalyzer.Analyze(
            Observation([Node("a"), Node("b")], [Relation("ab", "a", "b", "FLOW")]),
            new DerivedNetworkAnalysisOptions { MaxIterations = 1, Tolerance = 1e-30 });

        var emptyResult = GraphObservationNetworkAnalyzer.Analyze(empty);
        Assert.Empty(emptyResult.Nodes);
        Assert.True(emptyResult.Converged);
        Assert.Equal(0, emptyResult.Iterations);
        Assert.False(nonConverged.Converged);
        Assert.Equal(1, nonConverged.Iterations);
    }

    [Theory]
    [InlineData(0, 10, 0.1)]
    [InlineData(1, 10, 0.1)]
    [InlineData(0.85, 0, 0.1)]
    [InlineData(0.85, 10, 0)]
    [InlineData(0.85, 10, double.NaN)]
    public void RejectsInvalidOptions(double damping, int iterations, double tolerance)
    {
        var options = new DerivedNetworkAnalysisOptions
        {
            DampingFactor = damping,
            MaxIterations = iterations,
            Tolerance = tolerance,
        };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GraphObservationNetworkAnalyzer.Analyze(Observation([Node("a")], []), options));
    }

    [Fact]
    public void RejectsNullInputsAndInvalidRelationSelection()
    {
        Assert.Throws<ArgumentNullException>(() => GraphObservationNetworkAnalyzer.Analyze(null!));
        Assert.Throws<ArgumentNullException>(() => GraphObservationNetworkAnalyzer.Analyze(
            Observation([Node("a")], []),
            new DerivedNetworkAnalysisOptions { RelationTypes = null! }));
        Assert.Throws<ArgumentException>(() => GraphObservationNetworkAnalyzer.Analyze(
            Observation([Node("a")], []),
            new DerivedNetworkAnalysisOptions { RelationTypes = new HashSet<string> { " " } }));
    }

    private static GraphObservation Observation(
        IReadOnlyList<GraphNodeRecord> nodes,
        IReadOnlyList<GraphRelationRecord> relations) =>
        GraphObservationMaterializer.Materialize(new GraphQueryResult(nodes, relations));

    private static GraphNodeRecord Node(string id) => new("Node", id, new Dictionary<string, object?>());

    private static GraphRelationRecord Relation(string id, string source, string target, string type) =>
        new(type, id, source, target, new Dictionary<string, object?>());
}
