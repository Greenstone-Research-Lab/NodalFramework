using System.Text.Json;
using Nodal.Core.Migrations;
using Nodal.Migrations;
using Nodal.Tool;

namespace Nodal.Tool.Tests;

public sealed class NodalCliTests
{
    private static readonly JsonSerializerOptions CamelCaseJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public async Task SnapshotCanonicalizesInputWritesHashAndValidatesResult()
    {
        var files = Files(("model.json", Serialize(After())));
        var result = await RunAsync(files, "migrations", "snapshot", "--input", "model.json", "--output", "snapshot.json");

        Assert.Equal(NodalCli.Success, result.ExitCode);
        Assert.EndsWith(Environment.NewLine, files.Content["snapshot.json"], StringComparison.Ordinal);
        Assert.Contains("Snapshot written. Hash:", result.Output, StringComparison.Ordinal);

        var validation = await RunAsync(files, "migrations", "validate", "--snapshot", "snapshot.json");
        Assert.Equal(NodalCli.Success, validation.ExitCode);
        Assert.Contains("Valid snapshot v1. Hash:", validation.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiffRendersStableTextAndJson()
    {
        var files = Pair();

        var text = await RunAsync(files, "migrations", "diff", "--from", "before.json", "--to", "after.json");
        var json = await RunAsync(
            files,
            "migrations", "diff", "--from", "before.json", "--to", "after.json", "--format", "json");

        Assert.Equal(NodalCli.Success, text.ExitCode);
        Assert.Equal($"NodePropertyAdded: people.name{Environment.NewLine}", text.Output);
        Assert.Contains("\"kind\": \"nodePropertyAdded\"", json.Output, StringComparison.Ordinal);
        Assert.Contains("\"objectName\": \"people\"", json.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiffWritesRequestedOutputAndReportsNoChanges()
    {
        var files = Pair();
        var written = await RunAsync(
            files,
            "migrations", "diff", "--from", "before.json", "--to", "after.json", "--output", "diff.txt");
        var empty = await RunAsync(
            files,
            "migrations", "diff", "--from", "before.json", "--to", "before.json");

        Assert.Equal(string.Empty, written.Output);
        Assert.Equal($"NodePropertyAdded: people.name{Environment.NewLine}", files.Content["diff.txt"]);
        Assert.Equal($"No schema changes.{Environment.NewLine}", empty.Output);
    }

    [Fact]
    public async Task PlanRendersMarkdownAndJson()
    {
        var files = Pair();
        var markdown = await RunAsync(files, "migrations", "plan", "--from", "before.json", "--to", "after.json");
        var json = await RunAsync(
            files,
            "migrations", "plan", "--from", "before.json", "--to", "after.json", "--format", "json");

        Assert.Contains("# Nodal schema migration plan", markdown.Output, StringComparison.Ordinal);
        Assert.Contains("Add node property people.name", markdown.Output, StringComparison.Ordinal);
        Assert.Contains("\"operations\"", json.Output, StringComparison.Ordinal);
        Assert.Contains("Add node property people.name", json.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(new string[0], "Expected 'nodal migrations <command>'.")]
    [InlineData(new[] { "other", "diff" }, "Expected 'nodal migrations <command>'.")]
    [InlineData(new[] { "migrations", "--format", "json" }, "A migration command is required.")]
    [InlineData(new[] { "migrations", "unknown" }, "Unknown migration command 'unknown'.")]
    [InlineData(new[] { "migrations", "validate" }, "Required option '--snapshot' was not supplied.")]
    [InlineData(new[] { "migrations", "validate", "snapshot" }, "Options must use the '--name value' form.")]
    [InlineData(new[] { "migrations", "validate", "--snapshot" }, "Options must use the '--name value' form.")]
    [InlineData(new[] { "migrations", "validate", "--snapshot", "a", "--snapshot", "b" }, "Option '--snapshot' was specified more than once.")]
    [InlineData(new[] { "migrations", "validate", "--snapshot", "a", "--token", "do-not-print" }, "Unknown option '--token'.")]
    [InlineData(new[] { "migrations", "diff", "--from", "a", "--to", "b", "--format", "xml" }, "Option '--format' must be 'text', 'json', or 'github'.")]
    public async Task UsageFailuresAreStableAndDoNotEchoOptionValues(string[] arguments, string expected)
    {
        var result = await RunAsync(Pair(), arguments);

        Assert.Equal(NodalCli.UsageError, result.ExitCode);
        Assert.StartsWith(expected, result.Error, StringComparison.Ordinal);
        Assert.Contains("Usage: nodal migrations", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("do-not-print", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not-json", "A schema snapshot contains invalid JSON.")]
    [InlineData("{\"formatVersion\":2,\"nodes\":[],\"relations\":[]}", "Schema snapshot format version '2' is not supported.")]
    public async Task InvalidSnapshotsReturnDataExitCode(string content, string expected)
    {
        var result = await RunAsync(Files(("bad.json", content)), "migrations", "validate", "--snapshot", "bad.json");

        Assert.Equal(NodalCli.InvalidData, result.ExitCode);
        Assert.Contains(expected, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyAndUnresolvableSnapshotsReturnDataExitCode()
    {
        var empty = await RunAsync(Files(("empty.json", " ")), "migrations", "validate", "--snapshot", "empty.json");
        var current = After();
        var after = current with
        {
            Nodes =
            [
                current.Nodes[0] with
                {
                    Properties =
                    [
                        new NodalPropertySnapshot("custom", "Custom", "Missing.CustomType", true, false, []),
                        .. current.Nodes[0].Properties,
                    ],
                },
            ],
        };
        var files = Files(("before.json", Serialize(Before())), ("after.json", Serialize(after)));
        var unresolved = await RunAsync(
            files,
            "migrations", "plan", "--from", "before.json", "--to", "after.json");

        Assert.Equal(NodalCli.InvalidData, empty.ExitCode);
        Assert.Equal("A schema snapshot is empty or structurally invalid.", empty.Error.Trim());
        Assert.Equal(NodalCli.InvalidData, unresolved.ExitCode);
        Assert.Contains("Missing.CustomType", unresolved.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void TextRendererIncludesRenameAndDetailDescriptors()
    {
        var diff = new NodalSchemaDiffResult(
        [
            new NodalSchemaChange(
                NodalSchemaChangeKind.NodePropertyRenamed,
                "people",
                "name",
                "display_name",
                "explicit hint"),
        ]);

        var text = CliRenderers.RenderDiff(diff, CliOutputFormat.Text);

        Assert.Equal(
            $"NodePropertyRenamed: people.name -> display_name (explicit hint){Environment.NewLine}",
            text);
    }

    [Fact]
    public async Task BundleCommandCreatesImmutableArtifactAndListReadsIt()
    {
        var files = Files(("manifest.json", ManifestJson()));
        var bundled = await RunAsync(
            files,
            "migrations", "bundle", "--manifest", "manifest.json", "--output", "bundles/001.nodalbundle.json");
        var listed = await RunAsync(
            files,
            "migrations", "list", "--directory", "bundles");
        var json = await RunAsync(
            files,
            "migrations", "list", "--directory", "bundles", "--format", "json", "--output", "bundles.json");

        Assert.Equal(NodalCli.Success, bundled.ExitCode);
        Assert.Contains("Bundle written. Checksum:", bundled.Output, StringComparison.Ordinal);
        var bundle = NodalMigrationBundleSerializer.Deserialize(files.Content["bundles/001.nodalbundle.json"]);
        Assert.Equal("20260825_001_people", bundle.MigrationId);
        Assert.Contains("20260825_001_people Neo4j@5.26", listed.Output, StringComparison.Ordinal);
        Assert.Contains("destructive=false", listed.Output, StringComparison.Ordinal);
        Assert.Equal(string.Empty, json.Output);
        Assert.Contains("\"migrationId\": \"20260825_001_people\"", files.Content["bundles.json"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task BundleListRejectsChecksumDrift()
    {
        var bundle = NodalMigrationBundleSerializer.Create(Manifest());
        var validJson = NodalMigrationBundleSerializer.Serialize(bundle);
        var files = Files((
            "bundles/001.nodalbundle.json",
            validJson.Replace(bundle.Checksum, new string('0', 64), StringComparison.Ordinal)));

        var result = await RunAsync(files, "migrations", "list", "--directory", "bundles");

        Assert.Equal(NodalCli.InvalidData, result.ExitCode);
        Assert.Contains("failed checksum validation", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BundleListRejectsUnsupportedFormat()
    {
        var bundle = NodalMigrationBundleSerializer.Create(Manifest()) with { FormatVersion = 2 };
        var files = Files((
            "bundles/001.nodalbundle.json",
            JsonSerializer.Serialize(bundle, CamelCaseJson)));

        var result = await RunAsync(files, "migrations", "list", "--directory", "bundles");

        Assert.Equal(NodalCli.InvalidData, result.ExitCode);
        Assert.Contains("format version '2' is not supported", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyAndRollbackUseInjectedProviderCompositionBoundary()
    {
        var bundle = NodalMigrationBundleSerializer.Create(Manifest());
        var files = Files(("001.nodalbundle.json", NodalMigrationBundleSerializer.Serialize(bundle)));
        var host = new RecordingExecutionHost();

        var apply = await RunWithHostAsync(
            files,
            host,
            "migrations", "apply", "--bundle", "001.nodalbundle.json", "--dry-run", "true",
            "--approve-destructive", "true", "--format", "json");
        var rollback = await RunWithHostAsync(
            files,
            host,
            "migrations", "rollback", "--bundle", "001.nodalbundle.json", "--format", "github");

        Assert.Equal(NodalCli.Success, apply.ExitCode);
        Assert.Contains("\"outcome\": \"applyPlanned\"", apply.Output, StringComparison.Ordinal);
        Assert.True(host.ApplyOptions!.DryRun);
        Assert.True(host.ApplyOptions.AllowDestructiveOperations);
        Assert.Equal(NodalCli.Success, rollback.ExitCode);
        Assert.Contains("::notice title=Nodal migration execution::RevertPlanned", rollback.Output, StringComparison.Ordinal);
        Assert.False(host.RevertOptions!.DryRun);
    }

    [Theory]
    [InlineData("yes", "--dry-run")]
    [InlineData("1", "--approve-destructive")]
    public async Task ExecutionBooleanOptionsAreStrict(string value, string option)
    {
        var bundle = NodalMigrationBundleSerializer.Create(Manifest());
        var files = Files(("001.nodalbundle.json", NodalMigrationBundleSerializer.Serialize(bundle)));

        var result = await RunWithHostAsync(
            files,
            new RecordingExecutionHost(),
            "migrations", "apply", "--bundle", "001.nodalbundle.json", option, value);

        Assert.Equal(NodalCli.UsageError, result.ExitCode);
        Assert.Contains($"Option '{option}' must be 'true' or 'false'.", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecutionWithoutTrustedHostConfigurationFailsSafely()
    {
        var bundle = NodalMigrationBundleSerializer.Create(Manifest());
        var files = Files(("private-path.nodalbundle.json", NodalMigrationBundleSerializer.Serialize(bundle)));

        var result = await RunAsync(files, "migrations", "apply", "--bundle", "private-path.nodalbundle.json");

        Assert.Equal(NodalCli.InvalidData, result.ExitCode);
        Assert.Contains(CliMigrationExecutionHostLoader.AssemblyVariable, result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("private-path", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ExecutionHostLoaderValidatesConfigurationAndCreatesContract()
    {
        Assert.Throws<ArgumentNullException>(() => CliMigrationExecutionHostLoader.Load(null!));
        Assert.Throws<InvalidOperationException>(() => CliMigrationExecutionHostLoader.Load(_ => null));
        Assert.Throws<InvalidOperationException>(() => CliMigrationExecutionHostLoader.Load(name =>
            name == CliMigrationExecutionHostLoader.AssemblyVariable
                ? typeof(NodalCliTests).Assembly.Location
                : typeof(string).FullName));

        var host = CliMigrationExecutionHostLoader.Load(name =>
            name == CliMigrationExecutionHostLoader.AssemblyVariable
                ? typeof(NodalCliTests).Assembly.Location
                : typeof(PublicExecutionHost).FullName);

        Assert.IsType<PublicExecutionHost>(host);
    }

    [Fact]
    public async Task GitHubFormatProducesAnnotationsAndEscapesWorkflowData()
    {
        var files = Pair();
        var diff = await RunAsync(
            files,
            "migrations", "diff", "--from", "before.json", "--to", "after.json", "--format", "github");
        var emptyList = await RunAsync(
            files,
            "migrations", "list", "--directory", "missing", "--format", "github");
        var plan = new NodalSchemaMigrationPlan(
            [],
            [new NodalSchemaChange(NodalSchemaChangeKind.RelationShapeChanged, "line%1\nnext")]);

        var planOutput = CliRenderers.RenderPlan(plan, CliOutputFormat.GitHub);

        Assert.Contains("::notice title=Nodal schema diff::NodePropertyAdded: people.name", diff.Output, StringComparison.Ordinal);
        Assert.Equal("::notice title=Nodal migration bundles::No migration bundles.", emptyList.Output.Trim());
        Assert.Contains("::warning title=Nodal manual review::RelationShapeChanged: line%251%0Anext", planOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void BundleRenderersSupportEmptyTextAndGitHubItems()
    {
        Assert.Equal("No migration bundles.", CliRenderers.RenderBundleList([], CliOutputFormat.Text));
        Assert.Equal(
            "::notice title=Nodal migration bundles::No migration bundles.",
            CliRenderers.RenderBundleList([], CliOutputFormat.GitHub));
        Assert.Contains(
            "::notice title=Nodal schema diff::No schema changes.",
            CliRenderers.RenderDiff(new NodalSchemaDiffResult([]), CliOutputFormat.GitHub),
            StringComparison.Ordinal);
        Assert.Throws<ArgumentNullException>(() => CliRenderers.RenderPlan(null!, CliOutputFormat.Text));
        Assert.Throws<ArgumentNullException>(() => CliRenderers.RenderBundleList(null!, CliOutputFormat.Text));
    }

    [Fact]
    public async Task FileFailuresAndCancellationReturnStableExitCodesWithoutPaths()
    {
        var io = new MemoryFileSystem { ReadException = new IOException("secret-path") };
        var denied = new MemoryFileSystem { ReadException = new UnauthorizedAccessException("secret-path") };
        var cancelled = new MemoryFileSystem { ReadException = new OperationCanceledException() };

        var ioResult = await RunAsync(io, "migrations", "validate", "--snapshot", "secret-path");
        var deniedResult = await RunAsync(denied, "migrations", "validate", "--snapshot", "secret-path");
        var cancelledResult = await RunAsync(cancelled, "migrations", "validate", "--snapshot", "secret-path");

        Assert.Equal(NodalCli.FileError, ioResult.ExitCode);
        Assert.Equal("A migration file could not be read or written.", ioResult.Error.Trim());
        Assert.Equal(NodalCli.FileError, deniedResult.ExitCode);
        Assert.Equal("Access to a migration file was denied.", deniedResult.Error.Trim());
        Assert.Equal(NodalCli.Cancelled, cancelledResult.ExitCode);
        Assert.Equal("The migration operation was cancelled.", cancelledResult.Error.Trim());
    }

    [Fact]
    public async Task SnapshotWriteFailureUsesFileExitCode()
    {
        var files = Files(("model.json", Serialize(Before())));
        files.WriteException = new IOException();

        var result = await RunAsync(files, "migrations", "snapshot", "--input", "model.json", "--output", "out.json");

        Assert.Equal(NodalCli.FileError, result.ExitCode);
    }

    [Fact]
    public async Task PhysicalFileSystemReadsAndWritesUtf8()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"nodal-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "001.nodalbundle.json");
        try
        {
            Directory.CreateDirectory(directory);
            await PhysicalCliFileSystem.Instance.WriteAllTextAsync(path, "graph-✓", CancellationToken.None);
            await PhysicalCliFileSystem.Instance.WriteAllTextAsync(
                Path.Combine(directory, "ignored.json"), "{}", CancellationToken.None);
            var content = await PhysicalCliFileSystem.Instance.ReadAllTextAsync(path, CancellationToken.None);
            var bundles = PhysicalCliFileSystem.Instance.EnumerateFiles(directory, "*.nodalbundle.json");
            Assert.Equal("graph-✓", content);
            Assert.Equal([path], bundles);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task PublicEntryGuardsDependencies()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            NodalCli.RunAsync(null!, TextWriter.Null, TextWriter.Null, Pair(), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            NodalCli.RunAsync([], null!, TextWriter.Null, Pair(), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            NodalCli.RunAsync([], TextWriter.Null, null!, Pair(), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            NodalCli.RunAsync([], TextWriter.Null, TextWriter.Null, null!, CancellationToken.None));
        Assert.Throws<ArgumentNullException>(() => CliArguments.Parse(null!));
        Assert.Throws<ArgumentNullException>(() => CliRenderers.RenderDiff(null!, CliOutputFormat.Text));
        Assert.Throws<ArgumentNullException>(() => CliRenderers.RenderBundleExecution(null!, CliOutputFormat.Text));
    }

    private static async Task<CliResult> RunAsync(MemoryFileSystem files, params string[] arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await NodalCli.RunAsync(arguments, output, error, files, CancellationToken.None);
        return new CliResult(exitCode, output.ToString(), error.ToString());
    }

    private static async Task<CliResult> RunWithHostAsync(
        MemoryFileSystem files,
        INodalMigrationBundleExecutionHost host,
        params string[] arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await NodalCli.RunAsync(
            arguments,
            output,
            error,
            files,
            host,
            CancellationToken.None);
        return new CliResult(exitCode, output.ToString(), error.ToString());
    }

    private static MemoryFileSystem Pair() => Files(
        ("before.json", Serialize(Before())),
        ("after.json", Serialize(After())));

    private static MemoryFileSystem Files(params (string Path, string Content)[] files) =>
        new(files.ToDictionary(file => file.Path, file => file.Content, StringComparer.Ordinal));

    private static string Serialize(NodalSchemaSnapshot snapshot) =>
        NodalSchemaSnapshotSerializer.Serialize(snapshot);

    private static NodalSchemaSnapshot Before() => new(
        NodalSchemaSnapshot.CurrentFormatVersion,
        [new NodalNodeSnapshot(
            "people",
            "Person",
            "id",
            [new NodalPropertySnapshot("id", "Id", "System.String", false, false, [])])],
        []);

    private static NodalSchemaSnapshot After() => Before() with
    {
        Nodes =
        [
            new NodalNodeSnapshot(
                "people",
                "Person",
                "id",
                [
                    new NodalPropertySnapshot("name", "Name", "System.String", true, false, []),
                    new NodalPropertySnapshot("id", "Id", "System.String", false, false, []),
                ]),
        ],
    };

    private static string ManifestJson() => JsonSerializer.Serialize(Manifest(), CamelCaseJson);

    private static NodalMigrationBundleManifest Manifest() => new(
        "20260825_001_people",
        "Neo4j",
        "5.26",
        "0.1.0-alpha.1",
        ["SchemaWrite"],
        [new NodalMigrationBundleCommand("create-index", "CREATE INDEX people_name", true, false)]);

    private sealed record CliResult(int ExitCode, string Output, string Error);

    public sealed class PublicExecutionHost : INodalMigrationBundleExecutionHost
    {
        public ValueTask<NodalMigrationBundleExecutionResult> ApplyAsync(
            NodalMigrationBundle bundle,
            NodalMigrationBundleExecutionOptions? options = null,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result(bundle, NodalMigrationBundleExecutionOutcome.ApplyPlanned));

        public ValueTask<NodalMigrationBundleExecutionResult> RevertAsync(
            NodalMigrationBundle bundle,
            NodalMigrationBundleExecutionOptions? options = null,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result(bundle, NodalMigrationBundleExecutionOutcome.RevertPlanned));
    }

    private sealed class RecordingExecutionHost : INodalMigrationBundleExecutionHost
    {
        public NodalMigrationBundleExecutionOptions? ApplyOptions { get; private set; }

        public NodalMigrationBundleExecutionOptions? RevertOptions { get; private set; }

        public ValueTask<NodalMigrationBundleExecutionResult> ApplyAsync(
            NodalMigrationBundle bundle,
            NodalMigrationBundleExecutionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ApplyOptions = options;
            return ValueTask.FromResult(Result(bundle, NodalMigrationBundleExecutionOutcome.ApplyPlanned));
        }

        public ValueTask<NodalMigrationBundleExecutionResult> RevertAsync(
            NodalMigrationBundle bundle,
            NodalMigrationBundleExecutionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            RevertOptions = options;
            return ValueTask.FromResult(Result(bundle, NodalMigrationBundleExecutionOutcome.RevertPlanned));
        }
    }

    private static NodalMigrationBundleExecutionResult Result(
        NodalMigrationBundle bundle,
        NodalMigrationBundleExecutionOutcome outcome) =>
        new(bundle.MigrationId, bundle.Checksum, outcome, bundle.Commands.Count);

    private sealed class MemoryFileSystem : ICliFileSystem
    {
        public MemoryFileSystem(Dictionary<string, string>? content = null)
        {
            Content = content ?? new(StringComparer.Ordinal);
        }

        public Dictionary<string, string> Content { get; }

        public Exception? ReadException { get; init; }

        public Exception? WriteException { get; set; }

        public ValueTask<string> ReadAllTextAsync(string path, CancellationToken cancellationToken)
        {
            if (ReadException is not null)
            {
                return ValueTask.FromException<string>(ReadException);
            }

            return ValueTask.FromResult(Content[path]);
        }

        public ValueTask WriteAllTextAsync(string path, string content, CancellationToken cancellationToken)
        {
            if (WriteException is not null)
            {
                return ValueTask.FromException(WriteException);
            }

            Content[path] = content;
            return ValueTask.CompletedTask;
        }

        public IReadOnlyList<string> EnumerateFiles(string directory, string searchPattern)
        {
            var prefix = string.Concat(directory.TrimEnd('/', '\\'), "/");
            return Content.Keys
                .Where(path => path.Replace('\\', '/').StartsWith(prefix, StringComparison.Ordinal))
                .Where(path => path.EndsWith(".nodalbundle.json", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)
                .ToArray();
        }
    }
}
