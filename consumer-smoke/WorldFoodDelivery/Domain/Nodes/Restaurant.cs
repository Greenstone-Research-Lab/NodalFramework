using Nodal.Core.Metadata;

namespace WorldFoodDelivery.Domain.Nodes;

[GraphNode("Restaurant")]
internal sealed record Restaurant(
    [property: GraphKey] string Id,
    string Name,
    string District,
    decimal Rating);
