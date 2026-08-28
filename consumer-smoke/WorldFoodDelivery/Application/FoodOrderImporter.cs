using WorldFoodDelivery.Domain.Nodes;
using WorldFoodDelivery.Domain.Relations;
using WorldFoodDelivery.Persistence;

namespace WorldFoodDelivery.Application;

internal sealed class FoodOrderImporter
{
    public void Import(FoodDeliveryContext context, IReadOnlyList<FoodOrderCsvRow> rows)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(rows);

        var customers = new Dictionary<string, Customer>();
        var restaurants = new Dictionary<string, Restaurant>();
        var foods = new Dictionary<string, Food>();
        var orders = new Dictionary<string, FoodOrder>();
        var couriers = new Dictionary<string, Courier>();
        var zones = new Dictionary<string, DeliveryZone>();
        var weather = new Dictionary<string, WeatherObservation>();

        foreach (var row in rows)
        {
            var orderedAt = row.GetOrderedAt();
            var deliveredAt = row.GetDeliveredAt();
            var customer = GetOrAdd(customers, row.CustomerId, () =>
                new Customer(row.CustomerId, row.CustomerName, row.LoyaltyTier), value => context.Customers.Add(value));
            var restaurant = GetOrAdd(restaurants, row.RestaurantId, () =>
                new Restaurant(row.RestaurantId, row.RestaurantName, row.District, row.RestaurantRating), value => context.Restaurants.Add(value));
            var courier = GetOrAdd(couriers, row.CourierId, () =>
                new Courier(row.CourierId, row.CourierName, row.CourierRating, row.VehicleType), value => context.Couriers.Add(value));
            var zone = GetOrAdd(zones, row.ZoneId, () =>
                new DeliveryZone(row.ZoneId, row.ZoneName, "Tallinn"), value => context.DeliveryZones.Add(value));
            var observation = GetOrAdd(weather, row.WeatherId, () =>
                new WeatherObservation(row.WeatherId, row.WeatherCondition, row.TemperatureCelsius), value => context.WeatherObservations.Add(value));

            var foodIsNew = !foods.ContainsKey(row.FoodId);
            var food = GetOrAdd(foods, row.FoodId, () =>
                new Food(row.FoodId, row.FoodName, row.Category, row.Kitchen, row.UnitPrice), value => context.Foods.Add(value));
            if (foodIsNew)
            {
                context.ServesFoods.Connect(restaurant, new ServesFood(row.UnitPrice, Available: true), food);
            }

            var orderIsNew = !orders.ContainsKey(row.OrderId);
            var order = GetOrAdd(orders, row.OrderId, () =>
                new FoodOrder(row.OrderId, orderedAt, deliveredAt, row.PaymentMethod), value => context.Orders.Add(value));
            if (orderIsNew)
            {
                context.PlacedOrders.Connect(customer, new PlacedOrder(orderedAt), order);
                context.FromRestaurants.Connect(order, new FromRestaurant(), restaurant);
                context.FulfilledOrders.Connect(order, new FulfilledBy(deliveredAt - orderedAt), courier);
                context.DeliveredOrders.Connect(order, new DeliveredIn(), zone);
                context.WeatherEvents.Connect(order, new ExperiencedWeather(), observation);
            }

            context.ContainsFoods.Connect(order, new ContainsFood(row.Quantity, row.UnitPrice), food);
        }
    }

    private static TValue GetOrAdd<TValue>(
        IDictionary<string, TValue> values,
        string id,
        Func<TValue> create,
        Action<TValue> track)
    {
        if (values.TryGetValue(id, out var existing))
        {
            return existing;
        }

        var value = create();
        values.Add(id, value);
        track(value);
        return value;
    }
}
