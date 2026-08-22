using Nodal.Analytics.Similarity;

namespace Nodal.Analytics.Tests;

public sealed class PatternBitsetSimilarityTests
{
    [Fact]
    public void IdenticalVectorsReturnPerfectSimilarity()
    {
        var vector = PatternBitVector.Create(256, [0, 63, 64, 255]);

        var score = PatternBitsetSimilarity.Compare(vector, vector);

        Assert.Equal(0, score.DifferenceCount);
        Assert.Equal(4, score.IntersectionCount);
        Assert.Equal(4, score.UnionCount);
        Assert.Equal(0d, score.HammingDistance);
        Assert.Equal(1d, score.HammingSimilarity);
        Assert.Equal(1d, score.JaccardSimilarity);
        Assert.Equal(1d, score.BinaryCosineSimilarity);
    }

    [Fact]
    public void PartialOverlapReturnsExplainableCountsAndScores()
    {
        var left = PatternBitVector.Create(8, [0, 1, 4]);
        var right = PatternBitVector.Create(8, [1, 2, 4]);

        var score = PatternBitsetSimilarity.Compare(left, right);

        Assert.Equal(2, score.DifferenceCount);
        Assert.Equal(2, score.IntersectionCount);
        Assert.Equal(4, score.UnionCount);
        Assert.Equal(0.25d, score.HammingDistance);
        Assert.Equal(0.75d, score.HammingSimilarity);
        Assert.Equal(0.5d, score.JaccardSimilarity);
        Assert.Equal(2d / 3d, score.BinaryCosineSimilarity, precision: 12);
    }

    [Fact]
    public void EmptyVectorsAreTreatedAsIdentical()
    {
        var empty = PatternBitVector.Create(128, []);

        var score = PatternBitsetSimilarity.Compare(empty, empty);

        Assert.Equal(1d, score.JaccardSimilarity);
        Assert.Equal(1d, score.BinaryCosineSimilarity);
    }

    [Fact]
    public void DuplicateFeaturesAreIdempotent()
    {
        var vector = PatternBitVector.Create(64, [3, 3, 3]);

        Assert.Equal(1, vector.SetBitCount);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(64)]
    public void OutOfRangeFeatureIsRejected(int feature)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PatternBitVector.Create(64, [feature]));
    }

    [Fact]
    public void DifferentSchemasAreRejected()
    {
        var left = PatternBitVector.Create(64, []);
        var right = PatternBitVector.Create(65, []);

        Assert.Throws<ArgumentException>(() => PatternBitsetSimilarity.Compare(left, right));
    }

    [Fact]
    public void OptimizedKernelsMatchScalarOracleAcrossShapesAndDensities()
    {
        var random = new Random(1701);
        foreach (var featureCount in new[] { 1, 63, 64, 65, 256, 1_024, 4_096 })
        {
            foreach (var density in new[] { 0d, 0.005d, 0.05d, 0.25d, 0.5d, 1d })
            {
                for (var sample = 0; sample < 10; sample++)
                {
                    var left = CreateRandom(featureCount, density, random);
                    var right = CreateRandom(featureCount, density, random);
                    var expected = PatternBitsetSimilarity.CompareScalar(left, right);

                    Assert.Equal(expected, PatternBitsetSimilarity.Compare(left, right));
                    Assert.Equal(expected, PatternBitsetSimilarity.CompareUnrolled(left, right));
                    Assert.Equal(expected, PatternBitsetSimilarity.CompareVector256(left, right));
                }
            }
        }
    }

    private static PatternBitVector CreateRandom(int featureCount, double density, Random random)
    {
        var active = Enumerable.Range(0, featureCount).Where(_ => random.NextDouble() < density);
        return PatternBitVector.Create(featureCount, active);
    }
}
