using Nodal.Core.Providers;

namespace Nodal.Core.Execution;

/// <summary>
/// Executes a database-specific graph command and normalizes its response.
/// </summary>
public interface IGraphCommandExecutor
{
    /// <summary>
    /// Executes a command asynchronously.
    /// </summary>
    ValueTask<GraphQueryResult> ExecuteAsync(
        GraphCommand command,
        CancellationToken cancellationToken = default);
}
