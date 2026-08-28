using Nodal.Migrations;
using Nodal.Neo4j;
using Nodal.TigerGraph;
using WorldFoodDelivery.Domain.Enums;
using WorldFoodDelivery.Persistence;

namespace WorldFoodDelivery.Verification;

internal sealed class GraphScenarioVerifier
{
    public void Verify(FoodDeliveryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var asianFoodOrders = context.Customers.Query("customer")
            .Traverse(context.PlacedOrders, "placed", "order")
            .Traverse(context.ContainsFoods, "contains", "food")
            .Where(food => food.Kitchen == Kitchen.Asian)
            .ToQueryModel();
        var restaurantSummary = context.Restaurants.Query("restaurant")
            .Traverse(context.ServesFoods, "serves", "food")
            .ToRows()
            .Count("orderCount")
            .Average("averagePrice", food => food.Price)
            .ToQueryModel();
        var unavailableRestaurant = context.Restaurants.Query("restaurant")
            .WhereNotExists(context.ServesFoods, food => food.Price > 100m)
            .ToQueryModel();

        var neo4j = new Neo4jQueryCompiler();
        Ensure(neo4j.Compile(asianFoodOrders).Text.Contains("MATCH", StringComparison.Ordinal),
            "Neo4j did not compile the portable traversal.");
        Ensure(neo4j.Compile(restaurantSummary).Text.Length > 0,
            "Neo4j did not compile the aggregate projection.");

        var tigerGraph = new TigerGraphQueryCompiler("WorldFoodDelivery");
        Ensure(tigerGraph.Compile(asianFoodOrders).Text.Length > 0,
            "TigerGraph did not compile the portable traversal.");
        Ensure(ThrowsNotSupported(() => tigerGraph.Compile(unavailableRestaurant)),
            "TigerGraph must reject correlated subqueries before transport.");

        _ = new MigrationPlanner(new Neo4jMigrationDialect()).PlanUp(new FoodDeliveryMigration());
    }

    private static bool ThrowsNotSupported(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (NotSupportedException)
        {
            return true;
        }
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
