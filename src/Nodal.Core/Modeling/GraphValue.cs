using System.Collections.ObjectModel;

namespace Nodal.Core.Modeling;

/// <summary>Represents a geographic coordinate without introducing a provider spatial type.</summary>
public sealed record GraphGeoPoint
{
    /// <summary>Initializes and validates a geographic coordinate.</summary>
    /// <param name="latitude">Latitude in decimal degrees.</param>
    /// <param name="longitude">Longitude in decimal degrees.</param>
    public GraphGeoPoint(double latitude, double longitude)
    {
        if (latitude is < -90 or > 90)
        {
            throw new ArgumentOutOfRangeException(nameof(latitude));
        }

        if (longitude is < -180 or > 180)
        {
            throw new ArgumentOutOfRangeException(nameof(longitude));
        }

        Latitude = latitude;
        Longitude = longitude;
    }

    /// <summary>Gets latitude in decimal degrees.</summary>
    public double Latitude { get; }

    /// <summary>Gets longitude in decimal degrees.</summary>
    public double Longitude { get; }
}

/// <summary>Preserves one typed provider-neutral graph value.</summary>
/// <param name="Kind">The portable value kind.</param>
/// <param name="Value">The immutable value representation.</param>
public sealed record GraphValue(GraphValueKind Kind, object? Value)
{
    /// <summary>Creates a canonical graph value from a supported CLR value.</summary>
    /// <param name="value">The source value.</param>
    /// <returns>An immutable typed graph value.</returns>
    /// <exception cref="NotSupportedException">The CLR value shape is not portable.</exception>
    public static GraphValue From(object? value) => value switch
    {
        null => new(GraphValueKind.Null, null),
        string item => new(GraphValueKind.Text, item),
        char item => new(GraphValueKind.Character, item),
        sbyte or short or int or long => new(GraphValueKind.SignedInteger, Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture)),
        byte or ushort or uint or ulong => new(GraphValueKind.UnsignedInteger, Convert.ToUInt64(value, System.Globalization.CultureInfo.InvariantCulture)),
        decimal item => new(GraphValueKind.DecimalNumber, item),
        float item => new(GraphValueKind.FloatingPoint, (double)item),
        double item => new(GraphValueKind.FloatingPoint, item),
        bool item => new(GraphValueKind.Boolean, item),
        Guid item => new(GraphValueKind.Identifier, item),
        DateOnly item => new(GraphValueKind.Date, item),
        TimeOnly item => new(GraphValueKind.Time, item),
        DateTime item => new(GraphValueKind.DateTime, item),
        DateTimeOffset item => new(GraphValueKind.DateTimeOffset, item),
        Enum item => new(GraphValueKind.Categorical, item.ToString()),
        GraphGeoPoint item => new(GraphValueKind.GeoPoint, item),
        IReadOnlyDictionary<string, GraphValue> item => new(GraphValueKind.NestedObject, FreezeObject(item)),
        IEnumerable<GraphValue> item => new(GraphValueKind.Collection, Array.AsReadOnly(item.ToArray())),
        _ => throw new NotSupportedException($"CLR value type '{value.GetType().FullName}' is not a portable graph value."),
    };

    /// <summary>Creates a numeric vector by defensively copying its elements.</summary>
    /// <param name="values">The numeric vector.</param>
    /// <returns>An immutable vector graph value.</returns>
    public static GraphValue Vector(IEnumerable<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new GraphValue(GraphValueKind.Vector, Array.AsReadOnly(values.ToArray()));
    }

    private static ReadOnlyDictionary<string, GraphValue> FreezeObject(
        IReadOnlyDictionary<string, GraphValue> value)
    {
        var copy = value.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        return new ReadOnlyDictionary<string, GraphValue>(copy);
    }
}
