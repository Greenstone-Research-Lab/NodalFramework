using System.Runtime.CompilerServices;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Nodal.Import;
using Nodal.Import.Csv;
using Nodal.Import.Relational;

namespace Nodal.Import.Tests;

public sealed class ImportFoundationTests
{
    [Fact]
    public async Task RunnerBatchesRecordsAndAggregatesHandlerResults()
    {
        var handler = new RecordingHandler();
        var result = await new GraphImportRunner<int>(handler).RunAsync(Source(1, 2, 3), new GraphImportOptions(2));

        Assert.Equal([1L, 3L], handler.FirstRecordNumbers);
        Assert.Equal(3, result.ReadRecordCount);
        Assert.Equal(3, result.ImportedNodeCount);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task RunnerStopsAfterFailureInFailFastMode()
    {
        var handler = new RecordingHandler(fail: true);
        var result = await new GraphImportRunner<int>(handler).RunAsync(Source(1, 2, 3), new GraphImportOptions(1));

        Assert.Single(handler.FirstRecordNumbers);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task RunnerContinuesAfterFailuresAndValidatesOptions()
    {
        var handler = new RecordingHandler(fail: true);
        var result = await new GraphImportRunner<int>(handler).RunAsync(
            Source(1, 2, 3), new GraphImportOptions(1, GraphImportFailureMode.Continue));

        Assert.Equal([1L, 2L, 3L], handler.FirstRecordNumbers);
        Assert.Equal(3, result.Diagnostics.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => new GraphImportOptions(0).Validate());
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await new GraphImportRunner<int>(handler).RunAsync(null!));
    }

    [Fact]
    public async Task CsvReaderNormalizesHeadersAndSupportsQuotedCommas()
    {
        using var reader = new StringReader("customer_id,ordered_at,note\n42,2026-08-27,\"late, but warm\"\n");
        var records = new List<CsvImportRecord>();
        await foreach (var record in CsvImportReader.ReadAsync(reader)) records.Add(record);

        var result = Assert.Single(records);
        Assert.True(result.TryGetValue("customer_id", out var customerId));
        Assert.Equal("42", customerId);
        Assert.True(result.TryGetValue("Note", out var note));
        Assert.Equal("late, but warm", note);
        Assert.Equal("OrderedAt", CsvHeaderNormalizer.Normalize("ordered_at"));
    }

    [Fact]
    public async Task CsvReaderRejectsDuplicateNormalizedHeaders()
    {
        using var reader = new StringReader("customer_id,Customer Id\n1,2\n");
        var exception = await Assert.ThrowsAsync<FormatException>(async () =>
        {
            await foreach (var _ in CsvImportReader.ReadAsync(reader)) { }
        });
        Assert.Contains("not unique", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CsvReaderHandlesEmptyInputAndRejectsInvalidRowsAndQuotes()
    {
        using var empty = new StringReader(string.Empty);
        var records = new List<CsvImportRecord>();
        await foreach (var record in CsvImportReader.ReadAsync(empty)) records.Add(record);
        Assert.Empty(records);

        using var mismatched = new StringReader("Id,Name\n1\n");
        await Assert.ThrowsAsync<FormatException>(async () =>
        {
            await foreach (var _ in CsvImportReader.ReadAsync(mismatched)) { }
        });

        using var unclosed = new StringReader("Id\n\"1\n");
        await Assert.ThrowsAsync<FormatException>(async () =>
        {
            await foreach (var _ in CsvImportReader.ReadAsync(unclosed)) { }
        });
    }

    [Fact]
    public void CsvMapperUsesCachedConventionsAndReportsConversionErrors()
    {
        var source = new CsvImportRecord(new Dictionary<string, string?> { ["customer_id"] = "42", ["order_count"] = "not-a-number" });
        var result = new CsvPocoMapper<CustomerRow>().Map(source);

        Assert.Equal("42", result.Record.CustomerId);
        Assert.Equal(0, result.Record.OrderCount);
        Assert.Single(result.Diagnostics);
    }

    [Fact]
    public void CsvMapperConvertsEnumsNullsAndEscapedQuotes()
    {
        var source = new CsvImportRecord(new Dictionary<string, string?> { ["delivery_mode"] = "Bike", ["optional_count"] = null, ["unmapped"] = "ignored" });
        var mapped = new CsvPocoMapper<DeliveryRow>().Map(source);
        Assert.Equal(DeliveryMode.Bike, mapped.Record.DeliveryMode);
        Assert.Null(mapped.Record.OptionalCount);
        Assert.Empty(mapped.Diagnostics);
        Assert.Equal("CustomerId", CsvHeaderNormalizer.Normalize("CustomerId"));
        Assert.Equal("CustomerId", CsvHeaderNormalizer.Normalize("CUSTOMER_ID"));
        Assert.False(source.TryGetValue("missing", out _));
    }

    [Fact]
    public async Task CsvReaderSupportsEscapedQuotesCrLfAndFinalRecordWithoutNewline()
    {
        using var reader = new StringReader("id,note\r\n1,\"she said \"\"hello\"\"\"\r\n2,last");
        var records = new List<CsvImportRecord>();
        await foreach (var record in CsvImportReader.ReadAsync(reader)) records.Add(record);

        Assert.Equal(2, records.Count);
        Assert.True(records[0].TryGetValue("note", out var note));
        Assert.Equal("she said \"hello\"", note);
        Assert.True(records[1].TryGetValue("note", out var last));
        Assert.Equal("last", last);
    }

    [Fact]
    public void RelationalPlanTurnsTablesAndForeignKeysIntoReviewableProposals()
    {
        var schema = new RelationalSchemaSnapshot("food", [new RelationalTable("dbo", "Orders", "TABLE", [new RelationalColumn("Id", "int", false, 1, true)])],
            [new RelationalForeignKey("FK_Order_Customer", "dbo", "Orders", "dbo", "Customers")], []);
        var plan = RelationalGraphImportPlanBuilder.Build(schema);

        Assert.Equal("Orders", Assert.Single(plan.Nodes).Table);
        Assert.Equal("Customers", Assert.Single(plan.Relations).TargetTable);
        Assert.Throws<ArgumentNullException>(() => RelationalGraphImportPlanBuilder.Build(null!));
    }

    [Fact]
    public async Task RelationalReaderDiscoversTablesColumnsKeysAndForeignKeys()
    {
        await using var connection = new FakeConnection(BuildMetadataCollections()) { StateValue = ConnectionState.Open, DatabaseName = "food" };
        var schema = await new AdoNetRelationalSchemaReader().ReadAsync(connection);

        var order = Assert.Single(schema.Tables);
        Assert.Equal(("dbo", "Orders", "TABLE"), (order.Schema, order.Name, order.Kind));
        Assert.Equal(["Id", "CustomerId"], order.Columns.Select(column => column.Name));
        Assert.True(order.Columns[0].IsPrimaryKey);
        Assert.False(order.Columns[1].IsPrimaryKey);
        Assert.Equal("FK_Orders_Customers", Assert.Single(schema.ForeignKeys).Name);
        Assert.Empty(schema.Diagnostics);
    }

    [Fact]
    public async Task RelationalReaderFailsForClosedConnectionAndReportsUnavailableCollections()
    {
        await using var closed = new FakeConnection(new Dictionary<string, DataTable>()) { StateValue = ConnectionState.Closed };
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await new AdoNetRelationalSchemaReader().ReadAsync(closed));

        await using var open = new FakeConnection(new Dictionary<string, DataTable>()) { StateValue = ConnectionState.Open };
        var schema = await new AdoNetRelationalSchemaReader().ReadAsync(open);
        Assert.Empty(schema.Tables);
        Assert.Equal(4, schema.Diagnostics.Count);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await new AdoNetRelationalSchemaReader().ReadAsync(open, cancellation.Token));
    }

    private static async IAsyncEnumerable<int> Source(params int[] values)
    {
        foreach (var value in values)
        {
            yield return value;
            await Task.Yield();
        }
    }

    private sealed class RecordingHandler(bool fail = false) : IGraphImportBatchHandler<int>
    {
        public List<long> FirstRecordNumbers { get; } = [];

        public ValueTask<GraphImportBatchResult> HandleAsync(GraphImportBatch<int> batch, GraphImportOptions options, CancellationToken cancellationToken = default)
        {
            FirstRecordNumbers.Add(batch.FirstRecordNumber);
            return ValueTask.FromResult(new GraphImportBatchResult(batch.Records.Count, 0, fail ? [new GraphImportDiagnostic(batch.FirstRecordNumber, "ERROR-TEST", "Expected test failure.")] : []));
        }
    }

    private sealed class CustomerRow
    {
        public string CustomerId { get; set; } = string.Empty;

        public int OrderCount { get; set; }
    }

    private sealed class DeliveryRow
    {
        public DeliveryMode DeliveryMode { get; set; }

        public int? OptionalCount { get; set; }
    }

    private enum DeliveryMode { Bike, Car }

    private static Dictionary<string, DataTable> BuildMetadataCollections()
    {
        var tables = Table(("TABLE_SCHEMA", typeof(string)), ("TABLE_NAME", typeof(string)), ("TABLE_TYPE", typeof(string)));
        tables.Rows.Add("dbo", "Orders", "TABLE");
        tables.Rows.Add("dbo", "Ignored", "SYSTEM TABLE");
        var columns = Table(("TABLE_SCHEMA", typeof(string)), ("TABLE_NAME", typeof(string)), ("COLUMN_NAME", typeof(string)), ("DATA_TYPE", typeof(string)), ("IS_NULLABLE", typeof(bool)), ("ORDINAL_POSITION", typeof(int)));
        columns.Rows.Add("dbo", "Orders", "Id", "int", false, 1);
        columns.Rows.Add("dbo", "Orders", "CustomerId", "int", false, 2);
        var indexes = Table(("TABLE_SCHEMA", typeof(string)), ("TABLE_NAME", typeof(string)), ("COLUMN_NAME", typeof(string)), ("PRIMARY_KEY", typeof(bool)));
        indexes.Rows.Add("dbo", "Orders", "Id", true);
        var foreignKeys = Table(("CONSTRAINT_NAME", typeof(string)), ("TABLE_SCHEMA", typeof(string)), ("TABLE_NAME", typeof(string)), ("REFERENCED_TABLE_SCHEMA", typeof(string)), ("REFERENCED_TABLE_NAME", typeof(string)));
        foreignKeys.Rows.Add("FK_Orders_Customers", "dbo", "Orders", "dbo", "Customers");
        return new Dictionary<string, DataTable> { ["Tables"] = tables, ["Columns"] = columns, ["IndexColumns"] = indexes, ["ForeignKeys"] = foreignKeys };
    }

    private static DataTable Table(params (string Name, Type Type)[] columns)
    {
        var table = new DataTable();
        foreach (var column in columns) table.Columns.Add(column.Name, column.Type);
        return table;
    }

    private sealed class FakeConnection(IReadOnlyDictionary<string, DataTable> collections) : DbConnection
    {
        public ConnectionState StateValue { get; set; }
        public string DatabaseName { get; set; } = "test";
        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => DatabaseName;
        public override string DataSource => "fake";
        public override string ServerVersion => "1.0";
        public override ConnectionState State => StateValue;
        public override void ChangeDatabase(string databaseName) => DatabaseName = databaseName;
        public override void Close() => StateValue = ConnectionState.Closed;
        public override void Open() => StateValue = ConnectionState.Open;
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
        public override DataTable GetSchema(string collectionName) => collections.TryGetValue(collectionName, out var table) ? table : throw new NotSupportedException();
    }
}
