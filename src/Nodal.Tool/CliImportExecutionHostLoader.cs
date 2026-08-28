using System.Reflection;
using Nodal.Core.Mutations;

namespace Nodal.Tool;

internal static class CliImportExecutionHostLoader
{
    internal const string AssemblyVariable = "NODAL_IMPORT_HOST_ASSEMBLY";
    internal const string TypeVariable = "NODAL_IMPORT_HOST_TYPE";

    public static IGraphMutationExecutor LoadFromEnvironment() => Load(Environment.GetEnvironmentVariable);

    internal static IGraphMutationExecutor Load(Func<string, string?> readVariable)
    {
        ArgumentNullException.ThrowIfNull(readVariable);
        var assemblyPath = readVariable(AssemblyVariable);
        var typeName = readVariable(TypeVariable);
        if (string.IsNullOrWhiteSpace(assemblyPath) || string.IsNullOrWhiteSpace(typeName))
        {
            throw new InvalidOperationException(
                $"Import apply requires trusted host variables '{AssemblyVariable}' and '{TypeVariable}'.");
        }

        var assembly = Assembly.LoadFrom(Path.GetFullPath(assemblyPath));
        var type = assembly.GetType(typeName, throwOnError: false, ignoreCase: false);
        if (type is null || type.IsAbstract || !typeof(IGraphMutationExecutor).IsAssignableFrom(type))
        {
            throw new InvalidOperationException(
                "The configured import host type does not implement IGraphMutationExecutor.");
        }

        try
        {
            return (IGraphMutationExecutor)(Activator.CreateInstance(type)
                ?? throw new InvalidOperationException("The configured import host could not be created."));
        }
        catch (TargetInvocationException exception)
        {
            throw new InvalidOperationException(
                "The configured import host could not be created.",
                exception.InnerException ?? exception);
        }
    }
}
