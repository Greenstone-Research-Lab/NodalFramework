using Nodal.Import.Relational;

namespace WorldFoodDelivery.Relational;

internal sealed class WorldFoodRelationalInspectionHost : IRelationalInspectionHost
{
    public string ProviderName => "PortableSql";

    public ValueTask<RelationalSchemaSnapshot> InspectAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(WorldFoodRelationalSchema.Create());
    }
}
