using Nodal.Core.Mutations;

namespace Nodal.Tool;

internal static class CliImportExecutionHostLoader
{
    internal const string AssemblyVariable = "NODAL_IMPORT_HOST_ASSEMBLY";
    internal const string TypeVariable = "NODAL_IMPORT_HOST_TYPE";

    public static IGraphMutationExecutor LoadFromEnvironment() => Load(Environment.GetEnvironmentVariable);

    internal static IGraphMutationExecutor Load(Func<string, string?> readVariable) =>
        CliCompositionHostLoader.Load<IGraphMutationExecutor>(
            readVariable,
            AssemblyVariable,
            TypeVariable,
            "Import apply",
            "The configured import host type does not implement IGraphMutationExecutor.",
            "The configured import host could not be created.");
}
