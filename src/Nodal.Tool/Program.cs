using System.Diagnostics.CodeAnalysis;

namespace Nodal.Tool;

[ExcludeFromCodeCoverage]
internal static class Program
{
    public static Task<int> Main(string[] args) => NodalCli.RunAsync(
        args,
        Console.Out,
        Console.Error,
        PhysicalCliFileSystem.Instance,
        CancellationToken.None);
}
