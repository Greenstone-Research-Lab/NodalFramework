using System.Reflection;
using Nodal.Migrations;

namespace Nodal.Tool;

internal static class CliMigrationExecutionHostLoader
{
    internal const string AssemblyVariable = "NODAL_MIGRATION_HOST_ASSEMBLY";
    internal const string TypeVariable = "NODAL_MIGRATION_HOST_TYPE";

    public static INodalMigrationBundleExecutionHost LoadFromEnvironment() =>
        Load(Environment.GetEnvironmentVariable);

    internal static INodalMigrationBundleExecutionHost Load(Func<string, string?> readVariable)
    {
        ArgumentNullException.ThrowIfNull(readVariable);
        var assemblyPath = readVariable(AssemblyVariable);
        var typeName = readVariable(TypeVariable);
        if (string.IsNullOrWhiteSpace(assemblyPath) || string.IsNullOrWhiteSpace(typeName))
        {
            throw new InvalidOperationException(
                $"Migration execution requires trusted host variables '{AssemblyVariable}' and '{TypeVariable}'.");
        }

        var fullPath = Path.GetFullPath(assemblyPath);
        var assembly = Assembly.LoadFrom(fullPath);
        var type = assembly.GetType(typeName, throwOnError: false, ignoreCase: false);
        if (type is null || type.IsAbstract || !typeof(INodalMigrationBundleExecutionHost).IsAssignableFrom(type))
        {
            throw new InvalidOperationException(
                "The configured migration host type does not implement the required execution contract.");
        }

        try
        {
            return (INodalMigrationBundleExecutionHost)(Activator.CreateInstance(type)
                ?? throw new InvalidOperationException("The configured migration host could not be created."));
        }
        catch (TargetInvocationException exception)
        {
            throw new InvalidOperationException(
                "The configured migration host could not be created.",
                exception.InnerException ?? exception);
        }
    }
}
