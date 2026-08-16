using Nodal.Core.Providers;

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
}
