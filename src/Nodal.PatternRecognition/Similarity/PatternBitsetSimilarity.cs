using System.Numerics;
using System.Runtime.Intrinsics;

namespace Nodal.PatternRecognition.Similarity;

/// <summary>
/// Computes allocation-free structural similarity over equally shaped path feature vectors.
/// </summary>
public static class PatternBitsetSimilarity
{
    /// <summary>
    /// Compares two typed multi-hot vectors using XOR, AND, OR, and population-count operations.
    /// </summary>
    /// <remarks>
    /// Two empty vectors are treated as identical and receive Jaccard and binary-cosine scores of one.
    /// A single empty vector receives zero for those two measures. The production path deliberately uses
    /// the portable scalar kernel. The first benchmark found that the .NET 10 JIT-generated popcount
    /// loop outperformed manual unrolling and a Vector256 lane-reduction candidate on the test machine.
    /// </remarks>
    /// <param name="left">The first path feature vector.</param>
    /// <param name="right">The second path feature vector using the same schema.</param>
    /// <returns>Exact and normalized similarity measurements.</returns>
    public static PatternSimilarityScore Compare(PatternBitVector left, PatternBitVector right)
    {
        Validate(left, right);
        return CreateScore(CompareScalar(left.Words, right.Words), left, right);
    }

    internal static PatternSimilarityScore CompareScalar(PatternBitVector left, PatternBitVector right)
    {
        Validate(left, right);
        return CreateScore(CompareScalar(left.Words, right.Words), left, right);
    }

    internal static PatternSimilarityScore CompareVector256(PatternBitVector left, PatternBitVector right)
    {
        Validate(left, right);
        return CreateScore(CompareVector256(left.Words, right.Words), left, right);
    }

    internal static PatternSimilarityScore CompareUnrolled(PatternBitVector left, PatternBitVector right)
    {
        Validate(left, right);
        return CreateScore(CompareUnrolled(left.Words, right.Words), left, right);
    }

    private static void Validate(PatternBitVector left, PatternBitVector right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (left.FeatureCount != right.FeatureCount)
        {
            throw new ArgumentException("Pattern vectors must use the same feature schema.", nameof(right));
        }
    }

    private static Counts CompareScalar(ReadOnlySpan<ulong> left, ReadOnlySpan<ulong> right)
    {
        var counts = new Counts();
        for (var index = 0; index < left.Length; index++)
        {
            counts.Add(left[index], right[index]);
        }

        return counts;
    }

    private static Counts CompareUnrolled(ReadOnlySpan<ulong> left, ReadOnlySpan<ulong> right)
    {
        var counts = new Counts();
        var index = 0;
        for (; index <= left.Length - 4; index += 4)
        {
            counts.Add(left[index], right[index]);
            counts.Add(left[index + 1], right[index + 1]);
            counts.Add(left[index + 2], right[index + 2]);
            counts.Add(left[index + 3], right[index + 3]);
        }

        for (; index < left.Length; index++)
        {
            counts.Add(left[index], right[index]);
        }

        return counts;
    }

    private static Counts CompareVector256(ReadOnlySpan<ulong> left, ReadOnlySpan<ulong> right)
    {
        if (!Vector256.IsHardwareAccelerated || left.Length < Vector256<ulong>.Count)
        {
            return CompareUnrolled(left, right);
        }

        var counts = new Counts();
        var index = 0;
        for (; index <= left.Length - Vector256<ulong>.Count; index += Vector256<ulong>.Count)
        {
            var leftVector = Vector256.Create(left.Slice(index, Vector256<ulong>.Count));
            var rightVector = Vector256.Create(right.Slice(index, Vector256<ulong>.Count));
            var xor = leftVector ^ rightVector;
            var and = leftVector & rightVector;
            var or = leftVector | rightVector;

            for (var lane = 0; lane < Vector256<ulong>.Count; lane++)
            {
                counts.Difference += BitOperations.PopCount(xor.GetElement(lane));
                counts.Intersection += BitOperations.PopCount(and.GetElement(lane));
                counts.Union += BitOperations.PopCount(or.GetElement(lane));
            }
        }

        for (; index < left.Length; index++)
        {
            counts.Add(left[index], right[index]);
        }

        return counts;
    }

    private static PatternSimilarityScore CreateScore(
        Counts counts,
        PatternBitVector left,
        PatternBitVector right)
    {
        var hammingDistance = (double)counts.Difference / left.FeatureCount;
        var jaccard = counts.Union == 0 ? 1d : (double)counts.Intersection / counts.Union;
        var cosineDenominator = Math.Sqrt((double)left.SetBitCount * right.SetBitCount);
        var cosine = cosineDenominator == 0
            ? left.SetBitCount == right.SetBitCount ? 1d : 0d
            : counts.Intersection / cosineDenominator;

        return new PatternSimilarityScore(
            counts.Difference,
            counts.Intersection,
            counts.Union,
            hammingDistance,
            1d - hammingDistance,
            jaccard,
            cosine);
    }

    private struct Counts
    {
        public int Difference;
        public int Intersection;
        public int Union;

        public void Add(ulong left, ulong right)
        {
            Difference += BitOperations.PopCount(left ^ right);
            Intersection += BitOperations.PopCount(left & right);
            Union += BitOperations.PopCount(left | right);
        }
    }
}
