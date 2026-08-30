using System.Globalization;
using Nodal.Core.Modeling;
using Nodal.Import.Relational;

namespace Nodal.Tool;

internal static class CliRelationalInspectionCommand
{
    public static async Task<int> RunAsync(
        CliArguments request,
        TextWriter output,
        ICliFileSystem fileSystem,
        IRelationalInspectionHost? inspectionHost,
        CancellationToken cancellationToken)
    {
        request.EnsureOnly("--output", "--descriptor", "--graphml", "--gexf", "--dot");
        var destination = request.Require("--output");
        ValidateDestinations(request, destination);
        var ownsHost = inspectionHost is null;
        var host = inspectionHost ?? CliRelationalInspectionHostLoader.LoadFromEnvironment();
        try
        {
            var snapshot = await host.InspectAsync(cancellationToken).ConfigureAwait(false);
            var model = RelationalInteractionModelBuilder.Build(snapshot, host.ProviderName);
            await fileSystem.WriteAllTextAsync(
                destination,
                string.Concat(RelationalInteractionModelJson.Serialize(model), Environment.NewLine),
                cancellationToken).ConfigureAwait(false);

            if (request.Options.TryGetValue("--descriptor", out var descriptorDestination))
            {
                var descriptor = RelationalGraphModelDescriptorBuilder.Build(model);
                await fileSystem.WriteAllTextAsync(
                    descriptorDestination,
                    string.Concat(GraphModelDescriptorJson.Serialize(descriptor), Environment.NewLine),
                    cancellationToken).ConfigureAwait(false);
            }

            var exportCount = 0;
            exportCount += await WriteExportAsync(
                request, "--graphml", RelationalInteractionExportFormat.GraphMl, model, fileSystem, cancellationToken)
                .ConfigureAwait(false);
            exportCount += await WriteExportAsync(
                request, "--gexf", RelationalInteractionExportFormat.Gexf, model, fileSystem, cancellationToken)
                .ConfigureAwait(false);
            exportCount += await WriteExportAsync(
                request, "--dot", RelationalInteractionExportFormat.Dot, model, fileSystem, cancellationToken)
                .ConfigureAwait(false);

            await output.WriteLineAsync(
                $"Relational interaction model written: provider={Display(host.ProviderName)} " +
                $"database={Display(snapshot.DatabaseName)} objects={model.Objects.Count} " +
                $"relations={model.Relations.Count} exports={exportCount} " +
                $"fingerprint={model.Source.SchemaFingerprint}").ConfigureAwait(false);
            return NodalCli.Success;
        }
        finally
        {
            if (ownsHost && host is IAsyncDisposable disposable)
            {
                await disposable.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static async ValueTask<int> WriteExportAsync(
        CliArguments request,
        string option,
        RelationalInteractionExportFormat format,
        RelationalInteractionModel model,
        ICliFileSystem fileSystem,
        CancellationToken cancellationToken)
    {
        if (!request.Options.TryGetValue(option, out var path) || string.IsNullOrWhiteSpace(path))
        {
            return 0;
        }

        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        RelationalInteractionModelExporter.Write(model, format, writer);
        await fileSystem.WriteAllTextAsync(path, writer.ToString(), cancellationToken).ConfigureAwait(false);
        return 1;
    }

    private static string Display(string? value) => string.IsNullOrWhiteSpace(value) ? "unknown" : value;

    private static void ValidateDestinations(CliArguments request, string canonicalDestination)
    {
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            canonicalDestination,
        };
        foreach (var option in new[] { "--descriptor", "--graphml", "--gexf", "--dot" })
        {
            if (!request.Options.TryGetValue(option, out var path))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                throw new CliUsageException($"Option '{option}' requires a non-empty destination.");
            }

            if (!destinations.Add(path))
            {
                throw new CliUsageException("Relational inspection output destinations must be distinct.");
            }
        }
    }
}
