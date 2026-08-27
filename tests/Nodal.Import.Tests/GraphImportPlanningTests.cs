using Nodal.Core.Mutations;
using Nodal.Import;

namespace Nodal.Import.Tests;

public sealed class GraphImportPlanningTests
{
    [Fact]
    public void MappingExposesNodeAndRelationDecisions()
    {
        var mapping = CreateMapping();

        Assert.Collection(
            mapping.Decisions,
            customer =>
            {
                Assert.Equal(GraphImportMappingKind.Node, customer.Kind);
                Assert.Equal(("customer", "Customer", GraphImportWriteBehavior.Upsert),
                    (customer.MappingName, customer.TargetName, customer.WriteBehavior));
                Assert.Equal(["Name"], customer.PropertyNames);
                Assert.Null(customer.SourceMappingName);
            },
            order => Assert.Equal("Order", order.TargetName),
            placed =>
            {
                Assert.Equal(GraphImportMappingKind.Relation, placed.Kind);
                Assert.Equal(("customer", "order"), (placed.SourceMappingName, placed.TargetMappingName));
                Assert.Equal(["OrderedAt"], placed.PropertyNames);
            });
    }

    [Fact]
    public void PlannerProducesDependencySafeProviderNeutralPlanAndDryRun()
    {
        var batch = new GraphImportBatch<OrderRow>(7, [new("customer-1", "Ada", "order-1", 12.50m)]);
        var result = new GraphImportPlanner<OrderRow>().Plan(batch, CreateMapping());

        Assert.True(result.DryRun.Succeeded);
        Assert.True(result.DryRun.HasDestructiveRisks);
        Assert.Equal((1L, 2, 1),
            (result.DryRun.SourceRecordCount, result.DryRun.PlannedNodeCount, result.DryRun.PlannedRelationCount));
        Assert.Equal(3, result.MutationPlan.Operations.Count);

        var customer = Assert.IsType<CreateNodeOperation>(result.MutationPlan.Operations[0]);
        Assert.Equal((typeof(Customer), "Customer", "Id", "customer-1"),
            (customer.Identity.ClrType, customer.Identity.NodeType, customer.Identity.KeyProperty, customer.Identity.Value));
        Assert.Equal("Ada", customer.Properties["Name"]);
        var order = Assert.IsType<CreateNodeOperation>(result.MutationPlan.Operations[1]);
        Assert.Equal(12.50m, order.Properties["Total"]);
        var relation = Assert.IsType<CreateRelationOperation>(result.MutationPlan.Operations[2]);
        Assert.Equal(("Customer", "PLACED", "Order", true),
            (relation.Source.NodeType, relation.RelationType, relation.Target.NodeType, relation.Directed));
        Assert.Equal(FixedOrderedAt, relation.Properties["OrderedAt"]);

        var overwrite = Assert.Single(result.DryRun.Risks);
        Assert.Equal("NODAL-IMPORT-PROPERTY-OVERWRITE", overwrite.Code);
        Assert.Equal(3, overwrite.OccurrenceCount);
        Assert.Equal(GraphImportRiskSeverity.Warning, overwrite.Severity);
    }

