using System.Text;

namespace Nodal.Tool;

internal interface ICliFileSystem
{
    TextReader OpenText(string path);

    ValueTask<string> ReadAllTextAsync(string path, CancellationToken cancellationToken);

    ValueTask WriteAllTextAsync(string path, string content, CancellationToken cancellationToken);

    IReadOnlyList<string> EnumerateFiles(string directory, string searchPattern);
}

internal sealed class PhysicalCliFileSystem : ICliFileSystem
{
    private static readonly Encoding Utf8WithoutByteOrderMark = new UTF8Encoding(false);

    public static PhysicalCliFileSystem Instance { get; } = new();

    private PhysicalCliFileSystem()
    {
    }

    public TextReader OpenText(string path) => new StreamReader(path, Encoding.UTF8, true);

    public async ValueTask<string> ReadAllTextAsync(
        string path,
        CancellationToken cancellationToken) =>
        await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);

    public async ValueTask WriteAllTextAsync(
        string path,
        string content,
        CancellationToken cancellationToken) =>
        await File.WriteAllTextAsync(path, content, Utf8WithoutByteOrderMark, cancellationToken)
            .ConfigureAwait(false);

    public IReadOnlyList<string> EnumerateFiles(string directory, string searchPattern) =>
        Directory.GetFiles(directory, searchPattern, SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .ToArray();
}
