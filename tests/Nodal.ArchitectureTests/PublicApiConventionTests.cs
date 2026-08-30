using System.Reflection;
using System.Runtime.CompilerServices;
using Nodal.Analytics.Observations;
using Nodal.Core;
using Nodal.Import;
using Nodal.Import.Csv;
using Nodal.Import.Relational;
using Nodal.Migrations;
using Nodal.Modeling.CodeGeneration;
using Nodal.Neo4j;
using Nodal.TigerGraph;
using Nodal.Tool;

namespace Nodal.ArchitectureTests;

public sealed class PublicApiConventionTests
{
    private static readonly Assembly[] ProductAssemblies =
    [
        typeof(GraphObservation).Assembly,
        typeof(NodalContext).Assembly,
        typeof(GraphImportRunner<>).Assembly,
        typeof(CsvImportReader).Assembly,
        typeof(RelationalInteractionModelBuilder).Assembly,
        typeof(MigrationRunner).Assembly,
        typeof(GraphModelCodeGenerator).Assembly,
        typeof(Neo4jProvider).Assembly,
        typeof(TigerGraphProvider).Assembly,
        typeof(NodalCli).Assembly,
    ];

    [Fact]
    public void PublicTaskLikeMethodsUseAsyncNamingAndCancellation()
    {
        var violations = ProductAssemblies
            .SelectMany(assembly => assembly.GetExportedTypes())
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(method => !method.IsSpecialName && IsTaskLike(method.ReturnType))
            .SelectMany(ValidateAsyncMethod)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void PublicApisNeverExposeAsyncVoid()
    {
        var violations = ProductAssemblies
            .SelectMany(assembly => assembly.GetExportedTypes())
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(method => method.ReturnType == typeof(void))
            .Where(method => method.GetCustomAttribute<AsyncStateMachineAttribute>() is not null)
            .Select(Describe)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void PublicExceptionTypesUseTheExceptionSuffix()
    {
        var violations = ProductAssemblies
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => typeof(Exception).IsAssignableFrom(type))
            .Where(type => !type.Name.EndsWith("Exception", StringComparison.Ordinal))
            .Select(type => $"{type.FullName} must use the Exception suffix.")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(violations);
    }

    private static IEnumerable<string> ValidateAsyncMethod(MethodInfo method)
    {
        if (method.Name is "Main" or "DisposeAsync")
        {
            yield break;
        }

        if (!method.Name.EndsWith("Async", StringComparison.Ordinal))
        {
            yield return $"{Describe(method)} returns a task-like value but does not use the Async suffix.";
        }

        var parameters = method.GetParameters();
        var cancellationIndex = Array.FindIndex(parameters, parameter => parameter.ParameterType == typeof(CancellationToken));
        if (cancellationIndex < 0)
        {
            yield return $"{Describe(method)} does not expose CancellationToken.";
        }
        else if (cancellationIndex != parameters.Length - 1)
        {
            yield return $"{Describe(method)} must place CancellationToken last.";
        }
    }

    private static bool IsTaskLike(Type type) =>
        type == typeof(Task) ||
        type == typeof(ValueTask) ||
        (type.IsGenericType && type.GetGenericTypeDefinition() is var definition &&
         (definition == typeof(Task<>) || definition == typeof(ValueTask<>)));

    private static string Describe(MethodInfo method) =>
        $"{method.DeclaringType?.FullName}.{method.Name}";
}
