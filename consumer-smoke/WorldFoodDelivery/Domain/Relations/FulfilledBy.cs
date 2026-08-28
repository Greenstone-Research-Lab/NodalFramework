using Nodal.Core.Metadata;

namespace WorldFoodDelivery.Domain.Relations;

[GraphRelation("FULFILLED_BY")]
internal sealed record FulfilledBy(TimeSpan DeliveryDuration);
