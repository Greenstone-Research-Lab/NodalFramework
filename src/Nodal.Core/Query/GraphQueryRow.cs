using System.Globalization;

namespace Nodal.Core.Query;

/// <summary>
/// Represents one named provider-side row projection returned by a graph query.
/// </summary>
public sealed class GraphQueryRow
{
    private readonly IReadOnlyDictionary<string, object?> values;

    internal GraphQueryRow(IReadOnlyDictionary<string, object?> values)
    {
        this.values = values;
    }

    /// <summary>Gets every value in the projected row.</summary>
    public IReadOnlyDictionary<string, object?> Values => values;

    /// <summary>
    /// Gets a named projected value and converts it to the requested CLR type.
    /// </summary>
    /// <typeparam name="TValue">The requested CLR value type.</typeparam>
    /// <param name="name">The result-column name specified in the row projection.</param>
    /// <returns>The converted projected value.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the provider did not return <paramref name="name"/>.</exception>
    /// <example>
    /// <code>var orderCount = row.Get&lt;long&gt;("orderCount");</code>
    /// </example>
    public TValue? Get<TValue>(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!values.TryGetValue(name, out var value))
        {
            throw new KeyNotFoundException($"The projected row does not contain a '{name}' column.");
        }

        if (value is null)
        {
            return default;
        }
        if (value is TValue typed)
        {
            return typed;
        }

        var targetType = Nullable.GetUnderlyingType(typeof(TValue)) ?? typeof(TValue);
        if (targetType.IsEnum)
        {
            return (TValue)Enum.ToObject(targetType, value);
        }
        return (TValue)Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
    }
}
