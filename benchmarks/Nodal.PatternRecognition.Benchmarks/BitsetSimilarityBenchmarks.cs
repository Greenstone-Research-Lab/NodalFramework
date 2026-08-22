using BenchmarkDotNet.Attributes;
using Nodal.Analytics.Similarity;

namespace Nodal.Analytics.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class BitsetSimilarityBenchmarks
{
    private PatternBitVector left = null!;
    private PatternBitVector right = null!;

    [Params(256, 4_096, 16_384)]
    public int FeatureCount { get; set; }

    [Params(0.05, 0.25)]
    public double Density { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(1701 + FeatureCount + (int)(Density * 100));
        left = CreateVector(random);
        right = CreateVector(random);
    }

    [Benchmark(Baseline = true)]
    public PatternSimilarityScore ScalarOracle() => PatternBitsetSimilarity.CompareScalar(left, right);

    [Benchmark]
    public PatternSimilarityScore ProductionScalar() => PatternBitsetSimilarity.Compare(left, right);

    [Benchmark]
    public PatternSimilarityScore UnrolledCandidate() => PatternBitsetSimilarity.CompareUnrolled(left, right);

    [Benchmark]
    public PatternSimilarityScore Vector256Candidate() => PatternBitsetSimilarity.CompareVector256(left, right);

    private PatternBitVector CreateVector(Random random)
    {
        var features = Enumerable.Range(0, FeatureCount).Where(_ => random.NextDouble() < Density);
        return PatternBitVector.Create(FeatureCount, features);
    }
}
