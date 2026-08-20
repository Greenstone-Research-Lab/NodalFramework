namespace Nodal.Core.Query;

/// <summary>
/// Serves as the base type for provider-neutral graph predicates.
/// </summary>
public abstract record GraphPredicate;

/// <summary>
/// Compares a node property with a parameterized value.
/// </summary>
/// <param name="PropertyName">The domain property name.</param>
/// <param name="Operator">The comparison operator.</param>
/// <param name="ParameterName">The generated parameter name.</param>
public sealed record GraphComparisonPredicate(
    string PropertyName,
    GraphComparisonOperator Operator,
    string ParameterName) : GraphPredicate;

/// <summary>
/// Combines two predicates with a boolean operation.
/// </summary>
/// <param name="Left">The left predicate.</param>
/// <param name="Operator">The boolean operator.</param>
/// <param name="Right">The right predicate.</param>
public sealed record GraphLogicalPredicate(
    GraphPredicate Left,
    GraphLogicalOperator Operator,
    GraphPredicate Right) : GraphPredicate;

/// <summary>Negates a provider-neutral predicate.</summary>
public sealed record GraphNotPredicate(GraphPredicate Operand) : GraphPredicate;

/// <summary>Checks whether a property is null or non-null.</summary>
public sealed record GraphNullPredicate(string PropertyName, bool IsNull) : GraphPredicate;

/// <summary>Applies a string matching operation to a property.</summary>
public sealed record GraphStringPredicate(
    string PropertyName,
    GraphStringOperator Operator,
    string ParameterName) : GraphPredicate;

/// <summary>Checks whether a property occurs in a parameterized collection.</summary>
public sealed record GraphInPredicate(string PropertyName, string ParameterName, bool Negated = false) : GraphPredicate;

/// <summary>Defines provider-neutral string matching operations.</summary>
public enum GraphStringOperator
{
    /// <summary>Matches a prefix.</summary>
    StartsWith,
    /// <summary>Matches a substring.</summary>
    Contains,
    /// <summary>Matches a suffix.</summary>
    EndsWith,
}

/// <summary>
/// Defines provider-neutral boolean operations.
/// </summary>
public enum GraphLogicalOperator
{
    /// <summary>Requires both operands to evaluate to true.</summary>
    And,

    /// <summary>Requires at least one operand to evaluate to true.</summary>
    Or,
}
