using Nodal.Core.Migrations;

namespace Nodal.Core.Tests;

public sealed class MigrationContractCoverageTests
{
    [Fact]
    public void EvolutionOperationsExposeStableMetadata()
    {
        var property = new GraphSchemaProperty("name", typeof(string));
        var operations = new MigrationOperation[]
        {
            new CreateUniqueConstraintOperation("Person", "id"),
            new DropIndexOperation("Person", "name"),
            new DropUniqueConstraintOperation("Person", "id"),
            new AddNodePropertyOperation("Person", property),
            new AddRelationPropertyOperation("KNOWS", property),
            new DropNodePropertyOperation("Person", "name"),
            new DropRelationPropertyOperation("KNOWS", "name"),
            new RenameNodePropertyOperation("Person", "name", "display_name"),
            new RenameRelationPropertyOperation("KNOWS", "name", "label"),
            new AlterNodePropertyTypeOperation(
                "Person",
                "age",
                typeof(int),
                typeof(long),
                MigrationPropertyTypeCompatibility.RequiresRewrite),
            new AlterRelationPropertyTypeOperation(
                "KNOWS",
                "weight",
                typeof(int),
                typeof(double),
                MigrationPropertyTypeCompatibility.Destructive),
        };

        Assert.Equal("id", ((CreateUniqueConstraintOperation)operations[0]).PropertyName);
        Assert.Equal("name", ((DropIndexOperation)operations[1]).PropertyName);
        Assert.Equal("id", ((DropUniqueConstraintOperation)operations[2]).PropertyName);
        Assert.Equal("name", ((AddNodePropertyOperation)operations[3]).Property.Name);
        Assert.Equal("KNOWS", ((AddRelationPropertyOperation)operations[4]).RelationType);
        Assert.Equal("name", ((DropNodePropertyOperation)operations[5]).PropertyName);
        Assert.Equal("name", ((DropRelationPropertyOperation)operations[6]).PropertyName);
        Assert.Equal("display_name", ((RenameNodePropertyOperation)operations[7]).NewPropertyName);
        Assert.Equal("label", ((RenameRelationPropertyOperation)operations[8]).NewPropertyName);
        Assert.Equal(
            MigrationPropertyTypeCompatibility.RequiresRewrite,
            ((AlterNodePropertyTypeOperation)operations[9]).Compatibility);
        Assert.Equal(
            MigrationPropertyTypeCompatibility.Destructive,
            ((AlterRelationPropertyTypeOperation)operations[10]).Compatibility);

        Assert.Equal(typeof(string), property.ClrType);
        Assert.Equal("Person", ((CreateUniqueConstraintOperation)operations[0]).NodeType);
        Assert.Equal("Person", ((DropIndexOperation)operations[1]).NodeType);
        Assert.Equal("Person", ((DropUniqueConstraintOperation)operations[2]).NodeType);
        Assert.Equal("Person", ((AddNodePropertyOperation)operations[3]).NodeType);
        Assert.Equal("KNOWS", ((AddRelationPropertyOperation)operations[4]).RelationType);
        Assert.Equal("Person", ((DropNodePropertyOperation)operations[5]).NodeType);
        Assert.Equal("KNOWS", ((DropRelationPropertyOperation)operations[6]).RelationType);
        Assert.Equal("name", ((RenameNodePropertyOperation)operations[7]).OldPropertyName);
        Assert.Equal("Person", ((RenameNodePropertyOperation)operations[7]).NodeType);
        Assert.Equal("label", ((RenameRelationPropertyOperation)operations[8]).NewPropertyName);
        Assert.Equal("KNOWS", ((RenameRelationPropertyOperation)operations[8]).RelationType);
        Assert.Equal("name", ((RenameRelationPropertyOperation)operations[8]).OldPropertyName);
        Assert.Equal(typeof(int), ((AlterNodePropertyTypeOperation)operations[9]).OldClrType);
        Assert.Equal(typeof(long), ((AlterNodePropertyTypeOperation)operations[9]).NewClrType);
        Assert.Equal("Person", ((AlterNodePropertyTypeOperation)operations[9]).NodeType);
        Assert.Equal("age", ((AlterNodePropertyTypeOperation)operations[9]).PropertyName);
        Assert.Equal(typeof(int), ((AlterRelationPropertyTypeOperation)operations[10]).OldClrType);
        Assert.Equal(typeof(double), ((AlterRelationPropertyTypeOperation)operations[10]).NewClrType);
        Assert.Equal("KNOWS", ((AlterRelationPropertyTypeOperation)operations[10]).RelationType);
        Assert.Equal("weight", ((AlterRelationPropertyTypeOperation)operations[10]).PropertyName);

        var dropSchema = new DropSchemaObjectOperation("ix_person_name", MigrationSchemaObjectKind.Index);
        Assert.Equal("ix_person_name", dropSchema.Name);
        Assert.Equal(MigrationSchemaObjectKind.Index, dropSchema.Kind);
    }

