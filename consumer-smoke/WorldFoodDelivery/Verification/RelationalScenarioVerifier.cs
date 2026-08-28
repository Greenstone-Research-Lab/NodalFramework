using System.Text.Json;
using Nodal.Import.Relational;
using WorldFoodDelivery.Relational;

namespace WorldFoodDelivery.Verification;

internal sealed class RelationalScenarioVerifier
{
    public void Verify(string outputDirectory, RelationalInspectionResult result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(result);
        Ensure(result.Objects == 9, "The relational inspection did not retain all normalized tables.");
        Ensure(result.Relations == 9, "The relational inspection did not retain all foreign keys.");
        Ensure(result.Fingerprint.Length == 64, "The relational schema fingerprint is not a SHA-256 value.");

        var jsonPath = Path.Combine(outputDirectory, "world-food-delivery.nodalmodel.json");
        var model = RelationalInteractionModelJson.Deserialize(File.ReadAllText(jsonPath));
        Ensure(model.Objects.Count(item => item.Role == RelationalInteractionObjectRole.Association) == 2,
            "OrderLines and RestaurantFoods must be recognized as association tables.");
        Ensure(model.Relations.All(relation => relation.Display.RequiresReview),
            "Structural relation labels must remain explicit review suggestions.");

        using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));
        Ensure(document.RootElement.GetProperty("formatVersion").GetString() == RelationalInteractionFormat.CurrentVersion,
            "The canonical interaction model format is unexpected.");
        Ensure(File.ReadAllText(Path.Combine(outputDirectory, "world-food-delivery.graphml"), System.Text.Encoding.UTF8)
            .Contains("graphml", StringComparison.Ordinal), "GraphML export was not written.");
        Ensure(File.ReadAllText(Path.Combine(outputDirectory, "world-food-delivery.gexf"), System.Text.Encoding.UTF8)
            .Contains("gexf", StringComparison.Ordinal), "GEXF export was not written.");
        Ensure(File.ReadAllText(Path.Combine(outputDirectory, "world-food-delivery.dot"), System.Text.Encoding.UTF8)
            .Contains("digraph RelationalInteractionNetwork", StringComparison.Ordinal), "DOT export was not written.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
