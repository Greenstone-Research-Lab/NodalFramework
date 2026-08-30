using Nodal.Core.Metadata;

namespace WorldFoodDelivery.Domain.Relations;

[GraphRelation("REFERRED_CUSTOMER")]
internal sealed record ReferredCustomer(DateTimeOffset ReferredAt);
