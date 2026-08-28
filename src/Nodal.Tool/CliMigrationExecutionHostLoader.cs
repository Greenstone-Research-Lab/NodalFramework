using Nodal.Migrations;

namespace Nodal.Tool;

internal static class CliMigrationExecutionHostLoader
{
    internal const string AssemblyVariable = "NODAL_MIGRATION_HOST_ASSEMBLY";
    internal const string TypeVariable = "NODAL_MIGRATION_HOST_TYPE";

    public static INodalMigrationBundleExecutionHost LoadFromEnvironment() =>
        Load(Environment.GetEnvironmentVariable);

    internal static INodalMigrationBundleExecutionHost Load(Func<string, string?> readVariable) =>
        CliCompositionHostLoader.Load<INodalMigrationBundleExecutionHost>(
            readVariable,
            AssemblyVariable,
            TypeVariable,
            "Migration execution",
            "The configured migration host type does not implement the required execution contract.",
            "The configured migration host could not be created.");
}
