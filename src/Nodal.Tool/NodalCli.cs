using System.Text.Json;
using Nodal.Core.Migrations;
using Nodal.Migrations;

namespace Nodal.Tool;

internal static class NodalCli
{
    internal const int Success = 0;
    internal const int UsageError = 2;
    internal const int InvalidData = 3;
    internal const int FileError = 4;
    internal const int Cancelled = 130;

    public static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error,
        ICliFileSystem fileSystem,
        CancellationToken cancellationToken)
        => await RunAsync(
            arguments,
            output,
            error,
            fileSystem,
            executionHost: null,
            cancellationToken).ConfigureAwait(false);

    internal static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error,
        ICliFileSystem fileSystem,
        INodalMigrationBundleExecutionHost? executionHost,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(fileSystem);

        try
        {
            var request = CliArguments.Parse(arguments);
            return request.Command switch
            {
                "snapshot" => await SnapshotAsync(request, output, fileSystem, cancellationToken)
                    .ConfigureAwait(false),
                "diff" => await DiffAsync(request, output, fileSystem, cancellationToken)
                    .ConfigureAwait(false),
                "plan" => await PlanAsync(request, output, fileSystem, cancellationToken)
                    .ConfigureAwait(false),
                "validate" => await ValidateAsync(request, output, fileSystem, cancellationToken)
                    .ConfigureAwait(false),
                "bundle" => await BundleAsync(request, output, fileSystem, cancellationToken)
                    .ConfigureAwait(false),
                "list" => await ListAsync(request, output, fileSystem, cancellationToken)
                    .ConfigureAwait(false),
                "apply" => await ExecuteBundleAsync(
                    request, output, fileSystem, executionHost, revert: false, cancellationToken)
                    .ConfigureAwait(false),
                "rollback" => await ExecuteBundleAsync(
                    request, output, fileSystem, executionHost, revert: true, cancellationToken)
                    .ConfigureAwait(false),
                _ => throw new CliUsageException($"Unknown migration command '{request.Command}'."),
            };
        }
        catch (CliUsageException exception)
        {
            await error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            await error.WriteLineAsync(Usage).ConfigureAwait(false);
            return UsageError;
        }
        catch (OperationCanceledException)
        {
            await error.WriteLineAsync("The migration operation was cancelled.").ConfigureAwait(false);
            return Cancelled;
        }
        catch (JsonException)
        {
            await error.WriteLineAsync("A schema snapshot contains invalid JSON.").ConfigureAwait(false);
            return InvalidData;
        }
        catch (NodalSchemaSnapshotVersionException exception)
        {
            await error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            return InvalidData;
        }
        catch (InvalidOperationException exception)
        {
            await error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            return InvalidData;
        }
        catch (ArgumentException)
        {
            await error.WriteLineAsync("A schema snapshot is empty or structurally invalid.").ConfigureAwait(false);
            return InvalidData;
        }
        catch (NodalMigrationBundleException exception)
        {
            await error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            return InvalidData;
        }
        catch (NotSupportedException exception)
        {
            await error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            return InvalidData;
        }
        catch (IOException)
        {
            await error.WriteLineAsync("A migration file could not be read or written.").ConfigureAwait(false);
            return FileError;
        }
        catch (UnauthorizedAccessException)
        {
            await error.WriteLineAsync("Access to a migration file was denied.").ConfigureAwait(false);
            return FileError;
        }
    }

    private const string Usage =
        "Usage: nodal migrations <snapshot|diff|plan|validate|bundle|list|apply|rollback> [--name value]";

    private static async Task<int> SnapshotAsync(
        CliArguments request,
        TextWriter output,
        ICliFileSystem fileSystem,
        CancellationToken cancellationToken)
    {
        request.EnsureOnly("--input", "--output");
        var snapshot = await ReadSnapshotAsync(request.Require("--input"), fileSystem, cancellationToken)
            .ConfigureAwait(false);
        var destination = request.Require("--output");
        await fileSystem.WriteAllTextAsync(
            destination,
            string.Concat(NodalSchemaSnapshotSerializer.Serialize(snapshot), Environment.NewLine),
            cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync($"Snapshot written. Hash: {NodalSchemaSnapshotSerializer.ComputeHash(snapshot)}")
            .ConfigureAwait(false);
        return Success;
    }

    private static async Task<int> ValidateAsync(
        CliArguments request,
        TextWriter output,
        ICliFileSystem fileSystem,
        CancellationToken cancellationToken)
    {
        request.EnsureOnly("--snapshot");
        var snapshot = await ReadSnapshotAsync(request.Require("--snapshot"), fileSystem, cancellationToken)
            .ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Valid snapshot v{snapshot.FormatVersion}. Hash: {NodalSchemaSnapshotSerializer.ComputeHash(snapshot)}")
            .ConfigureAwait(false);
        return Success;
    }

    private static async Task<int> DiffAsync(
        CliArguments request,
        TextWriter output,
        ICliFileSystem fileSystem,
        CancellationToken cancellationToken)
    {
        request.EnsureOnly("--from", "--to", "--format", "--output");
        var format = ParseFormat(request);
        var (before, after) = await ReadPairAsync(request, fileSystem, cancellationToken).ConfigureAwait(false);
        var diff = NodalSchemaDiffer.Compare(before, after);
        var content = CliRenderers.RenderDiff(diff, format);
        await WriteResultAsync(content, request, output, fileSystem, cancellationToken).ConfigureAwait(false);
        return Success;
    }

    private static async Task<int> PlanAsync(
        CliArguments request,
        TextWriter output,
        ICliFileSystem fileSystem,
        CancellationToken cancellationToken)
    {
        request.EnsureOnly("--from", "--to", "--format", "--output");
        var format = ParseFormat(request);
        var (before, after) = await ReadPairAsync(request, fileSystem, cancellationToken).ConfigureAwait(false);
        var plan = NodalSchemaMigrationMapper.Map(before, after);
        var content = CliRenderers.RenderPlan(plan, format);
        await WriteResultAsync(content, request, output, fileSystem, cancellationToken).ConfigureAwait(false);
        return Success;
    }

    private static async Task<int> BundleAsync(
        CliArguments request,
        TextWriter output,
        ICliFileSystem fileSystem,
        CancellationToken cancellationToken)
    {
        request.EnsureOnly("--manifest", "--output");
        var manifestJson = await fileSystem.ReadAllTextAsync(
            request.Require("--manifest"), cancellationToken).ConfigureAwait(false);
        var manifest = NodalMigrationBundleSerializer.DeserializeManifest(manifestJson);
        var bundle = NodalMigrationBundleSerializer.Create(manifest);
        await fileSystem.WriteAllTextAsync(
            request.Require("--output"),
            string.Concat(NodalMigrationBundleSerializer.Serialize(bundle), Environment.NewLine),
            cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync($"Bundle written. Checksum: {bundle.Checksum}").ConfigureAwait(false);
        return Success;
    }

    private static async Task<int> ListAsync(
        CliArguments request,
        TextWriter output,
        ICliFileSystem fileSystem,
        CancellationToken cancellationToken)
    {
        request.EnsureOnly("--directory", "--format", "--output");
        var format = ParseFormat(request);
        var paths = fileSystem.EnumerateFiles(request.Require("--directory"), "*.nodalbundle.json");
        var bundles = new List<NodalMigrationBundle>(paths.Count);
        foreach (var path in paths)
        {
            var json = await fileSystem.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            bundles.Add(NodalMigrationBundleSerializer.Deserialize(json));
        }

        var content = CliRenderers.RenderBundleList(bundles, format);
        await WriteResultAsync(content, request, output, fileSystem, cancellationToken).ConfigureAwait(false);
        return Success;
    }

    private static async Task<int> ExecuteBundleAsync(
        CliArguments request,
        TextWriter output,
        ICliFileSystem fileSystem,
        INodalMigrationBundleExecutionHost? executionHost,
        bool revert,
        CancellationToken cancellationToken)
    {
        request.EnsureOnly("--bundle", "--dry-run", "--approve-destructive", "--format", "--output");
        var format = ParseFormat(request);
        var options = new NodalMigrationBundleExecutionOptions
        {
            DryRun = ParseBoolean(request, "--dry-run"),
            AllowDestructiveOperations = ParseBoolean(request, "--approve-destructive"),
        };
        var json = await fileSystem.ReadAllTextAsync(request.Require("--bundle"), cancellationToken)
            .ConfigureAwait(false);
        var bundle = NodalMigrationBundleSerializer.Deserialize(json);
        var ownsHost = executionHost is null;
        var host = executionHost ?? CliMigrationExecutionHostLoader.LoadFromEnvironment();
        NodalMigrationBundleExecutionResult result;
        try
        {
            result = revert
                ? await host.RevertAsync(bundle, options, cancellationToken).ConfigureAwait(false)
                : await host.ApplyAsync(bundle, options, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (ownsHost && host is IAsyncDisposable disposable)
            {
                await disposable.DisposeAsync().ConfigureAwait(false);
            }
        }
        await WriteResultAsync(
            CliRenderers.RenderBundleExecution(result, format),
            request,
            output,
            fileSystem,
            cancellationToken).ConfigureAwait(false);
        return Success;
    }

    private static async ValueTask<(NodalSchemaSnapshot Before, NodalSchemaSnapshot After)> ReadPairAsync(
        CliArguments request,
        ICliFileSystem fileSystem,
        CancellationToken cancellationToken)
    {
        var before = await ReadSnapshotAsync(request.Require("--from"), fileSystem, cancellationToken)
            .ConfigureAwait(false);
        var after = await ReadSnapshotAsync(request.Require("--to"), fileSystem, cancellationToken)
            .ConfigureAwait(false);
        return (before, after);
    }

    private static async ValueTask<NodalSchemaSnapshot> ReadSnapshotAsync(
        string path,
        ICliFileSystem fileSystem,
        CancellationToken cancellationToken)
    {
        var json = await fileSystem.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return NodalSchemaSnapshotSerializer.Deserialize(json);
    }

    private static CliOutputFormat ParseFormat(CliArguments request)
    {
        var value = request.Optional("--format", "text");
        return value switch
        {
            "text" => CliOutputFormat.Text,
            "json" => CliOutputFormat.Json,
            "github" => CliOutputFormat.GitHub,
            _ => throw new CliUsageException("Option '--format' must be 'text', 'json', or 'github'."),
        };
    }

    private static bool ParseBoolean(CliArguments request, string name)
    {
        var value = request.Optional(name, "false");
        return value switch
        {
            "true" => true,
            "false" => false,
            _ => throw new CliUsageException($"Option '{name}' must be 'true' or 'false'."),
        };
    }

    private static async ValueTask WriteResultAsync(
        string content,
        CliArguments request,
        TextWriter output,
        ICliFileSystem fileSystem,
        CancellationToken cancellationToken)
    {
        var normalized = content.EndsWith(Environment.NewLine, StringComparison.Ordinal)
            ? content
            : string.Concat(content, Environment.NewLine);
        if (request.Options.TryGetValue("--output", out var destination))
        {
            await fileSystem.WriteAllTextAsync(destination, normalized, cancellationToken).ConfigureAwait(false);
            return;
        }

        await output.WriteAsync(normalized).ConfigureAwait(false);
    }
}
