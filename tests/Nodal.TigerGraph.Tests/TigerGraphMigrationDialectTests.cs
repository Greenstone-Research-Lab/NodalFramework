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
    public void CompileHandlesPropertyEvolutionAndRejectsUnsafeRewrite()
    {
        var dialect = new TigerGraphMigrationDialect("SocialGraph");
        var commands = dialect.Compile(
        [
            new AddNodePropertyOperation(
                "Person",
                new GraphSchemaProperty("display_name", typeof(string))),
            new AddRelationPropertyOperation(
                "KNOWS",
                new GraphSchemaProperty("since", typeof(DateTime))),
            new DropNodePropertyOperation("Person", "display_name"),
            new DropRelationPropertyOperation("KNOWS", "since"),
        ]);

        Assert.Contains("ALTER VERTEX Person ADD ATTRIBUTE (display_name STRING)", commands[0].Text, StringComparison.Ordinal);
        Assert.Contains("ALTER EDGE KNOWS ADD ATTRIBUTE (since DATETIME)", commands[0].Text, StringComparison.Ordinal);
        Assert.Contains("ALTER VERTEX Person DROP ATTRIBUTE display_name", commands[0].Text, StringComparison.Ordinal);
        Assert.Contains("ALTER EDGE KNOWS DROP ATTRIBUTE since", commands[0].Text, StringComparison.Ordinal);
        Assert.Throws<NotSupportedException>(() =>
            dialect.Compile(
            [
                new AlterNodePropertyTypeOperation(
                    "Person",
                    "age",
                    typeof(int),
                    typeof(string),
                    MigrationPropertyTypeCompatibility.RequiresRewrite)
            ]));
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

    [Fact]
    public void CompileMapsEnumNumericBooleanAndFloatingPointSchemaTypes()
    {
        var dialect = new TigerGraphMigrationDialect("SocialGraph");

        var commands = dialect.Compile(
        [
            new CreateNodeTypeOperation(
                "Metric",
                "level",
                typeof(MetricLevel),
                [
                    new GraphSchemaProperty("level", typeof(MetricLevel)),
                    new GraphSchemaProperty("enabled", typeof(bool)),
                    new GraphSchemaProperty("score", typeof(double)),
                ]),
            new CreateRelationTypeOperation("LINKS", "Metric", "Metric", false),
        ]);

        Assert.Contains("PRIMARY_ID level INT, enabled BOOL, score DOUBLE", commands[0].Text, StringComparison.Ordinal);
        Assert.Contains("ADD UNDIRECTED EDGE LINKS", commands[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileRejectsUnmappablePrimaryAttributeAndOperationTypes()
    {
        var dialect = new TigerGraphMigrationDialect("SocialGraph");

        Assert.Throws<NotSupportedException>(() => dialect.Compile(
            [new CreateNodeTypeOperation("Metric", "enabled", typeof(bool))]));
        Assert.Throws<NotSupportedException>(() => dialect.Compile(
            [new CreateNodeTypeOperation(
                "Metric",
                "id",
                typeof(string),
                [new GraphSchemaProperty("payload", typeof(Version))])]));
        Assert.Throws<NotSupportedException>(() => dialect.Compile([new UnsupportedMigrationOperation()]));
    }

    [Fact]
    public void CompileMapsNullableDateGuidAndEnumAttributes()
    {
        var dialect = new TigerGraphMigrationDialect("SocialGraph");

        var commands = dialect.Compile(
        [
            new CreateNodeTypeOperation(
                "Typed",
                "id",
                typeof(Guid),
                [
                    new GraphSchemaProperty("optional_count", typeof(int?)),
                    new GraphSchemaProperty("created_at", typeof(DateTimeOffset)),
                    new GraphSchemaProperty("kind", typeof(MetricLevel)),
                ]),
        ]);

        Assert.Contains(
            "PRIMARY_ID id STRING, optional_count INT, created_at DATETIME, kind INT",
            commands[0].Text,
            StringComparison.Ordinal);
    }

    private enum MetricLevel
    {
        Low,
        High,
    }

    private sealed record UnsupportedMigrationOperation : MigrationOperation;
}
