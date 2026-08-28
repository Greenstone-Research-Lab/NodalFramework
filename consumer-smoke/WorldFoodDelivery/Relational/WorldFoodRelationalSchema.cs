using Nodal.Import.Relational;

namespace WorldFoodDelivery.Relational;

internal static class WorldFoodRelationalSchema
{
    public static RelationalSchemaSnapshot Create() => new(
        "WorldFoodDelivery",
        [
            Table("Customers", Column("CustomerId", "varchar", primaryKey: true), Column("Name", "varchar"), Column("LoyaltyTier", "varchar")),
            Table("Restaurants", Column("RestaurantId", "varchar", primaryKey: true), Column("Name", "varchar"), Column("District", "varchar"), Column("Rating", "decimal")),
            Table("Foods", Column("FoodId", "varchar", primaryKey: true), Column("Name", "varchar"), Column("Category", "varchar"), Column("Kitchen", "varchar")),
            Table("Couriers", Column("CourierId", "varchar", primaryKey: true), Column("Name", "varchar"), Column("Rating", "decimal"), Column("VehicleType", "varchar")),
            Table("DeliveryZones", Column("ZoneId", "varchar", primaryKey: true), Column("Name", "varchar"), Column("City", "varchar")),
            Table("WeatherObservations", Column("WeatherId", "varchar", primaryKey: true), Column("Condition", "varchar"), Column("TemperatureCelsius", "decimal")),
            Table("Orders", Column("OrderId", "varchar", primaryKey: true), Column("CustomerId", "varchar"), Column("RestaurantId", "varchar"), Column("CourierId", "varchar"), Column("ZoneId", "varchar"), Column("WeatherId", "varchar"), Column("OrderedAt", "timestamp"), Column("DeliveredAt", "timestamp"), Column("PaymentMethod", "varchar")),
            Table("OrderLines", Column("OrderId", "varchar", primaryKey: true), Column("FoodId", "varchar", primaryKey: true), Column("Quantity", "int"), Column("UnitPrice", "decimal")),
            Table("RestaurantFoods", Column("RestaurantId", "varchar", primaryKey: true), Column("FoodId", "varchar", primaryKey: true), Column("Price", "decimal"), Column("Available", "boolean")),
        ],
        [
            ForeignKey("FK_Orders_Customers", "Orders", "CustomerId", "Customers", "CustomerId"),
            ForeignKey("FK_Orders_Restaurants", "Orders", "RestaurantId", "Restaurants", "RestaurantId"),
            ForeignKey("FK_Orders_Couriers", "Orders", "CourierId", "Couriers", "CourierId"),
            ForeignKey("FK_Orders_DeliveryZones", "Orders", "ZoneId", "DeliveryZones", "ZoneId"),
            ForeignKey("FK_Orders_Weather", "Orders", "WeatherId", "WeatherObservations", "WeatherId"),
            ForeignKey("FK_OrderLines_Orders", "OrderLines", "OrderId", "Orders", "OrderId", RelationalReferentialAction.Cascade),
            ForeignKey("FK_OrderLines_Foods", "OrderLines", "FoodId", "Foods", "FoodId"),
            ForeignKey("FK_RestaurantFoods_Restaurants", "RestaurantFoods", "RestaurantId", "Restaurants", "RestaurantId", RelationalReferentialAction.Cascade),
            ForeignKey("FK_RestaurantFoods_Foods", "RestaurantFoods", "FoodId", "Foods", "FoodId"),
        ],
        []);

    private static RelationalTable Table(string name, params RelationalColumn[] columns) =>
        new("public", name, "TABLE", columns.Select((column, index) => column with { Ordinal = index + 1 }).ToArray());

    private static RelationalColumn Column(
        string name,
        string dataType,
        bool primaryKey = false,
        bool nullable = false) =>
        new(name, dataType, nullable, Ordinal: 0, primaryKey);

    private static RelationalForeignKey ForeignKey(
        string name,
        string sourceTable,
        string sourceColumn,
        string targetTable,
        string targetColumn,
        RelationalReferentialAction onDelete = RelationalReferentialAction.Restrict) =>
        new(name, "public", sourceTable, "public", targetTable)
        {
            Columns = [new RelationalForeignKeyColumn(sourceColumn, targetColumn, 1)],
            OnDelete = onDelete,
            OnUpdate = RelationalReferentialAction.Restrict,
        };
}
