using Nodal.Core.Execution;
using Nodal.Core.Metadata;
using Nodal.Core.Providers;
using Nodal.Core.Query;

namespace Nodal.Core.Tests;

public sealed class PocoAttributeTests
{
    [Fact]
    public void ContextDiscoversNodeAndRelationAttributesFromSetProperties()
    {
        var context = new AttributedContext(new UnusedProvider());

        var node = context.Model.GetNode<AttributedPerson>();
        var relation = context.Model.GetRelation<AttributedPerson, Knows, AttributedPerson>();

        Assert.Equal("people", node.Name);
        Assert.Equal(nameof(AttributedPerson.Id), node.KeyProperty);
        Assert.Equal("display_name", node.Properties[nameof(AttributedPerson.Name)].Name);
        Assert.DoesNotContain(nameof(AttributedPerson.RuntimeLabel), node.Properties.Keys);
        Assert.Equal("KNOWS", relation.Name);
        Assert.False(relation.Directed);
        Assert.Equal("since_at", relation.Properties[nameof(Knows.Since)].Name);
        Assert.DoesNotContain(nameof(Knows.TransientScore), relation.Properties.Keys);
    }

    [Fact]
    public void QueryUsesGraphPropertyNameDiscoveredFromAttribute()
    {
        var context = new AttributedContext(new UnusedProvider());

        var model = context.People.Match(person => person.Name == "Ada").ToQueryModel();

        var comparison = Assert.IsType<GraphComparisonPredicate>(model.Predicate);
        Assert.Equal("display_name", comparison.PropertyName);
    }

    [Fact]
    public void FluentRelationConfigurationOverridesAttributes()
    {
        var context = new FluentRelationContext(new UnusedProvider());

        var relation = context.Model.GetRelation<AttributedPerson, Knows, AttributedPerson>();

        Assert.Equal("COLLABORATES_WITH", relation.Name);
        Assert.True(relation.Directed);
    }

    private sealed class AttributedContext(IGraphProvider provider) : NodalContext(provider)
    {
        public GraphSet<AttributedPerson> People => Set<AttributedPerson>();

        public RelationSet<AttributedPerson, Knows, AttributedPerson> Knows =>
            Relations<AttributedPerson, Knows, AttributedPerson>();
    }

    private sealed class FluentRelationContext(IGraphProvider provider) : NodalContext(provider)
    {
        public RelationSet<AttributedPerson, Knows, AttributedPerson> Relations =>
            base.Relations<AttributedPerson, Knows, AttributedPerson>();

        protected override void OnModelCreating(NodalModelBuilder modelBuilder)
        {
            modelBuilder.Relation<AttributedPerson, Knows, AttributedPerson>()
                .HasName("COLLABORATES_WITH")
                .IsDirected();
        }
    }

    [GraphNode("people")]
    private sealed record AttributedPerson(
        [property: GraphKey]
        [property: GraphProperty("person_id")]
        string Id,
        [property: GraphProperty("display_name")]
        string Name)
    {
        [GraphIgnore]
        public string RuntimeLabel => $"{Id}:{Name}";
    }

    [GraphRelation("KNOWS", Directed = false)]
    private sealed record Knows(
        [property: GraphProperty("since_at")]
        DateTime Since)
    {
        [GraphIgnore]
        public double TransientScore => Since.Year;
    }

    private sealed class UnusedProvider : IGraphProvider
    {
        public IGraphQueryCompiler QueryCompiler => throw new NotSupportedException();

        public IGraphCommandExecutor CommandExecutor => throw new NotSupportedException();

        public IGraphResultMaterializer ResultMaterializer => throw new NotSupportedException();
    }
}
