using System.Text.Json;
using Nodal.Core.Analytics;
using Nodal.Core.Providers;
using Nodal.Import;
using Nodal.Tool;

namespace Nodal.ArchitectureTests;

public sealed class DependencyDirectionTests
{
    [Fact]
    public void CoreDoesNotReferenceDatabaseProviders()
    {
        var references = typeof(IGraphQueryCompiler).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToArray();

        Assert.DoesNotContain("Nodal.Neo4j", references);
        Assert.DoesNotContain("Nodal.TigerGraph", references);
        Assert.DoesNotContain("Nodal.Migrations", references);
    }

    [Fact]
    public void ToolDependsOnlyOnProviderNeutralProductAssemblies()
    {
        var references = typeof(NodalCli).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToArray();

        Assert.Contains("Nodal.Core", references);
        Assert.Contains("Nodal.Import", references);
        Assert.Contains("Nodal.Import.Csv", references);
        Assert.Contains("Nodal.Migrations", references);
        Assert.DoesNotContain("Nodal.Neo4j", references);
        Assert.DoesNotContain("Nodal.TigerGraph", references);
    }

    [Fact]
    public void ImportPlanningDependsOnCoreButNotProviderImplementations()
    {
        var references = typeof(GraphImportPlanner<>).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToArray();

        Assert.Contains("Nodal.Core", references);
        Assert.DoesNotContain("Nodal.Neo4j", references);
        Assert.DoesNotContain("Nodal.TigerGraph", references);
    }

    [Fact]
    public void PublishedKnowledgeGraphListsEveryPortableAnalyticsAlgorithm()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Nodal.slnx")))
        {
            root = root.Parent;
        }
        Assert.NotNull(root);
        var path = Path.Combine(root.FullName, "website", "static", "knowledge", "nodal-capabilities.jsonld");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var capability = document.RootElement.GetProperty("@graph").EnumerateArray().Single(item =>
            item.GetProperty("@id").GetString() == "nodal:ConditionalAnalytics");
        var documented = capability.GetProperty("algorithms").EnumerateArray()
            .Select(item => item.GetString()!).ToHashSet(StringComparer.Ordinal);

        Assert.True(Enum.GetNames<GraphAnalyticsAlgorithm>().ToHashSet(StringComparer.Ordinal).SetEquals(documented));
    }
}
