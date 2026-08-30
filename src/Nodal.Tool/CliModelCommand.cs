using System.Text.Json;
using System.Text.Json.Serialization;
using Nodal.Core.Modeling;
using Nodal.Modeling.CodeGeneration;

namespace Nodal.Tool;

internal static class CliModelCommand
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static async Task<int> RunAsync(
        CliArguments request,
        TextWriter output,
        ICliFileSystem fileSystem,
        CancellationToken cancellationToken) => request.Command switch
        {
            "generate" => await GenerateAsync(request, output, fileSystem, cancellationToken).ConfigureAwait(false),
            "validate" => await ValidateAsync(request, output, fileSystem, cancellationToken).ConfigureAwait(false),
            "inspect" => await InspectAsync(request, output, fileSystem, cancellationToken).ConfigureAwait(false),
            "diff" => await DiffAsync(request, output, fileSystem, cancellationToken).ConfigureAwait(false),
            _ => throw new CliUsageException($"Unknown model command '{request.Command}'."),
        };

    private static async Task<int> GenerateAsync(
        CliArguments request,
        TextWriter output,
        ICliFileSystem fileSystem,
        CancellationToken cancellationToken)
    {
        request.EnsureOnly("--descriptor", "--output", "--namespace", "--context");
        var descriptor = await ReadAsync(request.Require("--descriptor"), fileSystem, cancellationToken)
            .ConfigureAwait(false);
        var generated = GraphModelCodeGenerator.Generate(descriptor, new GraphModelGeneratorOptions
        {
            RootNamespace = request.Optional("--namespace", "Nodal.Generated"),
            ContextName = request.Optional("--context", "GeneratedGraphContext"),
        });
        var destination = request.Require("--output").TrimEnd('/', '\\');
        if (destination.Length == 0)
        {
            throw new CliUsageException("Option '--output' requires a non-empty directory.");
        }

        fileSystem.CreateDirectory(destination);
        foreach (var file in generated)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Combine(destination, file.RelativePath);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                fileSystem.CreateDirectory(directory);
            }

            await fileSystem.WriteAllTextAsync(path, file.Content, cancellationToken).ConfigureAwait(false);
        }

        await output.WriteLineAsync(
            $"Generated {generated.Count} files. Fingerprint: {GraphModelDescriptorJson.ComputeFingerprint(descriptor)}")
            .ConfigureAwait(false);
        return NodalCli.Success;
    }

    private static async Task<int> ValidateAsync(
        CliArguments request,
        TextWriter output,
        ICliFileSystem fileSystem,
        CancellationToken cancellationToken)
    {
        request.EnsureOnly("--descriptor", "--format", "--output");
        var descriptor = await ReadUnvalidatedAsync(request.Require("--descriptor"), fileSystem, cancellationToken)
            .ConfigureAwait(false);
        var result = GraphModelValidation.Validate(descriptor);
        await WriteAsync(Render(result, request.Optional("--format", "text")), request, output, fileSystem, cancellationToken)
            .ConfigureAwait(false);
        result.ThrowIfInvalid();
        return NodalCli.Success;
    }

    private static async Task<int> InspectAsync(
        CliArguments request,
        TextWriter output,
        ICliFileSystem fileSystem,
        CancellationToken cancellationToken)
    {
        request.EnsureOnly("--descriptor", "--format", "--output");
        var descriptor = await ReadAsync(request.Require("--descriptor"), fileSystem, cancellationToken)
            .ConfigureAwait(false);
        var inspection = GraphModelInspector.Inspect(descriptor);
        var format = request.Optional("--format", "text");
        var content = format switch
        {
            "text" => $"format={inspection.FormatVersion} fingerprint={inspection.Fingerprint} nodes={inspection.NodeCount} " +
                $"relations={inspection.RelationCount} properties={inspection.PropertyCount} " +
                $"compositeKeys={inspection.CompositeKeyCount} reviews={inspection.ReviewCount}",
            "json" => JsonSerializer.Serialize(inspection, JsonOptions),
            _ => throw FormatUsage(),
        };
        await WriteAsync(content, request, output, fileSystem, cancellationToken).ConfigureAwait(false);
        return NodalCli.Success;
    }

    private static async Task<int> DiffAsync(
        CliArguments request,
        TextWriter output,
        ICliFileSystem fileSystem,
        CancellationToken cancellationToken)
    {
        request.EnsureOnly("--from", "--to", "--format", "--output", "--fail-on-breaking");
        var failOnBreaking = ParseBoolean(request.Optional("--fail-on-breaking", "false"));
        var before = await ReadAsync(request.Require("--from"), fileSystem, cancellationToken).ConfigureAwait(false);
        var after = await ReadAsync(request.Require("--to"), fileSystem, cancellationToken).ConfigureAwait(false);
        var diff = GraphModelDiffer.Compare(before, after);
        var format = request.Optional("--format", "text");
        var content = format switch
        {
            "text" => diff.IsEmpty
                ? "No model changes."
                : string.Join(Environment.NewLine, diff.Changes.Select(change =>
                    $"{change.Impact}: {change.Kind} {change.Path} - {change.Message}")),
            "json" => JsonSerializer.Serialize(diff, JsonOptions),
            _ => throw FormatUsage(),
        };
        await WriteAsync(content, request, output, fileSystem, cancellationToken).ConfigureAwait(false);
        if (diff.HasBreakingChanges && failOnBreaking)
        {
            throw new GraphModelValidationException(new GraphModelValidationResult(
                [new GraphModelValidationIssue(
                    "NODAL-MODEL-BREAKING",
                    GraphModelIssueSeverity.Error,
                    "The descriptor diff contains breaking changes.",
                    "$")]));
        }

        return NodalCli.Success;
    }

    private static async ValueTask<GraphModelDescriptor> ReadAsync(
        string path,
        ICliFileSystem fileSystem,
        CancellationToken cancellationToken)
    {
        var descriptor = await ReadUnvalidatedAsync(path, fileSystem, cancellationToken).ConfigureAwait(false);
        GraphModelValidation.Validate(descriptor).ThrowIfInvalid();
        return descriptor;
    }

    private static async ValueTask<GraphModelDescriptor> ReadUnvalidatedAsync(
        string path,
        ICliFileSystem fileSystem,
        CancellationToken cancellationToken)
    {
        var json = await fileSystem.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<GraphModelDescriptor>(json, JsonOptions)
            ?? throw new JsonException("The graph model descriptor document is empty.");
    }

    private static string Render(GraphModelValidationResult result, string format) => format switch
    {
        "text" when result.Issues.Count == 0 => "Valid graph model descriptor.",
        "text" => string.Join(Environment.NewLine, result.Issues.Select(issue =>
            $"{issue.Severity}: [{issue.Code}] {issue.Path} - {issue.Message}")),
        "json" => JsonSerializer.Serialize(result, JsonOptions),
        _ => throw FormatUsage(),
    };

    private static async ValueTask WriteAsync(
        string content,
        CliArguments request,
        TextWriter output,
        ICliFileSystem fileSystem,
        CancellationToken cancellationToken)
    {
        var normalized = string.Concat(content.TrimEnd(), Environment.NewLine);
        if (request.Options.TryGetValue("--output", out var path))
        {
            await fileSystem.WriteAllTextAsync(path, normalized, cancellationToken).ConfigureAwait(false);
            return;
        }

        await output.WriteAsync(normalized).ConfigureAwait(false);
    }

    private static bool ParseBoolean(string value) => value switch
    {
        "true" => true,
        "false" => false,
        _ => throw new CliUsageException("Option '--fail-on-breaking' must be 'true' or 'false'."),
    };

    private static CliUsageException FormatUsage() =>
        new("Option '--format' must be 'text' or 'json'.");

    private static string Combine(string directory, string relativePath) =>
        string.Concat(directory, "/", relativePath).Replace('\\', '/');

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
