using Nodal.Core.Providers;

namespace Nodal.Core.Execution;

/// <summary>
/// Groups the query, transport, and materialization services supplied by a graph database provider.
/// </summary>
public interface IGraphProvider
{
    /// <summary>Gets the provider-specific query compiler.</summary>
    IGraphQueryCompiler QueryCompiler { get; }

    /// <summary>Gets the provider-specific command executor.</summary>
    IGraphCommandExecutor CommandExecutor { get; }

    /// <summary>Gets the domain object materializer.</summary>
    IGraphResultMaterializer ResultMaterializer { get; }
}
