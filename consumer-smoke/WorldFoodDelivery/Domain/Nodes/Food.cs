using Nodal.Core.Metadata;
using WorldFoodDelivery.Domain.Enums;

namespace WorldFoodDelivery.Domain.Nodes;

[GraphNode("Food")]
internal sealed record Food(
    [property: GraphKey] string Id,
    string Name,
    string Category,
    Kitchen Kitchen,
    decimal Price);
