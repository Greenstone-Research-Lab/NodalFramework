using Nodal.Core.Migrations;

namespace Nodal.Neo4j.Tests;

public sealed class Neo4jMigrationDialectTests
{
    [Fact]
    public void CompileProducesIdempotentEscapedConstraintAndIndexCommands()
    {
        MigrationOperation[] operations =
        [
            new CreateNodeTypeOperation("Person"),
            new CreateUniqueConstraintOperation("Per`son", "person_id"),
            new CreateIndexOperation("Person", "email"),
            new DropSchemaObjectOperation("old_index", MigrationSchemaObjectKind.Index),
        ];

        var commands = new Neo4jMigrationDialect().Compile(operations);

        Assert.Equal(3, commands.Count);
        Assert.Contains("CREATE CONSTRAINT", commands[0].Text, StringComparison.Ordinal);
        Assert.Contains("`Per``son`", commands[0].Text, StringComparison.Ordinal);
        Assert.Contains("IF NOT EXISTS", commands[1].Text, StringComparison.Ordinal);
        Assert.Equal("DROP INDEX `old_index` IF EXISTS", commands[2].Text);
        Assert.All(commands, command => Assert.True(command.IsTransactional));
    }

    [Fact]
    public void CompileRejectsUnknownOperationAndNullInput()
    {
        var dialect = new Neo4jMigrationDialect();

        Assert.Throws<ArgumentNullException>(() => dialect.Compile(null!));
        Assert.Throws<NotSupportedException>(() => dialect.Compile([new UnknownOperation()]));
    }

    private sealed record UnknownOperation : MigrationOperation;
}