    [Fact]
    public void PlannerCoalescesDuplicateIdentitiesUsingLaterRecord()
    {
        var batch = new GraphImportBatch<OrderRow>(1,
        [
            new("customer-1", "Ada", "order-1", 10m),
            new("customer-1", "Ada Updated", "order-1", 20m),
        ]);

        var result = new GraphImportPlanner<OrderRow>().Plan(batch, CreateMapping());

        Assert.Equal(3, result.MutationPlan.Operations.Count);
        Assert.Equal("Ada Updated", Assert.IsType<CreateNodeOperation>(result.MutationPlan.Operations[0]).Properties["Name"]);
        Assert.Equal(20m, Assert.IsType<CreateNodeOperation>(result.MutationPlan.Operations[1]).Properties["Total"]);
        Assert.Equal(3, result.DryRun.Diagnostics.Count);
        Assert.All(result.DryRun.Diagnostics, diagnostic => Assert.StartsWith("WARNING", diagnostic.Code));
        Assert.Contains(result.DryRun.Risks, risk => risk.Code == "NODAL-IMPORT-DUPLICATE-NODE" && risk.OccurrenceCount == 2);
        Assert.Contains(result.DryRun.Risks, risk => risk.Code == "NODAL-IMPORT-DUPLICATE-RELATION" && risk.OccurrenceCount == 1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PlannerReportsMissingKeysAndOmitsDependentRelations(string? customerId)
    {
        var batch = new GraphImportBatch<OrderRow>(11, [new(customerId, "Ada", "order-1", 10m)]);

        var result = new GraphImportPlanner<OrderRow>().Plan(batch, CreateMapping());

        Assert.False(result.DryRun.Succeeded);
        Assert.Single(result.MutationPlan.Operations);
        Assert.Equal(2, result.DryRun.Diagnostics.Count);
        Assert.Equal(11, result.DryRun.Diagnostics[0].RecordNumber);
        var omission = Assert.Single(result.DryRun.Risks, risk => risk.Code == "NODAL-IMPORT-OMITTED-MAPPING");
        Assert.Equal(2, omission.OccurrenceCount);
        Assert.Equal(GraphImportRiskSeverity.Critical, omission.Severity);
    }

    [Fact]
    public void PlannerEnforcesOperationBoundary()
    {
        var exception = Assert.Throws<GraphImportPlanLimitExceededException>(() =>
            new GraphImportPlanner<OrderRow>().Plan(
                new GraphImportBatch<OrderRow>(1, [new("customer-1", "Ada", "order-1", 10m)]),
                CreateMapping(),
                new GraphImportPlanningOptions(2)));

        Assert.Equal(2, exception.MaxOperations);
        Assert.Contains("2", exception.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(() => new GraphImportPlanningOptions(0).Validate());
    }

    [Fact]
    public void PlannerValidatesArgumentsAndSupportsAnEmptyBatch()
    {
        var planner = new GraphImportPlanner<OrderRow>();
        var mapping = CreateMapping();

        Assert.Throws<ArgumentNullException>(() => planner.Plan(null!, mapping));
        Assert.Throws<ArgumentNullException>(() => planner.Plan(new GraphImportBatch<OrderRow>(1, []), null!));
        Assert.Throws<ArgumentNullException>(() => planner.Plan(new GraphImportBatch<OrderRow>(1, null!), mapping));
        Assert.Throws<ArgumentOutOfRangeException>(() => planner.Plan(new GraphImportBatch<OrderRow>(0, []), mapping));
        var result = planner.Plan(new GraphImportBatch<OrderRow>(1, []), mapping);
        Assert.True(result.MutationPlan.IsEmpty);
        Assert.True(result.DryRun.Succeeded);
        Assert.False(result.DryRun.HasDestructiveRisks);
        Assert.Empty(result.DryRun.Risks);
    }

    [Fact]
    public void MappingBuilderRejectsInvalidAndAmbiguousDefinitions()
    {
        Assert.Throws<InvalidOperationException>(() => GraphImportMapping.For<OrderRow>().Build());
        Assert.Throws<ArgumentException>(() => GraphImportMapping.For<OrderRow>()
            .Node<Customer>("", "Customer", "Id", row => row.CustomerId));
        Assert.Throws<ArgumentNullException>(() => GraphImportMapping.For<OrderRow>()
            .Node<Customer>("customer", "Customer", "Id", null!));
        Assert.Throws<ArgumentException>(() => GraphImportMapping.For<OrderRow>()
            .Node<Customer>("customer", "Customer", "Id", row => row.CustomerId)
            .Node<Customer>("customer", "Other", "Id", row => row.CustomerId));
        Assert.Throws<InvalidOperationException>(() => GraphImportMapping.For<OrderRow>()
            .Node<Customer>("customer", "Customer", "Id", row => row.CustomerId)
            .Relation("placed", "customer", "missing", "PLACED")
            .Build());
    }

    [Fact]
    public void PropertyBuilderRejectsDuplicatesAndStableKeyRemapping()
    {
        Assert.Throws<ArgumentException>(() => GraphImportMapping.For<OrderRow>()
            .Node<Customer>("customer", "Customer", "Id", row => row.CustomerId,
                node => node.Property("Name", row => row.CustomerName)
                    .Property("Name", row => row.CustomerName)));
        Assert.Throws<ArgumentNullException>(() => GraphImportMapping.For<OrderRow>()
            .Node<Customer>("customer", "Customer", "Id", row => row.CustomerId,
                node => node.Property("Name", null!)));
        Assert.Throws<ArgumentException>(() => GraphImportMapping.For<OrderRow>()
            .Node<Customer>("customer", "Customer", "Id", row => row.CustomerId,
                node => node.Property("Id", row => row.CustomerId)));
    }

    [Fact]
    public void PlannerSupportsUndirectedPropertyFreeMappingsAndNullProperties()
    {
        var mapping = GraphImportMapping.For<OrderRow>()
            .Node<Customer>("customer", "Customer", "Id", row => row.CustomerId,
                node => node.Property("Name", _ => null))
            .Node<Order>("order", "Order", "Id", row => row.OrderId)
            .Relation("related", "customer", "order", "RELATED", directed: false)
            .Build();

        var result = new GraphImportPlanner<OrderRow>().Plan(
            new GraphImportBatch<OrderRow>(1, [new("customer-1", "Ada", "order-1", 10m)]),
            mapping);

        Assert.Null(Assert.IsType<CreateNodeOperation>(result.MutationPlan.Operations[0]).Properties["Name"]);
        Assert.False(Assert.IsType<CreateRelationOperation>(result.MutationPlan.Operations[2]).Directed);
        Assert.Equal(1, Assert.Single(result.DryRun.Risks).OccurrenceCount);
    }

    [Fact]
    public void PlannerCoalescesProviderIdentityAcrossDifferentClrMappings()
    {
        var mapping = GraphImportMapping.For<OrderRow>()
            .Node<Customer>("customer", "Customer", "Id", row => row.CustomerId,
                node => node.Property("Name", row => row.CustomerName))
            .Node<CustomerProjection>("projection", "Customer", "Id", row => row.CustomerId,
                node => node.Property("Name", _ => "Projected"))
            .Build();

        var result = new GraphImportPlanner<OrderRow>().Plan(
            new GraphImportBatch<OrderRow>(1, [new("customer-1", "Ada", "order-1", 10m)]),
            mapping);

        var node = Assert.IsType<CreateNodeOperation>(Assert.Single(result.MutationPlan.Operations));
        Assert.Equal(typeof(CustomerProjection), node.Identity.ClrType);
        Assert.Equal("Projected", node.Properties["Name"]);
        Assert.Contains(result.DryRun.Diagnostics, diagnostic => diagnostic.Code == "WARNING-IMPORT-DUPLICATE-NODE");
    }

    private static GraphImportMapping<OrderRow> CreateMapping() =>
        GraphImportMapping.For<OrderRow>()
            .Node<Customer>("customer", "Customer", "Id", row => row.CustomerId,
                node => node.Property("Name", row => row.CustomerName))
            .Node<Order>("order", "Order", "Id", row => row.OrderId,
                node => node.Property("Total", row => row.Total))
            .Relation("placed", "customer", "order", "PLACED", configure:
                relation => relation.Property("OrderedAt", _ => FixedOrderedAt))
            .Build();

    private static readonly DateTimeOffset FixedOrderedAt = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);

    private sealed record OrderRow(string? CustomerId, string CustomerName, string OrderId, decimal Total);

    private sealed class Customer;

    private sealed class CustomerProjection;

    private sealed class Order;
}
