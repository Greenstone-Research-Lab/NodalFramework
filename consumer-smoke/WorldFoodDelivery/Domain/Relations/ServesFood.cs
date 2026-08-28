using Nodal.Core.Metadata;

namespace WorldFoodDelivery.Domain.Relations;

[GraphRelation("SERVES")]
internal sealed record ServesFood(decimal Price, bool Available);
