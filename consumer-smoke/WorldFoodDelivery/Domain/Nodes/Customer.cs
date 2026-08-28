using Nodal.Core.Metadata;
using WorldFoodDelivery.Domain.Enums;

namespace WorldFoodDelivery.Domain.Nodes;

[GraphNode("Customer")]
internal sealed record Customer([property: GraphKey] string Id, string Name, LoyaltyTier LoyaltyTier);
