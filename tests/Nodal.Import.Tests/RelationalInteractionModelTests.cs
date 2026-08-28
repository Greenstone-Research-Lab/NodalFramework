using System.Xml.Linq;
using Nodal.Import.Relational;

namespace Nodal.Import.Tests;

public sealed class RelationalInteractionModelTests
{
    [Fact]
    public void NorthwindModelPreservesPhysicalEvidenceAndSuggestsReadableDisplay()
    {
        var model = RelationalInteractionModelBuilder.Build(Northwind(), "SqlServer");

        Assert.Equal(RelationalInteractionFormat.CurrentVersion, model.FormatVersion);
        Assert.Equal("SqlServer", model.Source.Provider);
        Assert.Equal("Northwind", model.Source.Database);
        Assert.Equal(64, model.Source.SchemaFingerprint.Length);
        Assert.Equal(RelationalInteractionObjectRole.Entity, Object(model, "dbo.Orders").Role);
        Assert.Equal(RelationalInteractionObjectRole.Association, Object(model, "dbo.OrderDetails").Role);
        Assert.Equal(RelationalInteractionObjectRole.View, Object(model, "dbo.CurrentOrders").Role);
        Assert.Equal(RelationalInteractionObjectRole.Unknown, Object(model, "dbo.ImportLog").Role);

        var orderRelation = Relation(model, "FK_OrderDetails_Orders");
        Assert.Equal("dbo.OrderDetails", orderRelation.Source.ObjectId);
        Assert.Equal(["OrderID"], orderRelation.Source.Columns);
        Assert.Equal("dbo.Orders", orderRelation.Target.ObjectId);
        Assert.Equal(["OrderID"], orderRelation.Target.Columns);
        Assert.Equal(RelationalReferentialAction.Cascade, orderRelation.OnDelete);
        Assert.True(orderRelation.Display.Reversed);
        Assert.True(orderRelation.Display.RequiresReview);
        Assert.Equal(("dbo.Orders", "dbo.OrderDetails", "HAS_ORDER_DETAIL"),
            (orderRelation.Display.SourceObjectId, orderRelation.Display.TargetObjectId, orderRelation.Display.SuggestedLabel));

        var productRelation = Relation(model, "FK_OrderDetails_Products");
        Assert.False(productRelation.Display.Reversed);
        Assert.Equal(("dbo.OrderDetails", "dbo.Products", "REFERENCES_PRODUCT"),
            (productRelation.Display.SourceObjectId, productRelation.Display.TargetObjectId, productRelation.Display.SuggestedLabel));
        Assert.Empty(model.Diagnostics);
    }

    [Fact]
    public void FingerprintIsOrderIndependentAndChangesWithStructuralEvidence()
    {
        var schema = Northwind();
        var reordered = schema with
        {
            Tables = schema.Tables.Reverse().ToArray(),
            ForeignKeys = schema.ForeignKeys.Reverse().ToArray(),
        };
        var changed = schema with
        {
            Tables = schema.Tables.Select(table => table.Name == "Products"
                ? table with { Columns = [.. table.Columns, new RelationalColumn("Discontinued", "bit", false, 2, false)] }
                : table).ToArray(),
        };

        Assert.Equal(
            RelationalInteractionModelBuilder.Build(schema).Source.SchemaFingerprint,
            RelationalInteractionModelBuilder.Build(reordered).Source.SchemaFingerprint);
        Assert.NotEqual(
            RelationalInteractionModelBuilder.Build(schema).Source.SchemaFingerprint,
            RelationalInteractionModelBuilder.Build(changed).Source.SchemaFingerprint);
    }

    [Fact]
    public void MissingForeignKeyEndpointIsRetainedAsExternalEvidence()
    {
        var schema = new RelationalSchemaSnapshot(
            "partial",
            [Table("dbo", "Orders", "TABLE", Column("OrderID", true))],
            [ForeignKey("FK_Orders_Customers", "Orders", "Customers", "CustomerID", "CustomerID")],
            ["partial discovery"]);

        var model = RelationalInteractionModelBuilder.Build(schema);

        Assert.Equal(RelationalInteractionObjectRole.External, Object(model, "dbo.Customers").Role);
        Assert.Contains(model.Diagnostics, message => message.Contains("external evidence", StringComparison.Ordinal));
        Assert.Contains("partial discovery", model.Diagnostics);
    }

