namespace WorldFoodDelivery.Application;

internal sealed record FoodDeliveryScenarioResult(
    int CsvRows,
    int AffectedNodes,
    int AffectedRelations,
    int RelationalObjects,
    int RelationalRelations);
