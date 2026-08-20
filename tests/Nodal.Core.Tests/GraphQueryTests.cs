using Nodal.Core.Query;

namespace Nodal.Core.Tests;

public sealed class GraphQueryTests
{
    [Fact]
    public void MatchTranslatesCapturedValuesIntoParameters()
    {
        const string personId = "person-42";
        var query = new GraphSet<Person>().Match(person => person.Id == personId && person.Age >= 18);

        var model = query.ToQueryModel();

        var logical = Assert.IsType<GraphLogicalPredicate>(model.Predicate);
        Assert.Equal(GraphLogicalOperator.And, logical.Operator);
        Assert.Collection(
            model.Parameters,
            parameter => Assert.Equal(("p0", personId), (parameter.Name, parameter.Value)),
            parameter => Assert.Equal(("p1", 18), (parameter.Name, parameter.Value)));
    }

    [Fact]
    public void WhereUsesUniqueParameterNamesAcrossMultipleCalls()
    {
        var query = new GraphSet<Person>()
            .Query()
            .Where(person => person.Age >= 18)
            .Where(person => person.Score > 4.5);

        var model = query.ToQueryModel();

        Assert.Equal(["p0", "p1"], model.Parameters.Select(parameter => parameter.Name));
    }

    [Fact]
    public void TakeRejectsNonPositiveLimits()
    {
        var query = new GraphSet<Person>().Query();

        Assert.Throws<ArgumentOutOfRangeException>(() => query.Take(0));
    }

    [Fact]
    public void OrElseAndReversedComparisonsAreTranslatedCorrectly()
    {
        var query = new GraphSet<Person>().Match(person =>
            18 < person.Age ||
            10 >= person.Score);

        var logical = Assert.IsType<GraphLogicalPredicate>(query.ToQueryModel().Predicate);
        Assert.Equal(GraphLogicalOperator.Or, logical.Operator);
        Assert.Equal(
            GraphComparisonOperator.GreaterThan,
            Assert.IsType<GraphComparisonPredicate>(logical.Left).Operator);
        Assert.Equal(
            GraphComparisonOperator.LessThanOrEqual,
            Assert.IsType<GraphComparisonPredicate>(logical.Right).Operator);
    }

    [Theory]
    [InlineData(ComparisonKind.Equal, GraphComparisonOperator.Equal)]
    [InlineData(ComparisonKind.NotEqual, GraphComparisonOperator.NotEqual)]
    [InlineData(ComparisonKind.GreaterThanOrEqual, GraphComparisonOperator.GreaterThanOrEqual)]
    [InlineData(ComparisonKind.LessThan, GraphComparisonOperator.LessThan)]
    [InlineData(ComparisonKind.LessThanOrEqual, GraphComparisonOperator.LessThanOrEqual)]
    public void ComparisonOperatorsAreTranslated(
        ComparisonKind kind,
        GraphComparisonOperator expected)
    {
        System.Linq.Expressions.Expression<Func<Person, bool>> expression = kind switch
        {
            ComparisonKind.Equal => person => person.Age == 18,
            ComparisonKind.NotEqual => person => person.Age != 18,
            ComparisonKind.GreaterThanOrEqual => person => person.Age >= 18,
            ComparisonKind.LessThan => person => person.Age < 18,
            ComparisonKind.LessThanOrEqual => person => person.Age <= 18,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        var comparison = Assert.IsType<GraphComparisonPredicate>(
            new GraphSet<Person>().Match(expression).ToQueryModel().Predicate);

        Assert.Equal(expected, comparison.Operator);
    }

    [Fact]
    public void BooleanPropertyIsTranslatedWithoutExplicitComparison()
    {
        var predicate = Assert.IsType<GraphComparisonPredicate>(
            new GraphSet<Person>().Match(person => person.Active).ToQueryModel().Predicate);

        Assert.Equal("Active", predicate.PropertyName);
        Assert.Equal(true, Assert.Single(new GraphSet<Person>().Match(person => person.Active)
            .ToQueryModel().Parameters).Value);
    }

    [Fact]
    public void StringCollectionNullAndNotPredicatesAreTranslated()
    {
        string[] names = ["Ada", "Alan"];
        var text = new GraphSet<Person>().Match(person => person.Name.StartsWith("Ad") &&
            person.Name.Contains("da") && person.Name.EndsWith("la")).ToQueryModel();
        var membership = new GraphSet<Person>().Match(person => names.Contains(person.Name)).ToQueryModel();
        var nullCheck = new GraphSet<Person>().Match(person => person.Name == null!).ToQueryModel();
        var negated = new GraphSet<Person>().Match(person => !person.Active).ToQueryModel();

        Assert.Equal(3, text.Parameters.Count);
        Assert.IsType<GraphLogicalPredicate>(text.Predicate);
        Assert.IsType<GraphInPredicate>(membership.Predicate);
        Assert.IsType<GraphNullPredicate>(nullCheck.Predicate);
        Assert.IsType<GraphNotPredicate>(negated.Predicate);
    }

    [Fact]
    public void OrderingPagingDistinctAndTrackingAreRepresentedInModel()
    {
        var model = new GraphSet<Person>().Query()
            .OrderBy(person => person.Name)
            .ThenByDescending(person => person.Age)
            .Skip(10)
            .Take(5)
            .Distinct()
            .AsNoTracking()
            .ToQueryModel();

        Assert.Equal(10, model.Offset);
        Assert.Equal(5, model.Limit);
        Assert.True(model.Distinct);
        Assert.Equal(GraphTrackingBehavior.NoTracking, model.TrackingBehavior);
        Assert.Collection(model.EffectiveOrderings,
            ordering => Assert.Equal(("Name", GraphSortDirection.Ascending), (ordering.PropertyName, ordering.Direction)),
            ordering => Assert.Equal(("Age", GraphSortDirection.Descending), (ordering.PropertyName, ordering.Direction)));
    }

    [Fact]
    public void PagingAndOrderingValidateArguments()
    {
        var query = new GraphSet<Person>().Query();

        Assert.Throws<ArgumentOutOfRangeException>(() => query.Skip(-1));
        Assert.Throws<InvalidOperationException>(() => query.ThenBy(person => person.Name));
        Assert.Throws<NotSupportedException>(() => query.OrderBy(person => person.Name.Length));
    }

    [Fact]
    public void ComparisonMustUseDirectNodeProperty()
    {
        Assert.Throws<NotSupportedException>(
            () => new GraphSet<Person>().Match(person => person.Name.Length > 2));
    }

    [Fact]
    public void ComparisonValueCannotDependOnQueriedNode()
    {
        Assert.Throws<NotSupportedException>(
            () => new GraphSet<Person>().Match(person => person.Age > person.Score));
    }

    [Fact]
    public async Task StandaloneQueryCannotExecute()
    {
        var query = new GraphSet<Person>().Query();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await query.ToListAsync());

        Assert.Contains("not attached", exception.Message, StringComparison.Ordinal);
    }

    public enum ComparisonKind
    {
        Equal,
        NotEqual,
        GreaterThanOrEqual,
        LessThan,
        LessThanOrEqual,
    }

    private sealed record Person(string Id, int Age, double Score, string Name = "", bool Active = true);
}
