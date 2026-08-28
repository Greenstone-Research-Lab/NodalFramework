using Nodal.Core.Metadata;
using WorldFoodDelivery.Domain.Enums;

namespace WorldFoodDelivery.Domain.Nodes;

[GraphNode("WeatherObservation")]
internal sealed record WeatherObservation(
    [property: GraphKey] string Id,
    WeatherCondition Condition,
    decimal TemperatureCelsius);
