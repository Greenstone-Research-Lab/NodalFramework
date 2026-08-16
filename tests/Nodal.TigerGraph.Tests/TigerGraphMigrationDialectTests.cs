using Nodal.Core.Migrations;

namespace Nodal.TigerGraph.Tests;

public sealed class TigerGraphMigrationDialectTests
{
    [Fact]
    public void CompileProducesDeterministicSchemaChangeJobWithTypedAttributes()
    {
        MigrationOperation[] operations =
        [
            new CreateNodeTypeOperation(
                "Person",
                "person_id",
                typeof(string),
                [new GraphSchemaProperty("person_id", typeof(string)), new GraphSchemaProperty("age", typeof(int))]),
            new CreateRelationTypeOperation(
                "KNOWS",
                "Person",
                "Person",
                true,
                [new GraphSchemaProperty("since_at", typeof(DateTime))]),
            new CreateIndexOperation("Person", "age"),
        ];
        var dialect = new TigerGraphMigrationDialect("SocialGraph");

        var first = dialect.Compile(operations);
        var second = dialect.Compile(operations);

        Assert.Equal(first, second);
        Assert.Collection(
            first,
            command =>
            {
                Assert.Contains("ADD VERTEX Person (PRIMARY_ID person_id STRING, age INT)", command.Text, StringComparison.Ordinal);
                Assert.Contains("ADD DIRECTED EDGE KNOWS (FROM Person, TO Person, since_at DATETIME)", command.Text, StringComparison.Ordinal);
                Assert.Contains("ALTER VERTEX Person ADD INDEX nodal_ix_Person_age ON (age)", command.Text, StringComparison.Ordinal);
            },
            command => Assert.StartsWith("RUN SCHEMA_CHANGE JOB nodal_", command.Text, StringComparison.Ordinal),
            command => Assert.StartsWith("DROP JOB nodal_", command.Text, StringComparison.Ordinal));
        Assert.All(first, command => Assert.False(command.IsTransactional));
    }

    [Fact]
    public void CompileHandlesEmptyAndDropOperations()
    {
        var dialect = new TigerGraphMigrationDialect("SocialGraph");

        Assert.Empty(dialect.Compile([]));
        var commands = dialect.Compile(
            [new DropRelationTypeOperation("KNOWS"), new DropNodeTypeOperation("Person")]);

        Assert.Contains("DROP EDGE KNOWS; DROP VERTEX Person;", commands[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void UnsupportedConstraintTypeAndUnsafeNamesAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new TigerGraphMigrationDialect("bad;graph"));
        var dialect = new TigerGraphMigrationDialect("SocialGraph");
        Assert.Throws<NotSupportedException>(() =>
            dialect.Compile([new CreateUniqueConstraintOperation("Person", "email")]));
        Assert.Throws<NotSupportedException>(() =>
            dialect.Compile([new DropSchemaObjectOperation("old_index", MigrationSchemaObjectKind.Index)]));
        Assert.Throws<ArgumentException>(() =>
            dialect.Compile([new DropNodeTypeOperation("bad-name")]));
    }
}
