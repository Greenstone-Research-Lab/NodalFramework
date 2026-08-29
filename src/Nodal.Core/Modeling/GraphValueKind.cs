namespace Nodal.Core.Modeling;

/// <summary>Defines portable value shapes preserved by model discovery and graph execution.</summary>
public enum GraphValueKind
{
    /// <summary>An explicit null value.</summary>
    Null,
    /// <summary>A Unicode string.</summary>
    Text,
    /// <summary>A single Unicode character.</summary>
    Character,
    /// <summary>A signed 64-bit integer.</summary>
    SignedInteger,
    /// <summary>An unsigned 64-bit integer.</summary>
    UnsignedInteger,
    /// <summary>A decimal number.</summary>
    DecimalNumber,
    /// <summary>An IEEE 754 floating-point number.</summary>
    FloatingPoint,
    /// <summary>A Boolean value.</summary>
    Boolean,
    /// <summary>A globally unique identifier.</summary>
    Identifier,
    /// <summary>A calendar date.</summary>
    Date,
    /// <summary>A time of day.</summary>
    Time,
    /// <summary>A date and time without a required offset.</summary>
    DateTime,
    /// <summary>A date and time with an offset.</summary>
    DateTimeOffset,
    /// <summary>A named categorical value.</summary>
    Categorical,
    /// <summary>A latitude and longitude pair.</summary>
    GeoPoint,
    /// <summary>A numeric vector.</summary>
    Vector,
    /// <summary>A bounded ordered collection.</summary>
    Collection,
    /// <summary>A bounded nested object.</summary>
    NestedObject,
}
