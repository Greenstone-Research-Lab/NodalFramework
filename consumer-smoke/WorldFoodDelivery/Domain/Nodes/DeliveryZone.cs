using Nodal.Core.Metadata;

namespace WorldFoodDelivery.Domain.Nodes;

[GraphNode("DeliveryZone")]
internal sealed record DeliveryZone([property: GraphKey] string Id, string Name, string City);
