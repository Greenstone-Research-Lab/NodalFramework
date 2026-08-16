namespace Nodal.Core.Model;

/// <summary>
/// Identifies a node without loading the complete node into memory.
/// </summary>
/// <typeparam name="TNode">The CLR type representing the referenced node.</typeparam>
/// <example>
/// <code>
/// var person = new GraphRef&lt;Person&gt;("person-42");
/// </code>
/// </example>
public readonly record struct GraphRef<TNode>
{
    /// <summary>
    /// Initializes a reference with a provider-compatible identifier.
    /// </summary>
    /// <param name="value">The provider-independent identifier value.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public GraphRef(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    /// <summary>
    /// Gets the identifier value supplied by the domain model.
    /// </summary>
    public object Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value.ToString() ?? string.Empty;
}
