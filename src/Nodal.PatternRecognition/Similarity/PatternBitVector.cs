using System.Numerics;

namespace Nodal.Analytics.Similarity;

/// <summary>
/// Represents a fixed-width, typed multi-hot feature vector used to compare canonical graph paths.
/// </summary>
/// <remarks>
/// Feature indexes must be assigned by a stable schema. Keeping the schema outside this value allows
/// node labels, relationship types, directions, positions, n-grams, and temporal buckets to occupy
/// distinct ranges without coupling the similarity kernel to a particular graph provider.
/// </remarks>
public sealed class PatternBitVector
{
    private readonly ulong[] words;

    private PatternBitVector(int featureCount, ulong[] words)
    {
        FeatureCount = featureCount;
        this.words = words;
        SetBitCount = words.Sum(static word => BitOperations.PopCount(word));
    }

    /// <summary>Gets the number of addressable features in the vector schema.</summary>
    public int FeatureCount { get; }

    /// <summary>Gets the number of active features.</summary>
    public int SetBitCount { get; }

    /// <summary>Gets the packed 64-bit words without allocating a copy.</summary>
    public ReadOnlySpan<ulong> Words => words;

    /// <summary>
    /// Creates a fixed-width vector from active feature indexes. Duplicate indexes are idempotent.
    /// </summary>
    /// <param name="featureCount">The positive size of the shared feature schema.</param>
    /// <param name="activeFeatures">Zero-based indexes of features present in the path.</param>
    /// <returns>A packed immutable feature vector.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the schema is empty or an index is outside the schema.
    /// </exception>
    public static PatternBitVector Create(int featureCount, IEnumerable<int> activeFeatures)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(featureCount);
        ArgumentNullException.ThrowIfNull(activeFeatures);

        var words = new ulong[(featureCount + 63) / 64];
        foreach (var feature in activeFeatures)
        {
            if ((uint)feature >= (uint)featureCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(activeFeatures),
                    feature,
                    $"Feature index must be between 0 and {featureCount - 1}.");
            }

            words[feature >> 6] |= 1UL << (feature & 63);
        }

        return new PatternBitVector(featureCount, words);
    }
}
