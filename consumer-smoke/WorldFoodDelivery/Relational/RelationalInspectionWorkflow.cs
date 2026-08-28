using Nodal.Import.Relational;

namespace WorldFoodDelivery.Relational;

internal sealed class RelationalInspectionWorkflow(IRelationalInspectionHost host)
{
    public async ValueTask<RelationalInspectionResult> RunAsync(
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var snapshot = await host.InspectAsync(cancellationToken);
        var model = RelationalInteractionModelBuilder.Build(snapshot, host.ProviderName);
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "world-food-delivery.nodalmodel.json"),
            RelationalInteractionModelJson.Serialize(model),
            cancellationToken);
        Write(model, RelationalInteractionExportFormat.GraphMl, outputDirectory, "world-food-delivery.graphml");
        Write(model, RelationalInteractionExportFormat.Gexf, outputDirectory, "world-food-delivery.gexf");
        Write(model, RelationalInteractionExportFormat.Dot, outputDirectory, "world-food-delivery.dot");

        return new RelationalInspectionResult(model.Objects.Count, model.Relations.Count, model.Source.SchemaFingerprint);
    }

    private static void Write(
        RelationalInteractionModel model,
        RelationalInteractionExportFormat format,
        string directory,
        string fileName)
    {
        using var writer = File.CreateText(Path.Combine(directory, fileName));
        RelationalInteractionModelExporter.Write(model, format, writer);
    }
}
