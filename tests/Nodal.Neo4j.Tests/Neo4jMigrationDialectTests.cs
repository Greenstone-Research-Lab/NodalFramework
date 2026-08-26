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
        Assert.All(commands, command => Assert.False(command.IsTransactional));
    }

    [Fact]
    public void CompileRejectsUnknownOperationAndNullInput()
    {
        var dialect = new Neo4jMigrationDialect();

        Assert.Throws<ArgumentNullException>(() => dialect.Compile(null!));
        Assert.Throws<NotSupportedException>(() => dialect.Compile([new UnknownOperation()]));
    }

    [Fact]
    public void CompileHandlesTypedIndexRemovalAndFlexibleProperties()
    {
        var commands = new Neo4jMigrationDialect().Compile(
        [
            new DropIndexOperation("Person", "email"),
            new DropUniqueConstraintOperation("Person", "person_id"),
            new AddNodePropertyOperation(
                "Person",
                new GraphSchemaProperty("display_name", typeof(string))),
            new RenameNodePropertyOperation("Person", "display_name", "name"),
        ]);

        Assert.Equal(2, commands.Count);
        Assert.Contains("DROP INDEX", commands[0].Text, StringComparison.Ordinal);
        Assert.Contains("nodal_ix_Person_email", commands[0].Text, StringComparison.Ordinal);
        Assert.Contains("DROP CONSTRAINT", commands[1].Text, StringComparison.Ordinal);
        Assert.Contains("nodal_uq_Person_person_id", commands[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileTreatsRelationPropertiesAsFlexibleAndRejectsTypeAlterations()
    {
        var commands = new Neo4jMigrationDialect().Compile(
        [
            new CreateRelationTypeOperation("KNOWS", "Person", "Person", true),
            new AddRelationPropertyOperation(
                "KNOWS",
                new GraphSchemaProperty("since", typeof(DateTime))),
            new DropRelationPropertyOperation("KNOWS", "since"),
            new RenameRelationPropertyOperation("KNOWS", "since", "connected_at"),
        ]);

        Assert.Empty(commands);

        Assert.Throws<NotSupportedException>(() => new Neo4jMigrationDialect().Compile(
        [
            new AlterNodePropertyTypeOperation(
                "Person", "age", typeof(int), typeof(long),
                MigrationPropertyTypeCompatibility.RequiresRewrite),
        ]));

        Assert.Throws<NotSupportedException>(() => new Neo4jMigrationDialect().Compile(
        [
            new AlterRelationPropertyTypeOperation(
                "KNOWS", "weight", typeof(int), typeof(double),
                MigrationPropertyTypeCompatibility.Destructive),
        ]));
    }

    [Fact]
    public void CommunityDialectRejectsEnterprisePropertyConstraintsDuringPreflight()
    {
        var dialect = new Neo4jMigrationDialect();

        var exception = Assert.Throws<NotSupportedException>(() => dialect.Compile(
        [
            new CreatePropertyExistenceConstraintOperation(
                GraphSchemaEntityKind.Node, "Person", "email"),
        ]));

        Assert.Contains("Enterprise Edition", exception.Message, StringComparison.Ordinal);
        Assert.Throws<NotSupportedException>(() => dialect.Compile(
        [
            new DropPropertyTypeConstraintOperation(
                GraphSchemaEntityKind.Relation, "KNOWS", "since", typeof(DateTime)),
        ]));
    }

    [Fact]
    public void EnterpriseDialectCompilesEscapedNodeAndRelationPropertyConstraints()
    {
        var commands = new Neo4jMigrationDialect(enterpriseSchemaConstraintsEnabled: true).Compile(
        [
            new CreatePropertyExistenceConstraintOperation(
                GraphSchemaEntityKind.Node, "Per`son", "e`mail"),
            new CreatePropertyTypeConstraintOperation(
                GraphSchemaEntityKind.Relation, "KNOWS", "since", typeof(DateTimeOffset)),
            new DropPropertyExistenceConstraintOperation(
                GraphSchemaEntityKind.Node, "Per`son", "e`mail"),
            new DropPropertyTypeConstraintOperation(
                GraphSchemaEntityKind.Relation, "KNOWS", "since", typeof(DateTimeOffset)),
        ]);

        Assert.Equal(4, commands.Count);
        Assert.Contains("FOR (`node`:`Per``son`)", commands[0].Text, StringComparison.Ordinal);
        Assert.Contains("`node`.`e``mail` IS NOT NULL", commands[0].Text, StringComparison.Ordinal);
        Assert.Contains("FOR ()-[`relation`:`KNOWS`]-()", commands[1].Text, StringComparison.Ordinal);
        Assert.Contains("IS :: ZONED DATETIME", commands[1].Text, StringComparison.Ordinal);
        Assert.StartsWith("DROP CONSTRAINT", commands[2].Text, StringComparison.Ordinal);
        Assert.StartsWith("DROP CONSTRAINT", commands[3].Text, StringComparison.Ordinal);
        Assert.All(commands, command => Assert.False(command.IsTransactional));
    }

    [Theory]
    [InlineData(typeof(bool), "BOOLEAN")]
    [InlineData(typeof(int), "INTEGER")]
    [InlineData(typeof(double), "FLOAT")]
    [InlineData(typeof(string), "STRING")]
    [InlineData(typeof(DateOnly), "DATE")]
    [InlineData(typeof(TimeOnly), "LOCAL TIME")]
    [InlineData(typeof(TimeSpan), "DURATION")]
    public void EnterpriseDialectMapsSupportedClrTypes(Type clrType, string storageType)
    {
        var command = Assert.Single(new Neo4jMigrationDialect(true).Compile(
        [
            new CreatePropertyTypeConstraintOperation(
                GraphSchemaEntityKind.Node, "Person", "value", clrType),
        ]));

        Assert.EndsWith($"IS :: {storageType}", command.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void EnterpriseDialectRejectsUnsupportedClrTypeAndEntityKind()
    {
        var dialect = new Neo4jMigrationDialect(true);

        Assert.Throws<NotSupportedException>(() => dialect.Compile(
        [
            new CreatePropertyTypeConstraintOperation(
                GraphSchemaEntityKind.Node, "Person", "payload", typeof(object)),
        ]));
        Assert.Throws<ArgumentOutOfRangeException>(() => dialect.Compile(
        [
            new CreatePropertyExistenceConstraintOperation(
                (GraphSchemaEntityKind)99, "Person", "email"),
        ]));
    }

    private sealed record UnknownOperation : MigrationOperation;
}
