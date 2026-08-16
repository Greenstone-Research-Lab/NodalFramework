using Nodal.Core.Migrations;

namespace Nodal.Migrations.Tests;

public sealed class MigrationPlannerTests
{
    [Fact]
    public void PlanUpPassesProviderNeutralOperationsToDialect()
    {
        var dialect = new RecordingDialect();
        var planner = new MigrationPlanner(dialect);

        var commands = planner.PlanUp(new InitialGraph());

        Assert.Collection(
            dialect.Operations,
            operation => Assert.IsType<CreateNodeTypeOperation>(operation),
            operation => Assert.IsType<CreateUniqueConstraintOperation>(operation),
            operation => Assert.IsType<CreateRelationTypeOperation>(operation));
        Assert.Single(commands);
    }

    private sealed class InitialGraph : NodalMigration
    {
        protected override void Up(MigrationBuilder migration)
        {
            migration
                .CreateNode<Person>()
                .CreateUniqueConstraint<Person, string>(person => person.Email)
                .CreateRelation<Knows, Person, Person>();
        }

        protected override void Down(MigrationBuilder migration)
        {
        }
    }

    private sealed class RecordingDialect : IGraphMigrationDialect
    {
        public IReadOnlyList<MigrationOperation> Operations { get; private set; } = [];

        public IReadOnlyList<MigrationCommand> Compile(IReadOnlyList<MigrationOperation> operations)
        {
            Operations = operations;
            return [new MigrationCommand("provider command", false)];
        }
    }

    private sealed record Person(string Id, string Email);

    private sealed record Knows(DateTime Since);
}
