using WorldFoodDelivery.Domain.Enums;

namespace WorldFoodDelivery.Application;

internal sealed class FoodOrderCsvRow
{
    public string OrderId { get; set; } = string.Empty;
    public string OrderedAt { get; set; } = string.Empty;
    public string DeliveredAt { get; set; } = string.Empty;
    public PaymentMethod PaymentMethod { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public LoyaltyTier LoyaltyTier { get; set; }
    public string RestaurantId { get; set; } = string.Empty;
    public string RestaurantName { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    public decimal RestaurantRating { get; set; }
    public string FoodId { get; set; } = string.Empty;
    public string FoodName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public Kitchen Kitchen { get; set; }
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public string CourierId { get; set; } = string.Empty;
    public string CourierName { get; set; } = string.Empty;
    public decimal CourierRating { get; set; }
    public VehicleType VehicleType { get; set; }
    public string ZoneId { get; set; } = string.Empty;
    public string ZoneName { get; set; } = string.Empty;
    public string WeatherId { get; set; } = string.Empty;
    public WeatherCondition WeatherCondition { get; set; }
    public decimal TemperatureCelsius { get; set; }

    public DateTimeOffset GetOrderedAt() =>
        DateTimeOffset.Parse(OrderedAt, System.Globalization.CultureInfo.InvariantCulture);

    public DateTimeOffset GetDeliveredAt() =>
        DateTimeOffset.Parse(DeliveredAt, System.Globalization.CultureInfo.InvariantCulture);
}
