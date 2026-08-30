using Nodal.Migrations;
using WorldFoodDelivery.Domain.Nodes;
using WorldFoodDelivery.Domain.Relations;

namespace WorldFoodDelivery.Persistence;

internal sealed class FoodDeliveryMigration : NodalMigration
{
    protected override void Up(MigrationBuilder migration) => migration
        .CreateNode<Customer>()
        .CreateNode<Restaurant>()
        .CreateNode<Food>()
        .CreateNode<FoodOrder>()
        .CreateNode<Courier>()
        .CreateNode<DeliveryZone>()
        .CreateNode<WeatherObservation>()
        .CreateRelation<PlacedOrder, Customer, FoodOrder>()
        .CreateRelation<ReferredCustomer, Customer, Customer>()
        .CreateRelation<ContainsFood, FoodOrder, Food>()
        .CreateRelation<FromRestaurant, FoodOrder, Restaurant>()
        .CreateRelation<FulfilledBy, FoodOrder, Courier>()
        .CreateRelation<ServesFood, Restaurant, Food>()
        .CreateRelation<DeliveredIn, FoodOrder, DeliveryZone>()
        .CreateRelation<ExperiencedWeather, FoodOrder, WeatherObservation>();

    protected override void Down(MigrationBuilder migration)
    {
    }
}
