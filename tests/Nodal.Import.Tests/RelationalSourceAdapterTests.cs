using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Nodal.Import.Relational;

namespace Nodal.Import.Tests;

public sealed class RelationalSourceAdapterTests
{
    [Theory]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    public async Task MetadataUsesOneSetBasedSequentialCommand(string provider)
    {
        await using var connection = new RecordingDbConnection(MetadataResults())
        {
            StateValue = ConnectionState.Open,
            DatabaseName = "food",
        };
        var adapter = Create(provider);

        var schema = await adapter.ReadAsync(connection);

        Assert.Equal(provider, adapter.ProviderName);
        Assert.Equal("food", schema.DatabaseName);
        Assert.Equal(2, schema.Tables.Count);
        var order = Assert.Single(schema.Tables, table => table.Name == "Orders");
        Assert.Equal(["Id", "CustomerId"], order.Columns.Select(column => column.Name));
        Assert.True(order.Columns[0].IsPrimaryKey);
        Assert.Equal("FK_Orders_Customers", Assert.Single(schema.ForeignKeys).Name);
        Assert.Empty(schema.Diagnostics);
        Assert.Equal(1, connection.ExecuteCount);
        Assert.Equal(CommandBehavior.SequentialAccess, connection.LastBehavior);
        Assert.Contains(provider == "SqlServer" ? "sys.foreign_keys" : "pg_catalog.pg_constraint", connection.LastCommandText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SqlServer", "SELECT TOP (@nodal_max_rows) [Id], [Display] FROM [sales].[Order]]History] ORDER BY [Id];")]
    [InlineData("PostgreSql", "SELECT \"Id\", \"Display\" FROM \"sales\".\"Order\"\"History\" ORDER BY \"Id\" LIMIT @nodal_max_rows;")]
    public async Task DataReadIsBoundedOrderedSequentialAndOrdinalCached(string provider, string expectedSql)
    {
        var rows = Table(("Id", typeof(int)), ("Display", typeof(string)));
        rows.Rows.Add(1, "Ada");
        rows.Rows.Add(2, DBNull.Value);
        await using var connection = new RecordingDbConnection([rows]) { StateValue = ConnectionState.Open };
        var request = new RelationalReadRequest(
            "sales",
            provider == "SqlServer" ? "Order]History" : "Order\"History",
            ["Id", "Display"],
            ["Id"],
            MaxRows: 25,
            CommandTimeoutSeconds: 7);

        var result = await ReadAsync(Create(provider).ReadRowsAsync(connection, request));

        Assert.Equal(2, result.Count);
        Assert.Equal(["Id", "Display"], result[0].Columns);
        Assert.Equal(2, result[0].Count);
        Assert.Equal(1, result[0][0]);
        Assert.Equal("Ada", result[0]["display"]);
        Assert.Null(result[1][1]);
        Assert.True(result[0].TryGetValue("Id", out var id));
        Assert.Equal(1, id);
        Assert.False(result[0].TryGetValue("missing", out var missing));
        Assert.Null(missing);
        Assert.Throws<KeyNotFoundException>(() => result[0]["missing"]);
        Assert.Equal(expectedSql, connection.LastCommandText);
        Assert.Equal(7, connection.LastCommandTimeout);
        Assert.Equal(25, Assert.Single(connection.LastParameters).Value);
        Assert.Equal(
            CommandBehavior.SequentialAccess | CommandBehavior.SingleResult,
            connection.LastBehavior);
        Assert.Equal(1, connection.ExecuteCount);
    }

    [Fact]
    public async Task MetadataWithoutForeignKeyResultReturnsEmptyCollection()
    {
        await using var connection = new RecordingDbConnection([MetadataTables()]) { StateValue = ConnectionState.Open };

        var schema = await new SqlServerRelationalSourceAdapter().ReadAsync(connection);

        Assert.NotEmpty(schema.Tables);
        Assert.Empty(schema.ForeignKeys);
    }