    [Fact]
    public void BackfillContractsValidateAndExposeContinuationState()
    {
        var request = new MigrationBackfillRequest("normalize", 25);
        var context = new MigrationBackfillContext("page-2", request.BatchSize, request.Name);
        var unnamedContext = new MigrationBackfillContext(null, request.BatchSize);
        var pending = new MigrationBackfillBatchResult(25, "page-3", false);
        var complete = new MigrationBackfillBatchResult(3, null, true);
        var updatedAt = DateTimeOffset.UtcNow;
        var checkpoint = new MigrationBackfillCheckpoint(
            request.Name,
            context.ContinuationToken,
            pending.Processed,
            updatedAt);

        Assert.Equal("normalize", request.Name);
        Assert.Equal(25, context.BatchSize);
        Assert.Equal("page-2", context.ContinuationToken);
        Assert.Equal("normalize", context.BackfillName);
        Assert.NotEqual(context.IdempotencyKey, unnamedContext.IdempotencyKey);
        Assert.Equal(64, context.IdempotencyKey.Length);
        Assert.True(pending.HasMore);
        Assert.False(complete.HasMore);
        Assert.Equal("normalize", checkpoint.BackfillName);
        Assert.Equal("page-2", checkpoint.ContinuationToken);
        Assert.Equal(25, checkpoint.Processed);
        Assert.Equal(updatedAt, checkpoint.UpdatedAt);

        Assert.Throws<ArgumentException>(
            () => new MigrationBackfillRequest(" ", 10));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new MigrationBackfillRequest("invalid", 0));
    }

    [Fact]
    public void CapabilityExceptionPreservesProviderAndCode()
    {
        var exception = new NodalCapabilityNotSupportedException(
            "Neo4jMigrationDialect",
            "NODAL-MIGRATION-UNSUPPORTED",
            "Type alteration requires a backfill.");

        Assert.Equal("Neo4jMigrationDialect", exception.ProviderName);
        Assert.Equal("NODAL-MIGRATION-UNSUPPORTED", exception.CapabilityCode);
        Assert.Contains("Type alteration", exception.Message, StringComparison.Ordinal);

        var legacy = new NodalCapabilityNotSupportedException("legacy");
        Assert.Equal("Unknown", legacy.ProviderName);

        var inner = new InvalidOperationException("provider failure");
        var legacyWithInner = new NodalCapabilityNotSupportedException("legacy", inner);
        Assert.Same(inner, legacyWithInner.InnerException);
        Assert.Equal("NODAL-CAPABILITY-UNSPECIFIED", legacyWithInner.CapabilityCode);
    }

    [Fact]
    public void PreflightResultClassifiesIssuesAndThrowsForUnsupported()
    {
        var unsupported = new MigrationPreflightIssue(
            MigrationPreflightKind.Unsupported,
            "NODAL-MIGRATION-UNSUPPORTED",
            "Not available.",
            typeof(DropIndexOperation));
        var warning = new MigrationPreflightIssue(
            MigrationPreflightKind.Warning,
            "NODAL-MIGRATION-NATIVE-SCHEMA",
            "Provider manages properties implicitly.",
            typeof(AddNodePropertyOperation));
        var result = new MigrationPreflightResult(
            [unsupported, warning],
            "Neo4jMigrationDialect");

        Assert.False(result.IsValid);
        Assert.True(result.HasWarnings);
        Assert.False(result.RequiresApproval);

        var exception = Assert.Throws<NodalCapabilityNotSupportedException>(
            result.ThrowIfInvalid);
        Assert.Equal("Neo4jMigrationDialect", exception.ProviderName);
        Assert.Equal("NODAL-MIGRATION-UNSUPPORTED", exception.CapabilityCode);

        var valid = new MigrationPreflightResult([], "TigerGraph");
        valid.ThrowIfInvalid();
        Assert.True(valid.IsValid);
        Assert.False(valid.RequiresApproval);
        Assert.False(valid.HasWarnings);
    }

    [Fact]
    public void MigrationStateContractsExposeLifecycleAndFailureMetadata()
    {
        var occurred = DateTimeOffset.UtcNow;
        var failure = new MigrationExecutionFailure(
            "safe message", "InvalidOperationException", occurred);
        var history = new MigrationHistoryEntry(
            "M001", "checksum", MigrationExecutionState.Failed, occurred, occurred, failure);
        var options = new MigrationExecutionOptions { AllowDestructiveOperations = true };
        var batch = new MigrationBackfillBatchResult(4, "next", false);
        var lockError = new MigrationLockUnavailableException(
            "neo4j://local", "lock unavailable", innerException: null);
        var lockCause = new InvalidOperationException("connection refused");
        var lockErrorWithCause = new MigrationLockUnavailableException(
            "neo4j://local", "lock unavailable", lockCause);

        Assert.Equal("safe message", failure.Message);
        Assert.Equal("InvalidOperationException", failure.ErrorType);
        Assert.Equal(occurred, failure.OccurredAt);
        Assert.Equal("M001", history.Id);
        Assert.Equal("checksum", history.Checksum);
        Assert.Equal(MigrationExecutionState.Failed, history.State);
        Assert.Equal(occurred, history.StartedAt);
        Assert.Equal(occurred, history.CompletedAt);
        Assert.Same(failure, history.Failure);
        Assert.True(options.AllowDestructiveOperations);
        Assert.Equal(4, batch.Processed);
        Assert.Equal("next", batch.ContinuationToken);
        Assert.Equal("neo4j://local", lockError.Scope);
        Assert.Same(lockCause, lockErrorWithCause.InnerException);
        Assert.Throws<ArgumentException>(
            () => new MigrationLockUnavailableException(" ", "invalid"));
    }
}
