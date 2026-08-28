using Nodal.Core.Metadata;

namespace WorldFoodDelivery.Domain.Relations;

[GraphRelation("CONTAINS")]
internal sealed record ContainsFood(int Quantity, decimal UnitPrice);
