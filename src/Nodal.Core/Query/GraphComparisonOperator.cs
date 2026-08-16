namespace Nodal.Core.Query;

/// <summary>
/// Defines the comparison operations supported by the provider-neutral query model.
/// </summary>
public enum GraphComparisonOperator
{
    /// <summary>Tests whether two values are equal.</summary>
    Equal,

    /// <summary>Tests whether two values are not equal.</summary>
    NotEqual,

    /// <summary>Tests whether the left value is greater than the right value.</summary>
    GreaterThan,

    /// <summary>Tests whether the left value is greater than or equal to the right value.</summary>
    GreaterThanOrEqual,

    /// <summary>Tests whether the left value is less than the right value.</summary>
    LessThan,

    /// <summary>Tests whether the left value is less than or equal to the right value.</summary>
    LessThanOrEqual,
}
