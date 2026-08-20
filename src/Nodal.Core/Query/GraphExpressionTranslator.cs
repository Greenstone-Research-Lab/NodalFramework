using System.Linq.Expressions;

namespace Nodal.Core.Query;

internal static class GraphExpressionTranslator
{
    public static string TranslateProperty<TNode, TProperty>(
        Expression<Func<TNode, TProperty>> expression,
        IReadOnlyDictionary<string, string>? propertyMappings)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var body = StripConvert(expression.Body);
        if (!TryGetNodeMember(body, expression.Parameters[0], out var member))
        {
            throw new NotSupportedException($"Expression '{expression}' must select a direct mapped property.");
        }

        return MapProperty(member, propertyMappings);
    }

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

        if (expression is UnaryExpression { NodeType: ExpressionType.Not } unary)
        {
            return new GraphNotPredicate(TranslateNode(
                unary.Operand, nodeParameter, parameters, parameterOffset, propertyMappings));
        }

        if (TryGetNodeMember(expression, nodeParameter, out var booleanMember) &&
            (booleanMember.Type == typeof(bool) || booleanMember.Type == typeof(bool?)))
        {
            return AddComparison(booleanMember, true, typeof(bool), GraphComparisonOperator.Equal,
                parameters, parameterOffset, propertyMappings);
        }

        if (expression is MethodCallExpression methodCall)
        {
            return TranslateMethodCall(methodCall, nodeParameter, parameters, parameterOffset, propertyMappings);
        }

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
        var graphPropertyName = MapProperty(member, propertyMappings);
        if (value is null && binary.NodeType is ExpressionType.Equal or ExpressionType.NotEqual)
        {
            return new GraphNullPredicate(graphPropertyName, binary.NodeType == ExpressionType.Equal);
        }

        return AddComparison(member, value, valueExpression.Type,
            ToComparisonOperator(binary.NodeType, reverse), parameters, parameterOffset, propertyMappings);
    }

    private static GraphPredicate TranslateMethodCall(
        MethodCallExpression call,
        ParameterExpression nodeParameter,
        ICollection<GraphQueryParameter> parameters,
        int parameterOffset,
        IReadOnlyDictionary<string, string>? propertyMappings)
    {
        if (call.Object is not null && TryGetNodeMember(call.Object, nodeParameter, out var stringMember) &&
            call.Method.DeclaringType == typeof(string) && call.Arguments.Count == 1)
        {
            var operation = call.Method.Name switch
            {
                nameof(string.StartsWith) => GraphStringOperator.StartsWith,
                nameof(string.Contains) => GraphStringOperator.Contains,
                nameof(string.EndsWith) => GraphStringOperator.EndsWith,
                _ => throw new NotSupportedException($"String method '{call.Method.Name}' is not supported."),
            };
            var value = Evaluate(call.Arguments[0], nodeParameter);
            var parameterName = AddParameter(value, call.Arguments[0].Type, parameters, parameterOffset);
            return new GraphStringPredicate(MapProperty(stringMember, propertyMappings), operation, parameterName);
        }

        Expression? collection = null;
        Expression? item = null;
        if (call.Method.Name == nameof(Enumerable.Contains))
        {
            if (call.Object is not null && call.Arguments.Count == 1)
            {
                collection = call.Object;
                item = call.Arguments[0];
            }
            else if (call.Arguments.Count == 2)
            {
                collection = call.Arguments[0];
                item = call.Arguments[1];
            }
        }

        if (collection is not null && item is not null && TryGetNodeMember(item, nodeParameter, out var member))
        {
            while (collection.Type.IsByRefLike)
            {
                collection = collection switch
                {
                    UnaryExpression conversion => conversion.Operand,
                    MethodCallExpression { Arguments.Count: 1 } conversionCall => conversionCall.Arguments[0],
                    _ => throw new NotSupportedException(
                        $"Collection expression '{collection}' uses a by-ref-like value that cannot be parameterized."),
                };
            }
            var value = Evaluate(collection, nodeParameter);
            var parameterName = AddParameter(value, collection.Type, parameters, parameterOffset);
            return new GraphInPredicate(MapProperty(member, propertyMappings), parameterName);
        }

        throw new NotSupportedException($"Method call '{call}' is not a supported graph predicate.");
    }

    private static GraphComparisonPredicate AddComparison(
        MemberExpression member,
        object? value,
        Type valueType,
        GraphComparisonOperator operation,
        ICollection<GraphQueryParameter> parameters,
        int parameterOffset,
        IReadOnlyDictionary<string, string>? propertyMappings)
    {
        var parameterName = AddParameter(value, valueType, parameters, parameterOffset);
        return new GraphComparisonPredicate(MapProperty(member, propertyMappings), operation, parameterName);
    }

    private static string AddParameter(object? value, Type type, ICollection<GraphQueryParameter> parameters, int offset)
    {
        var name = $"p{offset + parameters.Count}";
        parameters.Add(new GraphQueryParameter(name, value, type));
        return name;
    }

    private static string MapProperty(MemberExpression member, IReadOnlyDictionary<string, string>? mappings)
    {
        var clrName = member.Member.Name;
        return mappings is null ? clrName : mappings.TryGetValue(clrName, out var mappedName)
            ? mappedName
            : throw new NotSupportedException($"Property '{clrName}' is ignored or not mapped in the graph model.");
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
