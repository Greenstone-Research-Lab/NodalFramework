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

/// <summary>
/// Defines provider-neutral boolean operations.
/// </summary>
public enum GraphLogicalOperator
{
    And,
    Or,
}
