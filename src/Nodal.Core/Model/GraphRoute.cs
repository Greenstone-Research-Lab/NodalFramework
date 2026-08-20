namespace Nodal.Core.Model;

/// <summary>Represents an ordered, strongly typed route returned by a graph path-finding algorithm.</summary>
/// <typeparam name="TNode">The route node type.</typeparam>
/// <typeparam name="TRelation">The relationship payload connecting adjacent nodes.</typeparam>
/// <param name="Nodes">The ordered nodes from source to target.</param>
/// <param name="Relations">The ordered relationships connecting adjacent nodes.</param>
/// <param name="TotalCost">The optional weighted path cost.</param>
public sealed record GraphRoute<TNode, TRelation>(
    IReadOnlyList<TNode> Nodes,
    IReadOnlyList<TRelation> Relations,
    double? TotalCost = null)
    where TRelation : notnull
{
    /// <summary>Gets the number of traversed relationships.</summary>
    public int HopCount => Relations.Count;
}
