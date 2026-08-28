using WorldFoodDelivery.Persistence;
using WorldFoodDelivery.Relational;
using WorldFoodDelivery.Verification;

namespace WorldFoodDelivery.Application;

internal sealed class FoodDeliveryScenario(
    FoodDeliveryContext context,
    FoodOrderCsvReader csvReader,
    FoodOrderImporter importer,
    GraphScenarioVerifier graphVerifier,
    RelationalInspectionWorkflow relationalWorkflow,
    RelationalScenarioVerifier relationalVerifier)
{
    private const int ExpectedNodes = 17;
    private const int ExpectedRelations = 23;

    public async ValueTask<FoodDeliveryScenarioResult> RunAsync(
        string csvPath,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        var rows = await csvReader.ReadAsync(csvPath, cancellationToken);
        importer.Import(context, rows);

        var saved = await context.SaveChangesAsync(cancellationToken);
        if (saved.AffectedNodes != ExpectedNodes ||
            saved.AffectedRelations != ExpectedRelations ||
            !saved.IsAtomic)
        {
            throw new InvalidOperationException(
                $"CSV import produced {saved.AffectedNodes} nodes and {saved.AffectedRelations} relations; " +
                $"expected {ExpectedNodes} nodes and {ExpectedRelations} relations in one atomic batch.");
        }

        graphVerifier.Verify(context);
        var relational = await relationalWorkflow.RunAsync(outputDirectory, cancellationToken);
        relationalVerifier.Verify(outputDirectory, relational);
        return new FoodDeliveryScenarioResult(
            rows.Count,
            saved.AffectedNodes,
            saved.AffectedRelations,
            relational.Objects,
            relational.Relations);
    }
}
