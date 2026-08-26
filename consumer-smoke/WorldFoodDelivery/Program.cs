using Nodal.Core;
using Nodal.Core.Execution;
using Nodal.Core.Metadata;
using Nodal.Core.Migrations;
using Nodal.Core.Mutations;
using Nodal.Core.Providers;
using Nodal.Core.Query;
using Nodal.Migrations;
using Nodal.Neo4j;
using Nodal.TigerGraph;

var csvPath = args.SingleOrDefault() ?? "orders.csv";
var rows = FoodOrderCsv.Read(csvPath);
var provider = new ConsumerSmokeProvider();
var context = new FoodDeliveryContext(provider);
FoodOrderImporter.Import(context, rows);
var saved = await context.SaveChangesAsync();
if (saved.AffectedNodes != 13 || saved.AffectedRelations != 17 || !saved.IsAtomic)
    throw new InvalidOperationException("CSV import did not produce the expected atomic graph mutation batch.");

var foodOrders = context.Customers.Query("customer")
    .Traverse(context.PlacedOrders, "placed", "order")
    .Traverse(context.ContainsFoods, "contains", "food")
    .Where(food => food.Kitchen == Kitchen.Asian)
    .ToQueryModel();
var restaurantSummary = context.Restaurants.Query("restaurant")
    .Traverse(context.ServesFoods, "serves", "food")
    .ToRows().Count("orderCount").Average("averagePrice", food => food.Price)
    .ToQueryModel();
var unavailableRestaurant = context.Restaurants.Query("restaurant")
    .WhereNotExists(context.ServesFoods, food => food.Price > 100m).ToQueryModel();

var cypher = new Neo4jQueryCompiler().Compile(foodOrders);
if (!cypher.Text.Contains("MATCH", StringComparison.Ordinal) || new Neo4jQueryCompiler().Compile(restaurantSummary).Text.Length == 0)
    throw new InvalidOperationException("Neo4j did not compile the consumer query model.");
var tiger = new TigerGraphQueryCompiler("WorldFoodDelivery");
if (tiger.Compile(foodOrders).Text.Length == 0)
    throw new InvalidOperationException("TigerGraph did not compile the portable traversal.");
if (!ThrowsNotSupported(() => tiger.Compile(unavailableRestaurant)))
    throw new InvalidOperationException("TigerGraph must reject correlated subqueries before transport.");

_ = new MigrationPlanner(new Neo4jMigrationDialect()).PlanUp(new FoodDeliveryMigration());
Console.WriteLine($"Clean-room consumer smoke passed: {rows.Count} CSV rows, {saved.AffectedNodes} nodes, {saved.AffectedRelations} relations.");

static bool ThrowsNotSupported(Action action)
{
    try { action(); return false; }
    catch (NotSupportedException) { return true; }
}

enum Kitchen { Asian, Italian }
enum VehicleType { Bicycle, Scooter }

[GraphNode("Customer")] sealed record Customer([property: GraphKey] string Id, string Name);
[GraphNode("Restaurant")] sealed record Restaurant([property: GraphKey] string Id, string Name, string District);
[GraphNode("Food")] sealed record Food([property: GraphKey] string Id, string Name, string Category, Kitchen Kitchen, decimal Price);
[GraphNode("Order")] sealed record Order([property: GraphKey] string Id);
[GraphNode("Courier")] sealed record Courier([property: GraphKey] string Id, VehicleType VehicleType);
[GraphRelation("PLACED")] sealed record PlacedOrder(DateTimeOffset OrderedAt);
[GraphRelation("CONTAINS")] sealed record ContainsFood(int Quantity, decimal UnitPrice);
[GraphRelation("FROM")] sealed record FromRestaurant;
[GraphRelation("FULFILLED_BY")] sealed record FulfilledBy;
[GraphRelation("SERVES")] sealed record ServesFood(decimal Price, bool Available);

sealed class FoodDeliveryContext(IGraphProvider provider) : NodalContext(provider)
{
    public GraphSet<Customer> Customers => Set<Customer>(); public GraphSet<Restaurant> Restaurants => Set<Restaurant>();
    public GraphSet<Food> Foods => Set<Food>(); public GraphSet<Order> Orders => Set<Order>(); public GraphSet<Courier> Couriers => Set<Courier>();
    public RelationSet<Customer, PlacedOrder, Order> PlacedOrders => Relations<Customer, PlacedOrder, Order>();
    public RelationSet<Order, ContainsFood, Food> ContainsFoods => Relations<Order, ContainsFood, Food>();
    public RelationSet<Order, FromRestaurant, Restaurant> FromRestaurants => Relations<Order, FromRestaurant, Restaurant>();
    public RelationSet<Order, FulfilledBy, Courier> FulfilledOrders => Relations<Order, FulfilledBy, Courier>();
    public RelationSet<Restaurant, ServesFood, Food> ServesFoods => Relations<Restaurant, ServesFood, Food>();
}

