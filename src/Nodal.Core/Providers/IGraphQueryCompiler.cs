using Nodal.Core.Query;

namespace Nodal.Core.Providers;

/// <summary>
/// Compiles a provider-neutral graph query into a database-specific command.
/// </summary>
public interface IGraphQueryCompiler
{
    /// <summary>
    /// Compiles the supplied query without embedding parameter values in command text.
    /// </summary>
    /// <param name="query">The provider-neutral query model.</param>
    /// <returns>A database command and its separate parameters.</returns>
    GraphCommand Compile(GraphQueryModel query);
}

/// <summary>
/// Represents provider-specific command text with separately transported parameters.
/// </summary>
/// <param name="Text">The provider command text.</param>
/// <param name="Parameters">The command parameters.</param>
/// <param name="Route">An optional provider-relative execution route for installed operations.</param>
public sealed record GraphCommand(
    string Text,
    IReadOnlyDictionary<string, object?> Parameters,
    string? Route = null);
