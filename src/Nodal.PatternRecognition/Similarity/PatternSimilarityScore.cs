namespace Nodal.PatternRecognition.Similarity;

/// <summary>
/// Contains explainable binary similarity measurements for two path feature vectors.
/// </summary>
/// <param name="DifferenceCount">The number of features present in exactly one vector.</param>
/// <param name="IntersectionCount">The number of features shared by both vectors.</param>
/// <param name="UnionCount">The number of features present in either vector.</param>
/// <param name="HammingDistance">The normalized XOR distance in the range zero to one.</param>
/// <param name="HammingSimilarity">One minus the normalized Hamming distance.</param>
/// <param name="JaccardSimilarity">Intersection divided by union.</param>
/// <param name="BinaryCosineSimilarity">Intersection normalized by both active-feature counts.</param>
public readonly record struct PatternSimilarityScore(
    int DifferenceCount,
    int IntersectionCount,
    int UnionCount,
    double HammingDistance,
    double HammingSimilarity,
    double JaccardSimilarity,
    double BinaryCosineSimilarity);
