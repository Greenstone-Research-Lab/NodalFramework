using Nodal.Core.Metadata;
using WorldFoodDelivery.Domain.Enums;

namespace WorldFoodDelivery.Domain.Nodes;

[GraphNode("Order")]
internal sealed record FoodOrder(
    [property: GraphKey] string Id,
    DateTimeOffset OrderedAt,
    DateTimeOffset DeliveredAt,
    PaymentMethod PaymentMethod);
