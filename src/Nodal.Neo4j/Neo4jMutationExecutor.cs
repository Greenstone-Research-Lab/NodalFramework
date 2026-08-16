using Neo4j.Driver;
using Nodal.Core.Mutations;

namespace Nodal.Neo4j;

/// <summary>Executes a complete Nodal mutation plan in one Neo4j write transaction.</summary>
public sealed class Neo4jMutationExecutor : IGraphMutationExecutor
{
    private readonly IDriver driver;
    private readonly string? database;

    /// <summary>Initializes an executor with an externally managed pooled driver.</summary>
    public Neo4jMutationExecutor(
        IDriver driver,
        string? database = null)
    {
        ArgumentNullException.ThrowIfNull(driver);
        this.driver = driver;
        this.database = database;
    }

    /// <inheritdoc />
    public async ValueTask<GraphMutationResult> ExecuteAsync(
        GraphMutationPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();
        var commands = Neo4jMutationCompiler.Compile(plan);

        await using var session = driver.AsyncSession(ConfigureSession);
        await session.ExecuteWriteAsync(async transaction =>
        {
            foreach (var command in commands)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var parameters = command.Parameters.ToDictionary(
                    parameter => parameter.Key,
                    parameter => parameter.Value!);
                var cursor = await transaction.RunAsync(command.Text, parameters).ConfigureAwait(false);
                await cursor.ConsumeAsync().ConfigureAwait(false);
            }
        }).ConfigureAwait(false);

        var affectedNodes = plan.Operations.Count(operation =>
            operation is CreateNodeOperation or UpdateNodeOperation or DeleteNodeOperation);
        var affectedRelations = plan.Operations.Count(operation =>
            operation is CreateRelationOperation or UpdateRelationOperation or DeleteRelationOperation);
        return new GraphMutationResult(affectedNodes, affectedRelations, true);
    }

    private void ConfigureSession(SessionConfigBuilder builder)
    {
        if (!string.IsNullOrWhiteSpace(database))
        {
            builder.WithDatabase(database);
        }
    }
}
