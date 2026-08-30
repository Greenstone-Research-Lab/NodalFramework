using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.Fluent.Syntax.Elements.Types;
using ArchUnitNET.Loader;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Nodal.ArchitectureTests;

public sealed class ProductArchitectureRules
{
    private static readonly string[] ExpectedProductAssemblies =
    [
        "Nodal.Analytics",
        "Nodal.Core",
        "Nodal.Import",
        "Nodal.Import.Csv",
        "Nodal.Import.Relational",
        "Nodal.Migrations",
        "Nodal.Modeling.CodeGeneration",
        "Nodal.Neo4j",
        "Nodal.TigerGraph",
        "Nodal.Tool",
    ];

    private static readonly Architecture ProductArchitecture = new ArchLoader()
        .LoadFilteredDirectory(AppContext.BaseDirectory, "Nodal.Analytics.dll", SearchOption.TopDirectoryOnly)
        .LoadFilteredDirectory(AppContext.BaseDirectory, "Nodal.Core.dll", SearchOption.TopDirectoryOnly)
        .LoadFilteredDirectory(AppContext.BaseDirectory, "Nodal.Import.dll", SearchOption.TopDirectoryOnly)
        .LoadFilteredDirectory(AppContext.BaseDirectory, "Nodal.Import.Csv.dll", SearchOption.TopDirectoryOnly)
        .LoadFilteredDirectory(AppContext.BaseDirectory, "Nodal.Import.Relational.dll", SearchOption.TopDirectoryOnly)
        .LoadFilteredDirectory(AppContext.BaseDirectory, "Nodal.Migrations.dll", SearchOption.TopDirectoryOnly)
        .LoadFilteredDirectory(AppContext.BaseDirectory, "Nodal.Modeling.CodeGeneration.dll", SearchOption.TopDirectoryOnly)
        .LoadFilteredDirectory(AppContext.BaseDirectory, "Nodal.Neo4j.dll", SearchOption.TopDirectoryOnly)
        .LoadFilteredDirectory(AppContext.BaseDirectory, "Nodal.TigerGraph.dll", SearchOption.TopDirectoryOnly)
        .LoadFilteredDirectory(AppContext.BaseDirectory, "Nodal.Tool.dll", SearchOption.TopDirectoryOnly)
        .Build();

    [Fact]
    public void ArchitectureMustLoadEveryProductAssembly()
    {
        var loadedAssemblies = ProductArchitecture.Assemblies
            .Where(assembly => !assembly.IsOnlyReferenced)
            .Select(assembly => assembly.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ExpectedProductAssemblies, loadedAssemblies);
    }

    [Fact]
    public void CoreMustNotDependOnNeo4j() => AssertRule(
        MustNotDependOn("Nodal.Core", "Nodal.Neo4j"));

    [Fact]
    public void CoreMustNotDependOnTigerGraph() => AssertRule(
        MustNotDependOn("Nodal.Core", "Nodal.TigerGraph"));

    [Fact]
    public void CoreMustNotDependOnOuterProductLayers()
    {
        AssertRule(MustNotDependOn("Nodal.Core", "Nodal.Migrations"));
        AssertRule(MustNotDependOn("Nodal.Core", "Nodal.Analytics"));
        AssertRule(MustNotDependOn("Nodal.Core", "Nodal.Import"));
        AssertRule(MustNotDependOn("Nodal.Core", "Nodal.Tool"));
        AssertRule(MustNotDependOn("Nodal.Core", "Nodal.Modeling.CodeGeneration"));
    }

    [Fact]
    public void ProvidersMustRemainIsolatedFromEachOther()
    {
        AssertRule(MustNotDependOn("Nodal.Neo4j", "Nodal.TigerGraph"));
        AssertRule(MustNotDependOn("Nodal.TigerGraph", "Nodal.Neo4j"));
    }

    [Fact]
    public void ImportLayersMustRemainProviderNeutral()
    {
        foreach (var assemblyName in new[] { "Nodal.Import", "Nodal.Import.Csv", "Nodal.Import.Relational" })
        {
            AssertRule(MustNotDependOn(assemblyName, "Nodal.Neo4j"));
            AssertRule(MustNotDependOn(assemblyName, "Nodal.TigerGraph"));
        }
    }

    [Fact]
    public void ToolMustNotDependOnConcreteGraphProviders()
    {
        AssertRule(MustNotDependOn("Nodal.Tool", "Nodal.Neo4j"));
        AssertRule(MustNotDependOn("Nodal.Tool", "Nodal.TigerGraph"));
    }

    [Fact]
    public void CodeGenerationMustRemainAProviderNeutralLeafAboveCore()
    {
        foreach (var assemblyName in new[]
                 {
                     "Nodal.Analytics", "Nodal.Import", "Nodal.Import.Csv", "Nodal.Import.Relational",
                     "Nodal.Migrations", "Nodal.Neo4j", "Nodal.TigerGraph", "Nodal.Tool",
                 })
        {
            AssertRule(MustNotDependOn("Nodal.Modeling.CodeGeneration", assemblyName));
        }
    }

    [Fact]
    public void ProductAssembliesMustBeFreeOfDependencyCycles()
    {
        var productNames = ExpectedProductAssemblies.ToHashSet(StringComparer.Ordinal);
        var remainingDependencies = ProductArchitecture.Assemblies
            .Where(assembly => !assembly.IsOnlyReferenced && productNames.Contains(assembly.Name))
            .ToDictionary(
                assembly => assembly.Name,
                assembly => assembly.ReferencedAssemblyNames
                    .Where(productNames.Contains)
                    .ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);

        while (remainingDependencies.FirstOrDefault(pair => pair.Value.Count == 0) is { Key: not null } leaf)
        {
            remainingDependencies.Remove(leaf.Key);
            foreach (var dependencies in remainingDependencies.Values)
            {
                dependencies.Remove(leaf.Key);
            }
        }

        Assert.True(
            remainingDependencies.Count == 0,
            $"Product assembly dependency cycle detected: {string.Join(", ", remainingDependencies.Keys)}");
    }

    private static GivenTypesConjunctionWithDescription ProductTypes(string assemblyName) =>
        Types().That().ResideInAssembly(assemblyName).As(assemblyName);

    private static ArchRule<IType> MustNotDependOn(string sourceAssembly, string targetAssembly) =>
        ProductTypes(sourceAssembly)
            .Should()
            .NotDependOnAny(ProductTypes(targetAssembly))
            .WithoutRequiringPositiveResults();

    private static void AssertRule(ArchRule<IType> rule)
    {
        var violations = rule.Evaluate(ProductArchitecture)
            .Where(result => !result.Passed)
            .Select(result => result.Description)
            .ToArray();

        Assert.Empty(violations);
    }
}
