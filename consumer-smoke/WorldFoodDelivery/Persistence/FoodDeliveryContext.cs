using Nodal.Core;
using Nodal.Core.Execution;
using Nodal.Core.Query;
using WorldFoodDelivery.Domain.Nodes;
using WorldFoodDelivery.Domain.Relations;

namespace WorldFoodDelivery.Persistence;

internal sealed class FoodDeliveryContext(IGraphProvider provider) : NodalContext(provider)
{
    public GraphSet<Customer> Customers => Set<Customer>();
    public GraphSet<Restaurant> Restaurants => Set<Restaurant>();
    public GraphSet<Food> Foods => Set<Food>();
    public GraphSet<FoodOrder> Orders => Set<FoodOrder>();
    public GraphSet<Courier> Couriers => Set<Courier>();
    public GraphSet<DeliveryZone> DeliveryZones => Set<DeliveryZone>();
    public GraphSet<WeatherObservation> WeatherObservations => Set<WeatherObservation>();

    public RelationSet<Customer, PlacedOrder, FoodOrder> PlacedOrders => Relations<Customer, PlacedOrder, FoodOrder>();
    public RelationSet<Customer, ReferredCustomer, Customer> CustomerReferrals => Relations<Customer, ReferredCustomer, Customer>();
    public RelationSet<FoodOrder, ContainsFood, Food> ContainsFoods => Relations<FoodOrder, ContainsFood, Food>();
    public RelationSet<FoodOrder, FromRestaurant, Restaurant> FromRestaurants => Relations<FoodOrder, FromRestaurant, Restaurant>();
    public RelationSet<FoodOrder, FulfilledBy, Courier> FulfilledOrders => Relations<FoodOrder, FulfilledBy, Courier>();
    public RelationSet<Restaurant, ServesFood, Food> ServesFoods => Relations<Restaurant, ServesFood, Food>();
    public RelationSet<FoodOrder, DeliveredIn, DeliveryZone> DeliveredOrders => Relations<FoodOrder, DeliveredIn, DeliveryZone>();
    public RelationSet<FoodOrder, ExperiencedWeather, WeatherObservation> WeatherEvents => Relations<FoodOrder, ExperiencedWeather, WeatherObservation>();
}
