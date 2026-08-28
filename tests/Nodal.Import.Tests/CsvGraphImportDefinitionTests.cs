using Nodal.Core.Mutations;
using Nodal.Import;
using Nodal.Import.Csv;

namespace Nodal.Import.Tests;

public sealed class CsvGraphImportDefinitionTests
{
    [Fact]
    public void DefinitionCompilesNormalizedColumnsIntoNodeAndRelationOperations()
    {
        var definition = CsvGraphImportDefinitionSerializer.Deserialize(ValidJson());
        var mapping = CsvGraphImportDefinitionCompiler.Compile(definition);
        var row = new CsvImportRecord(new Dictionary<string, string?>
        {
            ["customer_id"] = "customer-1",
            ["customer_name"] = "Ada",
            ["order_id"] = "order-1",
        });

        var result = new GraphImportPlanner<CsvImportRecord>().Plan(new GraphImportBatch<CsvImportRecord>(1, [row]), mapping);

        Assert.Equal(2, result.DryRun.PlannedNodeCount);
        Assert.Equal(1, result.DryRun.PlannedRelationCount);
        var customer = Assert.IsType<CreateNodeOperation>(result.MutationPlan.Operations[0]);
        Assert.Equal("customer-1", customer.Identity.Value);
        Assert.Equal("Ada", customer.Properties["Name"]);
        Assert.IsType<CreateRelationOperation>(result.MutationPlan.Operations[2]);
    }

    [Theory]
    [InlineData("", typeof(ArgumentException))]
    [InlineData("not-json", typeof(CsvGraphImportDefinitionException))]
    [InlineData("{\"formatVersion\":2,\"nodes\":[],\"relations\":[]}", typeof(CsvGraphImportDefinitionException))]
    [InlineData("{\"formatVersion\":1,\"nodes\":[],\"relations\":[]}", typeof(CsvGraphImportDefinitionException))]
    [InlineData("{\"formatVersion\":1,\"nodes\":null,\"relations\":[]}", typeof(CsvGraphImportDefinitionException))]
    [InlineData("{\"formatVersion\":1,\"nodes\":[null],\"relations\":[]}", typeof(CsvGraphImportDefinitionException))]
    [InlineData("{\"formatVersion\":1,\"nodes\":[{\"name\":\"n\",\"type\":\"N\",\"keyColumn\":\"id\",\"keyProperty\":\"Id\",\"properties\":null}],\"relations\":[]}", typeof(CsvGraphImportDefinitionException))]
    [InlineData("{\"formatVersion\":1,\"nodes\":[{\"name\":\"n\",\"type\":\"N\",\"keyColumn\":\"id\",\"keyProperty\":\"Id\",\"properties\":[]}],\"relations\":null}", typeof(CsvGraphImportDefinitionException))]
    public void DefinitionRejectsInvalidDocuments(string json, Type exceptionType)
    {
        var exception = Record.Exception(() => CsvGraphImportDefinitionSerializer.Deserialize(json));

        Assert.NotNull(exception);
        Assert.IsType(exceptionType, exception);
    }

    [Theory]
    [InlineData("name", "")]
    [InlineData("type", "")]
    [InlineData("keyColumn", " ")]
    [InlineData("keyProperty", "")]
    public void DefinitionRequiresEveryNodeIdentityField(string field, string value)
    {
        var json = ValidJson().Replace($"\"{field}\": \"customer\"", $"\"{field}\": \"{value}\"", StringComparison.Ordinal)
            .Replace($"\"{field}\": \"Customer\"", $"\"{field}\": \"{value}\"", StringComparison.Ordinal)
            .Replace($"\"{field}\": \"customer_id\"", $"\"{field}\": \"{value}\"", StringComparison.Ordinal)
            .Replace($"\"{field}\": \"Id\"", $"\"{field}\": \"{value}\"", StringComparison.Ordinal);

        Assert.Throws<CsvGraphImportDefinitionException>(() => CsvGraphImportDefinitionSerializer.Deserialize(json));
    }

    [Fact]
    public void DefinitionRejectsInvalidRelationAndPropertyShapes()
    {
        var invalidRelation = ValidJson().Replace("\"source\": \"customer\"", "\"source\": \"\"", StringComparison.Ordinal);
        var nullRelation = ValidJson().Replace(RelationJson(), "null", StringComparison.Ordinal);
        var nullProperty = ValidJson().Replace("{ \"column\": \"customer_name\", \"property\": \"Name\" }", "null", StringComparison.Ordinal);
        var emptyProperty = ValidJson().Replace("\"column\": \"customer_name\"", "\"column\": \"\"", StringComparison.Ordinal);

        Assert.Throws<CsvGraphImportDefinitionException>(() => CsvGraphImportDefinitionSerializer.Deserialize(invalidRelation));
        Assert.Throws<CsvGraphImportDefinitionException>(() => CsvGraphImportDefinitionSerializer.Deserialize(nullRelation));
        Assert.Throws<CsvGraphImportDefinitionException>(() => CsvGraphImportDefinitionSerializer.Deserialize(nullProperty));
        Assert.Throws<CsvGraphImportDefinitionException>(() => CsvGraphImportDefinitionSerializer.Deserialize(emptyProperty));
    }

    [Fact]
    public void DefinitionRejectsUnknownJsonMembersAndCompilerGuardsNull()
    {
        var unknown = ValidJson().Replace("\"formatVersion\": 1", "\"formatVersion\": 1, \"secret\": true", StringComparison.Ordinal);

        Assert.Throws<CsvGraphImportDefinitionException>(() => CsvGraphImportDefinitionSerializer.Deserialize(unknown));
        Assert.Throws<ArgumentNullException>(() => CsvGraphImportDefinitionCompiler.Compile(null!));
    }

    private static string ValidJson() => $$"""
        {
          "formatVersion": 1,
          "nodes": [
            {
              "name": "customer",
              "type": "Customer",
              "keyColumn": "customer_id",
              "keyProperty": "Id",
              "properties": [{ "column": "customer_name", "property": "Name" }]
            },
            {
              "name": "order",
              "type": "Order",
              "keyColumn": "order_id",
              "keyProperty": "Id",
              "properties": []
            }
          ],
          "relations": [{{RelationJson()}}]
        }
        """;

    private static string RelationJson() => """
        {
              "name": "placed",
              "source": "customer",
              "target": "order",
              "type": "PLACED",
              "directed": true,
              "properties": []
            }
        """;
}
