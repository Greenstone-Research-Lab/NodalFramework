using Nodal.Import.Relational;

namespace Nodal.Tool;

internal static class CliRelationalInspectionHostLoader
{
    internal const string AssemblyVariable = "NODAL_RELATIONAL_HOST_ASSEMBLY";
    internal const string TypeVariable = "NODAL_RELATIONAL_HOST_TYPE";

    public static IRelationalInspectionHost LoadFromEnvironment() =>
        Load(Environment.GetEnvironmentVariable);

    internal static IRelationalInspectionHost Load(Func<string, string?> readVariable) =>
        CliCompositionHostLoader.Load<IRelationalInspectionHost>(
            readVariable,
            AssemblyVariable,
            TypeVariable,
            "Relational inspection",
            "The configured relational inspection host type does not implement IRelationalInspectionHost.",
            "The configured relational inspection host could not be created.");
}