sealed record CsvOrder(string OrderId, string CustomerId, string CustomerName, string RestaurantId, string RestaurantName, string District, string FoodId, string FoodName, string Category, Kitchen Kitchen, decimal Price, int Quantity, string CourierId, VehicleType VehicleType);
static class FoodOrderCsv
{
    public static IReadOnlyList<CsvOrder> Read(string path) => File.ReadLines(path).Skip(1).Select(line => line.Split(',', StringSplitOptions.TrimEntries)).Select(parts => new CsvOrder(parts[0], parts[1], parts[2], parts[3], parts[4], parts[5], parts[6], parts[7], parts[8], Enum.Parse<Kitchen>(parts[9]), decimal.Parse(parts[10], System.Globalization.CultureInfo.InvariantCulture), int.Parse(parts[11], System.Globalization.CultureInfo.InvariantCulture), parts[12], Enum.Parse<VehicleType>(parts[13]))).ToArray();
}
static class FoodOrderImporter
{
    public static void Import(FoodDeliveryContext context, IReadOnlyList<CsvOrder> rows)
    {
        var customers = new Dictionary<string, Customer>(); var restaurants = new Dictionary<string, Restaurant>(); var foods = new Dictionary<string, Food>(); var orders = new Dictionary<string, Order>(); var couriers = new Dictionary<string, Courier>();
        foreach (var row in rows)
        {
            if (!customers.TryGetValue(row.CustomerId, out var customer)) { customer = new(row.CustomerId, row.CustomerName); customers.Add(customer.Id, customer); context.Customers.Add(customer); }
            if (!restaurants.TryGetValue(row.RestaurantId, out var restaurant)) { restaurant = new(row.RestaurantId, row.RestaurantName, row.District); restaurants.Add(restaurant.Id, restaurant); context.Restaurants.Add(restaurant); }
            if (!foods.TryGetValue(row.FoodId, out var food)) { food = new(row.FoodId, row.FoodName, row.Category, row.Kitchen, row.Price); foods.Add(food.Id, food); context.Foods.Add(food); context.ServesFoods.Connect(restaurant, new(row.Price, true), food); }
            if (!couriers.TryGetValue(row.CourierId, out var courier)) { courier = new(row.CourierId, row.VehicleType); couriers.Add(courier.Id, courier); context.Couriers.Add(courier); }
            if (!orders.TryGetValue(row.OrderId, out var order)) { order = new(row.OrderId); orders.Add(order.Id, order); context.Orders.Add(order); context.PlacedOrders.Connect(customer, new(DateTimeOffset.Parse("2026-08-27T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture)), order); context.FromRestaurants.Connect(order, new(), restaurant); context.FulfilledOrders.Connect(order, new(), courier); }
            context.ContainsFoods.Connect(order, new(row.Quantity, row.Price), food);
        }
    }
}
sealed class FoodDeliveryMigration : NodalMigration
{
    protected override void Up(MigrationBuilder migration) => migration.CreateNode<Customer>().CreateNode<Restaurant>().CreateNode<Food>().CreateNode<Order>().CreateNode<Courier>().CreateRelation<PlacedOrder, Customer, Order>().CreateRelation<ContainsFood, Order, Food>().CreateRelation<FromRestaurant, Order, Restaurant>().CreateRelation<FulfilledBy, Order, Courier>().CreateRelation<ServesFood, Restaurant, Food>();
    protected override void Down(MigrationBuilder migration) { }
}
sealed class ConsumerSmokeProvider : IGraphProvider, IGraphMutationProvider
{
    public IGraphQueryCompiler QueryCompiler { get; } = new Neo4jQueryCompiler(); public IGraphCommandExecutor CommandExecutor { get; } = new NoopExecutor(); public IGraphResultMaterializer ResultMaterializer { get; } = new JsonGraphResultMaterializer(); public GraphProviderCapabilities Capabilities { get; } = new() { SupportsTransactions = true, SupportsAtomicBatch = true, TransactionScope = GraphTransactionScope.RequestOrQuery }; public IGraphMutationExecutor MutationExecutor { get; } = new RecordingMutationExecutor();
    private sealed class NoopExecutor : IGraphCommandExecutor { public ValueTask<GraphQueryResult> ExecuteAsync(GraphCommand command, CancellationToken cancellationToken = default) => ValueTask.FromResult(new GraphQueryResult([])); }
    private sealed class RecordingMutationExecutor : IGraphMutationExecutor { public ValueTask<GraphMutationResult> ExecuteAsync(GraphMutationPlan plan, CancellationToken cancellationToken = default) => ValueTask.FromResult(new GraphMutationResult(plan.Operations.Count(operation => operation is not Nodal.Core.Mutations.CreateRelationOperation), plan.Operations.Count(operation => operation is Nodal.Core.Mutations.CreateRelationOperation), true)); }
}
