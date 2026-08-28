using Nodal.Core.Metadata;
using WorldFoodDelivery.Domain.Enums;

namespace WorldFoodDelivery.Domain.Nodes;

[GraphNode("Courier")]
internal sealed record Courier(
    [property: GraphKey] string Id,
    string Name,
    decimal Rating,
    VehicleType VehicleType);