    [Fact]
    public void CanonicalJsonRoundTripsAndRejectsUnsupportedDocuments()
    {
        var model = RelationalInteractionModelBuilder.Build(Northwind(), "SqlServer");

        var json = RelationalInteractionModelJson.Serialize(model);
        var restored = RelationalInteractionModelJson.Deserialize(json);

        Assert.Equal(model.FormatVersion, restored.FormatVersion);
        Assert.Equal(model.Source, restored.Source);
        Assert.Equal(model.Objects.Count, restored.Objects.Count);
        Assert.Equal(model.Relations.Count, restored.Relations.Count);
        Assert.Equal(model.Diagnostics, restored.Diagnostics);
        Assert.Equal(json, RelationalInteractionModelJson.Serialize(restored));
        Assert.Contains("\"formatVersion\": \"1.0\"", json, StringComparison.Ordinal);
        Assert.Contains("\"role\": \"Association\"", json, StringComparison.Ordinal);
        Assert.Throws<ArgumentNullException>(() => RelationalInteractionModelJson.Serialize(null!));
        Assert.Throws<ArgumentException>(() => RelationalInteractionModelJson.Deserialize(" "));
        Assert.Throws<NotSupportedException>(() => RelationalInteractionModelJson.Deserialize(json.Replace("\"1.0\"", "\"2.0\"", StringComparison.Ordinal)));
        Assert.Throws<System.Text.Json.JsonException>(() => RelationalInteractionModelJson.Deserialize("null"));
    }

    [Theory]
    [InlineData(RelationalInteractionExportFormat.GraphMl)]
    [InlineData(RelationalInteractionExportFormat.Gexf)]
    public void XmlExportsAreWellFormedAndUseReadableDisplayDirection(RelationalInteractionExportFormat format)
    {
        using var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);

        RelationalInteractionModelExporter.Write(RelationalInteractionModelBuilder.Build(Northwind()), format, writer);
        var document = XDocument.Parse(writer.ToString());

        Assert.NotNull(document.Root);
        Assert.Contains("HAS_ORDER_DETAIL", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("dbo.Orders", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DotExportEscapesIdentifiersAndUnknownFormatFails()
    {
        var model = RelationalInteractionModelBuilder.Build(Northwind() with
        {
            Tables = [.. Northwind().Tables, Table("dbo", "Quoted\"Object", "TABLE", Column("Id", true))],
        });
        using var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);

        RelationalInteractionModelExporter.Write(model, RelationalInteractionExportFormat.Dot, writer);

        Assert.Contains("digraph RelationalInteractionNetwork", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("Quoted\\\"Object", writer.ToString(), StringComparison.Ordinal);
        Assert.Throws<ArgumentNullException>(() => RelationalInteractionModelExporter.Write(null!, RelationalInteractionExportFormat.Dot, writer));
        Assert.Throws<ArgumentNullException>(() => RelationalInteractionModelExporter.Write(model, RelationalInteractionExportFormat.Dot, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => RelationalInteractionModelExporter.Write(model, (RelationalInteractionExportFormat)99, writer));
        Assert.Throws<ArgumentNullException>(() => RelationalInteractionModelBuilder.Build(null!));
    }

    private static RelationalSchemaSnapshot Northwind()
    {
        var orderDetails = Table(
            "dbo",
            "OrderDetails",
            "TABLE",
            Column("OrderID", true, 1),
            Column("ProductID", true, 2),
            new RelationalColumn("Quantity", "smallint", false, 3, false));
        return new RelationalSchemaSnapshot(
            "Northwind",
            [
                Table("dbo", "Orders", "TABLE", Column("OrderID", true)),
                orderDetails,
                Table("dbo", "Products", "TABLE", Column("ProductID", true)),
                Table("dbo", "CurrentOrders", "VIEW", Column("OrderID", false)),
                Table("dbo", "ImportLog", "TABLE", Column("Message", false)),
                Table("dbo", "Categories", "TABLE", Column("CategoryID", true)),
                Table("dbo", "Status", "TABLE", Column("StatusID", true)),
            ],
            [
                ForeignKey("FK_OrderDetails_Orders", "OrderDetails", "Orders", "OrderID", "OrderID", RelationalReferentialAction.Cascade),
                ForeignKey("FK_OrderDetails_Products", "OrderDetails", "Products", "ProductID", "ProductID"),
            ],
            []);
    }

    private static RelationalForeignKey ForeignKey(
        string name,
        string source,
        string target,
        string sourceColumn,
        string targetColumn,
        RelationalReferentialAction onDelete = RelationalReferentialAction.NoAction) =>
        new(name, "dbo", source, "dbo", target)
        {
            Columns = [new RelationalForeignKeyColumn(sourceColumn, targetColumn, 1)],
            OnDelete = onDelete,
        };

    private static RelationalTable Table(string schema, string name, string kind, params RelationalColumn[] columns) =>
        new(schema, name, kind, columns);

    private static RelationalColumn Column(string name, bool primaryKey, int ordinal = 1) =>
        new(name, "int", false, ordinal, primaryKey);

    private static RelationalInteractionObject Object(RelationalInteractionModel model, string id) =>
        Assert.Single(model.Objects, item => item.Id == id);

    private static RelationalInteractionRelation Relation(RelationalInteractionModel model, string constraint) =>
        Assert.Single(model.Relations, item => item.ConstraintName == constraint);
}
