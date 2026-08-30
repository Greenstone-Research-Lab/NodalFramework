using Nodal.Analytics.DerivedNetworks;
using Nodal.Analytics.Observations;
using Nodal.Core.Analytics;
using Nodal.Core.Execution;
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

        var pageRank = context.Customers.Query("customer")
            .Analyze(context.CustomerReferrals)
            .PageRank()
            .OnProjection("world_food_delivery")
            .Top(10)
            .ToQueryModel();
        Ensure(new Neo4jAnalyticsCompiler().Compile(pageRank).Text.Contains("pageRank", StringComparison.Ordinal),
            "Neo4j did not compile provider-native PageRank.");
        var tigerAnalytics = new TigerGraphAnalyticsCompiler(
            "WorldFoodDelivery",
            new Dictionary<GraphAnalyticsAlgorithm, string>
            {
                [GraphAnalyticsAlgorithm.PageRank] = "nodal_customer_pagerank",
            });
        var tigerPageRankCommand = tigerAnalytics.Compile(pageRank);
        Ensure(string.Equals(
                tigerPageRankCommand.Route,
                "restpp/query/WorldFoodDelivery/nodal_customer_pagerank",
                StringComparison.Ordinal),
            "TigerGraph did not compile the configured native PageRank query.");

        var observation = GraphObservationMaterializer.Materialize(new GraphQueryResult(
            [
                new GraphNodeRecord("Customer", "customer-1", new Dictionary<string, object?> { ["name"] = "Ada" }),
                new GraphNodeRecord("FoodOrder", "order-1", new Dictionary<string, object?> { ["total"] = 24.5m }),
            ],
            [new GraphRelationRecord(
                "PLACED_ORDER", "placed-1", "customer-1", "order-1",
                new Dictionary<string, object?> { ["orderedAt"] = "2026-08-30T12:00:00Z" })]),
            new GraphObservationOptions
            {
                MaxNodes = 10,
                MaxRelations = 10,
                NodeProperties = new HashSet<string>(["name", "total"], StringComparer.Ordinal),
                RelationProperties = new HashSet<string>(["orderedAt"], StringComparer.Ordinal),
            });
        Ensure(observation.Nodes.Count == 2 && observation.Relations.Count == 1,
            "The provider-normalized result did not become a canonical bounded observation.");
        var derived = GraphObservationNetworkAnalyzer.Analyze(observation);
        Ensure(derived.Converged && derived.Nodes.Count == observation.Nodes.Count,
            "The bounded observation did not produce reproducible derived-network evidence.");

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
