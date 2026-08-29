using System.Globalization;

namespace Nodal.Analytics.Observations;

/// <summary>
/// Represents a stable, culture-independent graph identity at the analytics boundary.
/// </summary>
/// <remarks>
/// The kind is retained so that, for example, the string <c>"42"</c> remains distinct
/// from the integer <c>42</c>.
/// </remarks>
public sealed record GraphObservationKey
{
    private GraphObservationKey(string kind, string value)
    {
        Kind = kind;
        Value = value;
    }

    /// <summary>Gets the canonical identity kind.</summary>
    public string Kind { get; }

    /// <summary>Gets the invariant identity value.</summary>
    public string Value { get; }

    /// <summary>Creates a canonical key from a provider-normalized identity.</summary>
    /// <param name="identity">A string, GUID, or signed or unsigned integer identity.</param>
    /// <returns>A typed, culture-independent key.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="identity"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The identity type is not supported.</exception>
    public static GraphObservationKey From(object identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        return identity switch
        {
            string value when !string.IsNullOrWhiteSpace(value) => new("string", value),
            string => throw new ArgumentException("Graph string identity cannot be empty.", nameof(identity)),
            Guid value => new("guid", value.ToString("D", CultureInfo.InvariantCulture)),
            sbyte value => Integer(value),
            byte value => Integer(value),
            short value => Integer(value),
            ushort value => Integer(value),
            int value => Integer(value),
            uint value => Integer(value),
            long value => Integer(value),
            ulong value => Integer(value),
            _ => throw new ArgumentException(
                $"Graph identity type '{identity.GetType().FullName}' is not supported.",
                nameof(identity)),
        };
    }

    /// <inheritdoc />
    public override string ToString() => $"{Kind}:{Value}";

    private static GraphObservationKey Integer<T>(T value)
        where T : struct, IFormattable =>
        new("integer", value.ToString(null, CultureInfo.InvariantCulture));
}