    [Fact]
    public async Task SourceAdaptersRejectClosedConnectionsNullsAndCancellation()
    {
        var adapter = new PostgreSqlRelationalSourceAdapter();
        await using var closed = new RecordingDbConnection([MetadataTables()]);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await adapter.ReadAsync(closed));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await adapter.ReadAsync(null!));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await ReadAsync(adapter.ReadRowsAsync(closed, Request())));

        await using var open = new RecordingDbConnection([DataRows()]) { StateValue = ConnectionState.Open };
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await ReadAsync(adapter.ReadRowsAsync(open, null!)));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await ReadAsync(adapter.ReadRowsAsync(open, Request(), cancellation.Token)));
    }

    [Fact]
    public void ReadRequestEnforcesDeterministicBoundsAndIdentifiers()
    {
        new RelationalReadRequest("dbo", "Orders", ["Id"], ["Id"]).Validate();
        Assert.Throws<ArgumentException>(() => new RelationalReadRequest("", "Orders", ["Id"], ["Id"]).Validate());
        Assert.Throws<ArgumentException>(() => new RelationalReadRequest("dbo", "", ["Id"], ["Id"]).Validate());
        Assert.Throws<ArgumentNullException>(() => new RelationalReadRequest("dbo", "Orders", null!, ["Id"]).Validate());
        Assert.Throws<ArgumentNullException>(() => new RelationalReadRequest("dbo", "Orders", ["Id"], null!).Validate());
        Assert.Throws<ArgumentException>(() => new RelationalReadRequest("dbo", "Orders", [], ["Id"]).Validate());
        Assert.Throws<ArgumentException>(() => new RelationalReadRequest("dbo", "Orders", ["Id"], []).Validate());
        Assert.Throws<ArgumentException>(() => new RelationalReadRequest("dbo", "Orders", ["Id", "id"], ["Id"]).Validate());
        Assert.Throws<ArgumentException>(() => new RelationalReadRequest("dbo", "Orders", ["Id"], ["Id", "id"]).Validate());
        Assert.Throws<ArgumentException>(() => new RelationalReadRequest("dbo", "Orders", ["Id"], ["Missing"]).Validate());
        Assert.Throws<ArgumentException>(() => new RelationalReadRequest("dbo", "Orders", [" "], [" "]).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new RelationalReadRequest("dbo", "Orders", ["Id"], ["Id"], 0).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new RelationalReadRequest("dbo", "Orders", ["Id"], ["Id"], CommandTimeoutSeconds: 0).Validate());
    }

    private static IRelationalSourceAdapter Create(string provider) => provider switch
    {
        "SqlServer" => new SqlServerRelationalSourceAdapter(),
        "PostgreSql" => new PostgreSqlRelationalSourceAdapter(),
        _ => throw new ArgumentOutOfRangeException(nameof(provider)),
    };

    private static RelationalReadRequest Request() => new("public", "orders", ["Id"], ["Id"]);

    private static DataTable[] MetadataResults() => [MetadataTables(), MetadataForeignKeys()];

    private static DataTable MetadataTables()
    {
        var table = Table(
            ("schema_name", typeof(string)),
            ("table_name", typeof(string)),
            ("table_kind", typeof(string)),
            ("column_name", typeof(string)),
            ("data_type", typeof(string)),
            ("is_nullable", typeof(bool)),
            ("ordinal_position", typeof(int)),
            ("is_primary_key", typeof(bool)));
        table.Rows.Add("dbo", "Orders", "TABLE", "CustomerId", "int", false, 2, false);
        table.Rows.Add("dbo", "Orders", "TABLE", "Id", "int", false, 1, true);
        table.Rows.Add("dbo", "Customers", "TABLE", "Id", "int", false, 1, true);
        return table;
    }

    private static DataTable MetadataForeignKeys()
    {
        var table = Table(
            ("constraint_name", typeof(string)),
            ("source_schema", typeof(string)),
            ("source_table", typeof(string)),
            ("target_schema", typeof(string)),
            ("target_table", typeof(string)));
        table.Rows.Add("FK_Orders_Customers", "dbo", "Orders", "dbo", "Customers");
        return table;
    }

    private static DataTable DataRows()
    {
        var table = Table(("Id", typeof(int)));
        table.Rows.Add(1);
        return table;
    }

    private static DataTable Table(params (string Name, Type Type)[] columns)
    {
        var table = new DataTable();
        foreach (var column in columns)
        {
            table.Columns.Add(column.Name, column.Type);
        }

        return table;
    }

    private static async Task<List<RelationalRow>> ReadAsync(IAsyncEnumerable<RelationalRow> source)
    {
        var rows = new List<RelationalRow>();
        await foreach (var row in source)
        {
            rows.Add(row);
        }

        return rows;
    }

    private sealed class RecordingDbConnection(IReadOnlyList<DataTable> results) : DbConnection
    {
        public ConnectionState StateValue { get; set; }

        public string DatabaseName { get; set; } = "test";

        public int ExecuteCount { get; private set; }

        public string LastCommandText { get; private set; } = string.Empty;

        public int LastCommandTimeout { get; private set; }

        public CommandBehavior LastBehavior { get; private set; }

        public IReadOnlyList<DbParameter> LastParameters { get; private set; } = [];

        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;

        public override string Database => DatabaseName;

        public override string DataSource => "recording";

        public override string ServerVersion => "1.0";

        public override ConnectionState State => StateValue;

        public override void ChangeDatabase(string databaseName) => DatabaseName = databaseName;

        public override void Close() => StateValue = ConnectionState.Closed;

        public override void Open() => StateValue = ConnectionState.Open;

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
            throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() => new RecordingDbCommand(this);

        public DataTableReader Execute(RecordingDbCommand command, CommandBehavior behavior)
        {
            ExecuteCount++;
            LastCommandText = command.CommandText;
            LastCommandTimeout = command.CommandTimeout;
            LastBehavior = behavior;
            LastParameters = command.Parameters.Cast<DbParameter>().ToArray();
            return new DataTableReader(results.ToArray());
        }
    }

    private sealed class RecordingDbCommand(RecordingDbConnection connection) : DbCommand
    {
        private readonly RecordingDbParameterCollection parameters = new();

        [AllowNull]
        public override string CommandText { get; set; } = string.Empty;

        public override int CommandTimeout { get; set; }

        public override CommandType CommandType { get; set; } = CommandType.Text;

        public override bool DesignTimeVisible { get; set; }

        public override UpdateRowSource UpdatedRowSource { get; set; }

        [AllowNull]
        protected override DbConnection DbConnection { get; set; } = connection;

        protected override DbParameterCollection DbParameterCollection => parameters;

        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel()
        {
        }

        public override int ExecuteNonQuery() => throw new NotSupportedException();

        public override object? ExecuteScalar() => throw new NotSupportedException();

        public override void Prepare()
        {
        }

        protected override DbParameter CreateDbParameter() => new RecordingDbParameter();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
            connection.Execute(this, behavior);

        protected override Task<DbDataReader> ExecuteDbDataReaderAsync(
            CommandBehavior behavior,
            CancellationToken cancellationToken) => cancellationToken.IsCancellationRequested
            ? Task.FromCanceled<DbDataReader>(cancellationToken)
            : Task.FromResult<DbDataReader>(connection.Execute(this, behavior));
    }

    private sealed class RecordingDbParameter : DbParameter
    {
        public override DbType DbType { get; set; }

        public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;

        public override bool IsNullable { get; set; }

        [AllowNull]
        public override string ParameterName { get; set; } = string.Empty;

        public override int Size { get; set; }

        [AllowNull]
        public override string SourceColumn { get; set; } = string.Empty;

        public override bool SourceColumnNullMapping { get; set; }

        public override object? Value { get; set; }

        public override void ResetDbType()
        {
        }
    }

    private sealed class RecordingDbParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> parameters = [];

        public override int Count => parameters.Count;

        public override object SyncRoot => ((ICollection)parameters).SyncRoot;

        public override int Add(object value)
        {
            parameters.Add((DbParameter)value);
            return parameters.Count - 1;
        }

        public override void AddRange(Array values)
        {
            foreach (var value in values)
            {
                Add(value!);
            }
        }

        public override void Clear() => parameters.Clear();

        public override bool Contains(object value) => parameters.Contains((DbParameter)value);

        public override bool Contains(string value) => IndexOf(value) >= 0;

        public override void CopyTo(Array array, int index) => ((ICollection)parameters).CopyTo(array, index);

        public override IEnumerator GetEnumerator() => parameters.GetEnumerator();

        public override int IndexOf(object value) => parameters.IndexOf((DbParameter)value);

        public override int IndexOf(string parameterName) => parameters.FindIndex(parameter =>
            string.Equals(parameter.ParameterName, parameterName, StringComparison.Ordinal));

        public override void Insert(int index, object value) => parameters.Insert(index, (DbParameter)value);

        public override void Remove(object value) => parameters.Remove((DbParameter)value);

        public override void RemoveAt(int index) => parameters.RemoveAt(index);

        public override void RemoveAt(string parameterName) => parameters.RemoveAt(IndexOf(parameterName));

        protected override DbParameter GetParameter(int index) => parameters[index];

        protected override DbParameter GetParameter(string parameterName) => parameters[IndexOf(parameterName)];

        protected override void SetParameter(int index, DbParameter value) => parameters[index] = value;

        protected override void SetParameter(string parameterName, DbParameter value)
        {
            var index = IndexOf(parameterName);
            if (index < 0)
            {
                parameters.Add(value);
            }
            else
            {
                parameters[index] = value;
            }
        }
    }
}
