using Nodal.Core.Modeling;
using Nodal.Import.Relational;

namespace Nodal.Import.Tests;

public sealed class RelationalGraphModelDescriptorBuilderTests
{
    [Fact]
    public void BuildsTypedCanonicalDescriptorWithPhysicalEvidence()
    {
        var interaction = RelationalInteractionModelBuilder.Build(Schema(), "SqlServer");

        var descriptor = RelationalGraphModelDescriptorBuilder.Build(interaction);

        Assert.Equal(interaction.Source.SchemaFingerprint, descriptor.SourceFingerprint);
        Assert.Equal("SqlServer", descriptor.ProviderAnnotations!["source.provider"]);
        var order = Assert.Single(descriptor.Nodes, node => node.Id == "dbo.Orders");
        Assert.Equal(["OrderID"], order.Key.Properties);
        Assert.Equal(GraphValueKind.SignedInteger, Assert.Single(order.Properties, property => property.Name == "OrderID").ValueKind);
        Assert.Equal(GraphValueKind.DateTimeOffset, Assert.Single(order.Properties, property => property.Name == "OrderedAt").ValueKind);
        var relation = Assert.Single(descriptor.Relations);
        Assert.Equal("REFERENCES_CUSTOMER", relation.Name);
        Assert.Equal("true", relation.ProviderAnnotations!["review.required"]);
        Assert.Equal("CustomerID", relation.ProviderAnnotations["source.sourceColumns"]);
        Assert.Equal(GraphModelDescriptorJson.Serialize(descriptor),
            GraphModelDescriptorJson.Serialize(RelationalGraphModelDescriptorBuilder.Build(interaction)));
    }

    [Fact]
    public void MissingKeyAndUnknownTypesRemainExplicitReviewableEvidence()
    {
        var schema = new RelationalSchemaSnapshot("Db", [new RelationalTable(
            "dbo", "Logs", "TABLE", [new RelationalColumn("Payload", "vendor_blob", true, 1, false)])], [], []);

        var descriptor = RelationalGraphModelDescriptorBuilder.Build(RelationalInteractionModelBuilder.Build(schema));

        var node = Assert.Single(descriptor.Nodes);
        Assert.Equal(["__nodal_source_identity"], node.Key.Properties);
        Assert.Equal("true", node.ProviderAnnotations!["review.syntheticKey"]);
        Assert.Equal(GraphValueKind.Text, Assert.Single(node.Properties, property => property.Name == "Payload").ValueKind);
    }

    [Fact]
    public void TypeClassifierPreservesPortableKindsThroughDescriptorOutput()
    {
        var columns = new[]
        {
            Column("Id", "uniqueidentifier", true, 1),
            Column("Amount", "numeric(18,2)", false, 2),
            Column("Score", "double precision", false, 3),
            Column("Active", "boolean", false, 4),
            Column("Day", "date", false, 5),
            Column("Clock", "time", false, 6),
            Column("At", "datetime", false, 7),
            Column("Location", "geography", false, 8),
            Column("Embedding", "vector(3)", false, 9),
        };
        var schema = new RelationalSchemaSnapshot("Db", [new RelationalTable("dbo", "Typed", "TABLE", columns)], [], []);

        var node = Assert.Single(RelationalGraphModelDescriptorBuilder.Build(
            RelationalInteractionModelBuilder.Build(schema)).Nodes);

        Assert.Equal(GraphValueKind.Identifier, Kind(node, "Id"));
        Assert.Equal(GraphValueKind.DecimalNumber, Kind(node, "Amount"));
        Assert.Equal(GraphValueKind.FloatingPoint, Kind(node, "Score"));
        Assert.Equal(GraphValueKind.Boolean, Kind(node, "Active"));
        Assert.Equal(GraphValueKind.Date, Kind(node, "Day"));
        Assert.Equal(GraphValueKind.Time, Kind(node, "Clock"));
        Assert.Equal(GraphValueKind.DateTime, Kind(node, "At"));
        Assert.Equal(GraphValueKind.GeoPoint, Kind(node, "Location"));
        Assert.Equal(GraphValueKind.Vector, Kind(node, "Embedding"));
        Assert.Throws<ArgumentNullException>(() => RelationalGraphModelDescriptorBuilder.Build(null!));
    }

    [Fact]
    public void NormalizedClrNameCollisionsReceiveStableSourceDerivedSuffixes()
    {
        var schema = new RelationalSchemaSnapshot(
            "Db",
            [
                new RelationalTable("dbo", "Sales-Areas", "TABLE", [Column("Area-Id", "int", true, 1), Column("A-B", "int", false, 2), Column("A_B", "int", false, 3)]),
                new RelationalTable("dbo", "Sales_Areas", "TABLE", [Column("AreaId", "int", true, 1)]),
                new RelationalTable("dbo", "Orders", "TABLE", [Column("OrderId", "int", true, 1), Column("FirstArea", "int", false, 2), Column("SecondArea", "int", false, 3)]),
            ],
            [
                ForeignKey("FK_Orders_First", "Orders", "FirstArea", "Sales-Areas", "Area-Id"),
                ForeignKey("FK_Orders_Second", "Orders", "SecondArea", "Sales_Areas", "AreaId"),
            ],
            []);

        var descriptor = RelationalGraphModelDescriptorBuilder.Build(RelationalInteractionModelBuilder.Build(schema));

        Assert.Equal(descriptor.Nodes.Count, descriptor.Nodes.Select(node => node.ClrName).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(descriptor.Relations.Count, descriptor.Relations.Select(relation => relation.ClrName).Distinct(StringComparer.Ordinal).Count());
        var collidingProperties = Assert.Single(descriptor.Nodes, node => node.Id == "dbo.Sales-Areas").Properties;
        Assert.Equal(collidingProperties.Count, collidingProperties.Select(property => property.ClrName).Distinct(StringComparer.Ordinal).Count());
        Assert.True(GraphModelValidation.Validate(descriptor).IsValid);
        Assert.Equal(
            GraphModelDescriptorJson.Serialize(descriptor),
            GraphModelDescriptorJson.Serialize(RelationalGraphModelDescriptorBuilder.Build(RelationalInteractionModelBuilder.Build(schema))));
    }

    private static GraphValueKind Kind(NodeTypeDescriptor node, string name) =>
        Assert.Single(node.Properties, property => property.Name == name).ValueKind;

    private static RelationalSchemaSnapshot Schema() => new(
        "Northwind",
        [
            new RelationalTable("dbo", "Orders", "TABLE", [
                Column("OrderID", "int", true, 1),
                Column("CustomerID", "nvarchar(5)", false, 2),
                Column("OrderedAt", "datetimeoffset", false, 3),
            ]),
            new RelationalTable("dbo", "Customers", "TABLE", [Column("CustomerID", "nvarchar(5)", true, 1)]),
        ],
        [new RelationalForeignKey("FK_Orders_Customers", "dbo", "Orders", "dbo", "Customers")
        {
            Columns = [new RelationalForeignKeyColumn("CustomerID", "CustomerID", 1)],
        }],
        []);

    private static RelationalForeignKey ForeignKey(
        string name,
        string source,
        string sourceColumn,
        string target,
        string targetColumn) => new(name, "dbo", source, "dbo", target)
        {
            Columns = [new RelationalForeignKeyColumn(sourceColumn, targetColumn, 1)],
        };

    private static RelationalColumn Column(string name, string type, bool key, int ordinal) =>
        new(name, type, false, ordinal, key);
}
