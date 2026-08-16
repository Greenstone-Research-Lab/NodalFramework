namespace Nodal.Core.Model;

/// <summary>
/// Represents a first-class, strongly typed relationship between two graph nodes.
/// </summary>
/// <typeparam name="TSource">The source node type.</typeparam>
/// <typeparam name="TRelation">The relationship properties type.</typeparam>
/// <typeparam name="TTarget">The target node type.</typeparam>
/// <param name="Source">A reference to the source node.</param>
/// <param name="Properties">The relationship properties.</param>
/// <param name="Target">A reference to the target node.</param>
/// <example>
/// <code>
/// var knows = new GraphRelation&lt;Person, Knows, Person&gt;(
///     new GraphRef&lt;Person&gt;("alice"),
///     new Knows { Since = 2024 },
///     new GraphRef&lt;Person&gt;("bob"));
/// </code>
/// </example>
public sealed record GraphRelation<TSource, TRelation, TTarget>(
    GraphRef<TSource> Source,
    TRelation Properties,
    GraphRef<TTarget> Target)
    where TRelation : notnull;
