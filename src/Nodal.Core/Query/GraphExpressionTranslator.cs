using System.Linq.Expressions;

namespace Nodal.Core.Query;

internal static class GraphExpressionTranslator
{
    public static TranslationResult Translate<TNode>(
        Expression<Func<TNode, bool>> expression,
        int parameterOffset,
        IReadOnlyDictionary<string, string>? propertyMappings)
    {
        var parameters = new List<GraphQueryParameter>();
        var predicate = TranslateNode(
            expression.Body,
            expression.Parameters[0],
            parameters,
            parameterOffset,
            propertyMappings);
        return new TranslationResult(predicate, parameters);
    }

    private static GraphPredicate TranslateNode(
        Expression expression,
        ParameterExpression nodeParameter,
        ICollection<GraphQueryParameter> parameters,
        int parameterOffset,
        IReadOnlyDictionary<string, string>? propertyMappings)
    {
        expression = StripConvert(expression);

        if (expression is not BinaryExpression binary)
        {
            throw new NotSupportedException($"Expression '{expression}' is not a supported graph predicate.");
        }

        if (binary.NodeType is ExpressionType.AndAlso or ExpressionType.OrElse)
        {
            return new GraphLogicalPredicate(
                TranslateNode(binary.Left, nodeParameter, parameters, parameterOffset, propertyMappings),
                binary.NodeType == ExpressionType.AndAlso ? GraphLogicalOperator.And : GraphLogicalOperator.Or,
                TranslateNode(binary.Right, nodeParameter, parameters, parameterOffset, propertyMappings));
        }

        var (member, valueExpression, reverse) = GetComparisonParts(binary, nodeParameter);
        var value = Evaluate(valueExpression, nodeParameter);
        var parameterName = $"p{parameterOffset + parameters.Count}";
        parameters.Add(new GraphQueryParameter(parameterName, value, valueExpression.Type));

        var clrPropertyName = member.Member.Name;
        var graphPropertyName = propertyMappings is null
            ? clrPropertyName
            : propertyMappings.TryGetValue(clrPropertyName, out var mappedName)
                ? mappedName
                : throw new NotSupportedException(
                    $"Property '{clrPropertyName}' is ignored or not mapped in the graph model.");

        return new GraphComparisonPredicate(
            graphPropertyName,
            ToComparisonOperator(binary.NodeType, reverse),
            parameterName);
    }

    private static (MemberExpression Member, Expression Value, bool Reverse) GetComparisonParts(
        BinaryExpression expression,
        ParameterExpression nodeParameter)
    {
        if (TryGetNodeMember(expression.Left, nodeParameter, out var left))
        {
            return (left, expression.Right, false);
        }

        if (TryGetNodeMember(expression.Right, nodeParameter, out var right))
        {
            return (right, expression.Left, true);
        }

        throw new NotSupportedException($"Comparison '{expression}' must reference a direct node property.");
    }

    private static bool TryGetNodeMember(
        Expression expression,
        ParameterExpression nodeParameter,
        out MemberExpression member)
    {
        expression = StripConvert(expression);
        if (expression is MemberExpression candidate && StripConvert(candidate.Expression!) == nodeParameter)
        {
            member = candidate;
            return true;
        }

        member = null!;
        return false;
    }

    private static object? Evaluate(Expression expression, ParameterExpression nodeParameter)
    {
        if (ReferencesParameter(expression, nodeParameter))
        {
            throw new NotSupportedException($"Value expression '{expression}' cannot depend on the queried node.");
        }

        var converted = Expression.Convert(expression, typeof(object));
        return Expression.Lambda<Func<object?>>(converted).Compile().Invoke();
    }

    private static bool ReferencesParameter(Expression expression, ParameterExpression parameter)
    {
        var visitor = new ParameterReferenceVisitor(parameter);
        visitor.Visit(expression);
        return visitor.Found;
    }

    private static Expression StripConvert(Expression expression)
    {
        while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
        {
            expression = unary.Operand;
        }

        return expression;
    }

    private static GraphComparisonOperator ToComparisonOperator(ExpressionType nodeType, bool reverse)
    {
        var comparison = nodeType switch
        {
            ExpressionType.Equal => GraphComparisonOperator.Equal,
            ExpressionType.NotEqual => GraphComparisonOperator.NotEqual,
            ExpressionType.GreaterThan => GraphComparisonOperator.GreaterThan,
            ExpressionType.GreaterThanOrEqual => GraphComparisonOperator.GreaterThanOrEqual,
            ExpressionType.LessThan => GraphComparisonOperator.LessThan,
            ExpressionType.LessThanOrEqual => GraphComparisonOperator.LessThanOrEqual,
            _ => throw new NotSupportedException($"Comparison operator '{nodeType}' is not supported."),
        };

        if (!reverse)
        {
            return comparison;
        }

        return comparison switch
        {
            GraphComparisonOperator.GreaterThan => GraphComparisonOperator.LessThan,
            GraphComparisonOperator.GreaterThanOrEqual => GraphComparisonOperator.LessThanOrEqual,
            GraphComparisonOperator.LessThan => GraphComparisonOperator.GreaterThan,
            GraphComparisonOperator.LessThanOrEqual => GraphComparisonOperator.GreaterThanOrEqual,
            _ => comparison,
        };
    }

    private sealed class ParameterReferenceVisitor(ParameterExpression target) : ExpressionVisitor
    {
        public bool Found { get; private set; }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            Found |= node == target;
            return base.VisitParameter(node);
        }
    }

    internal sealed record TranslationResult(
        GraphPredicate Predicate,
        IReadOnlyList<GraphQueryParameter> Parameters);
}
