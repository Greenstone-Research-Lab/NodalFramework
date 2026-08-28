using Nodal.Core.Metadata;

namespace WorldFoodDelivery.Domain.Relations;

[GraphRelation("PLACED")]
internal sealed record PlacedOrder(DateTimeOffset OrderedAt);
