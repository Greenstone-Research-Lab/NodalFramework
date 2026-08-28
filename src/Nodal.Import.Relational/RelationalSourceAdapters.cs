using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;

namespace Nodal.Import.Relational;

/// <summary>Reads SQL Server catalog metadata and streams bounded table data.</summary>
public sealed class SqlServerRelationalSourceAdapter : RelationalSourceAdapter
{
    private const string SchemaSql = """
        WITH nodal_objects AS (
            SELECT object_id, schema_id, name, CAST('TABLE' AS nvarchar(16)) AS object_kind FROM sys.tables
            UNION ALL
            SELECT object_id, schema_id, name, CAST('VIEW' AS nvarchar(16)) AS object_kind FROM sys.views
        )
        SELECT s.name AS schema_name, o.name AS table_name, o.object_kind AS table_kind,
               c.name AS column_name, ty.name AS data_type, c.is_nullable,
               c.column_id AS ordinal_position,
               CAST(CASE WHEN pk.column_id IS NULL THEN 0 ELSE 1 END AS bit) AS is_primary_key
        FROM nodal_objects o
        INNER JOIN sys.schemas s ON s.schema_id = o.schema_id
        INNER JOIN sys.columns c ON c.object_id = o.object_id
        INNER JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        LEFT JOIN (
            SELECT ic.object_id, ic.column_id
            FROM sys.indexes i
            INNER JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            WHERE i.is_primary_key = 1
        ) pk ON pk.object_id = c.object_id AND pk.column_id = c.column_id
        ORDER BY s.name, o.name, c.column_id;

        SELECT fk.name AS constraint_name, source_schema.name AS source_schema,
               source_table.name AS source_table, target_schema.name AS target_schema,
               target_table.name AS target_table
        FROM sys.foreign_keys fk
        INNER JOIN sys.tables source_table ON source_table.object_id = fk.parent_object_id
        INNER JOIN sys.schemas source_schema ON source_schema.schema_id = source_table.schema_id
        INNER JOIN sys.tables target_table ON target_table.object_id = fk.referenced_object_id
        INNER JOIN sys.schemas target_schema ON target_schema.schema_id = target_table.schema_id
        ORDER BY source_schema.name, source_table.name, fk.name;
        """;

    /// <inheritdoc />
    public override string ProviderName => "SqlServer";

    /// <inheritdoc/>
    protected override string MetadataCommandText => SchemaSql;

    /// <inheritdoc/>
    protected override string BuildDataCommandText(RelationalReadRequest request) =>
        $"SELECT TOP (@nodal_max_rows) {JoinIdentifiers(request.Columns)} " +
        $"FROM {Quote(request.Schema)}.{Quote(request.Table)} " +
        $"ORDER BY {JoinIdentifiers(request.OrderByColumns)};";

    /// <inheritdoc/>
    protected override string Quote(string identifier) => $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
}

/// <summary>Reads PostgreSQL catalog metadata and streams bounded table data.</summary>
public sealed class PostgreSqlRelationalSourceAdapter : RelationalSourceAdapter
{
    private const string SchemaSql = """
        WITH nodal_objects AS (
            SELECT c.oid, c.relnamespace, c.relname,
                   CASE WHEN c.relkind IN ('v', 'm') THEN 'VIEW' ELSE 'TABLE' END AS object_kind
            FROM pg_catalog.pg_class c
            WHERE c.relkind IN ('r', 'p', 'v', 'm')
        )
        SELECT n.nspname AS schema_name, o.relname AS table_name, o.object_kind AS table_kind,
               a.attname AS column_name, pg_catalog.format_type(a.atttypid, a.atttypmod) AS data_type,
               NOT a.attnotnull AS is_nullable, a.attnum AS ordinal_position,
               EXISTS (
                   SELECT 1 FROM pg_catalog.pg_index i
                   WHERE i.indrelid = o.oid AND i.indisprimary AND a.attnum = ANY(i.indkey)
               ) AS is_primary_key
        FROM nodal_objects o
        INNER JOIN pg_catalog.pg_namespace n ON n.oid = o.relnamespace
        INNER JOIN pg_catalog.pg_attribute a ON a.attrelid = o.oid
        WHERE a.attnum > 0 AND NOT a.attisdropped
          AND n.nspname NOT IN ('pg_catalog', 'information_schema')
        ORDER BY n.nspname, o.relname, a.attnum;

        SELECT con.conname AS constraint_name, source_schema.nspname AS source_schema,
               source_table.relname AS source_table, target_schema.nspname AS target_schema,
               target_table.relname AS target_table
        FROM pg_catalog.pg_constraint con
        INNER JOIN pg_catalog.pg_class source_table ON source_table.oid = con.conrelid
        INNER JOIN pg_catalog.pg_namespace source_schema ON source_schema.oid = source_table.relnamespace
        INNER JOIN pg_catalog.pg_class target_table ON target_table.oid = con.confrelid
        INNER JOIN pg_catalog.pg_namespace target_schema ON target_schema.oid = target_table.relnamespace
        WHERE con.contype = 'f'
        ORDER BY source_schema.nspname, source_table.relname, con.conname;
        """;

    /// <inheritdoc />
    public override string ProviderName => "PostgreSql";

    /// <inheritdoc/>
    protected override string MetadataCommandText => SchemaSql;

    /// <inheritdoc/>
    protected override string BuildDataCommandText(RelationalReadRequest request) =>
        $"SELECT {JoinIdentifiers(request.Columns)} " +
        $"FROM {Quote(request.Schema)}.{Quote(request.Table)} " +
        $"ORDER BY {JoinIdentifiers(request.OrderByColumns)} LIMIT @nodal_max_rows;";

    /// <inheritdoc/>
    protected override string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}

/// <summary>Provides the shared, allocation-conscious relational source execution pipeline.</summary>
public abstract class RelationalSourceAdapter : IRelationalSourceAdapter
{
    /// <inheritdoc />
    public abstract string ProviderName { get; }

    /// <inheritdoc/>
    protected abstract string MetadataCommandText { get; }

    /// <inheritdoc />
    public async ValueTask<RelationalSchemaSnapshot> ReadAsync(
        DbConnection connection,
        CancellationToken cancellationToken = default)
    {
        ValidateOpen(connection);
        await using var command = connection.CreateCommand();
        command.CommandText = MetadataCommandText;
        await using var reader = await command.ExecuteReaderAsync(
            CommandBehavior.SequentialAccess,
            cancellationToken).ConfigureAwait(false);
        var tables = await ReadTablesAsync(reader, cancellationToken).ConfigureAwait(false);
        var foreignKeys = await reader.NextResultAsync(cancellationToken).ConfigureAwait(false)
            ? await ReadForeignKeysAsync(reader, cancellationToken).ConfigureAwait(false)
            : [];
        return new RelationalSchemaSnapshot(connection.Database, tables, foreignKeys, []);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<RelationalRow> ReadRowsAsync(
        DbConnection connection,
        RelationalReadRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ValidateOpen(connection);
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        await using var command = connection.CreateCommand();
        command.CommandText = BuildDataCommandText(request);
        command.CommandTimeout = request.CommandTimeoutSeconds;
        var limit = command.CreateParameter();
        limit.ParameterName = "@nodal_max_rows";
        limit.DbType = DbType.Int32;
        limit.Value = request.MaxRows;
        command.Parameters.Add(limit);

        await using var reader = await command.ExecuteReaderAsync(
            CommandBehavior.SequentialAccess | CommandBehavior.SingleResult,
            cancellationToken).ConfigureAwait(false);
        var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        var shape = new RelationalRowShape(columns);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var values = new object?[reader.FieldCount];
            for (var ordinal = 0; ordinal < values.Length; ordinal++)
            {
                values[ordinal] = await reader.IsDBNullAsync(ordinal, cancellationToken).ConfigureAwait(false)
                    ? null
                    : reader.GetValue(ordinal);
            }

            yield return new RelationalRow(shape, values);
        }
    }

    /// <inheritdoc/>
    protected abstract string BuildDataCommandText(RelationalReadRequest request);

    /// <inheritdoc/>
    protected abstract string Quote(string identifier);

    /// <inheritdoc/>
    protected string JoinIdentifiers(IEnumerable<string> identifiers) => string.Join(", ", identifiers.Select(Quote));

    private static async ValueTask<IReadOnlyList<RelationalTable>> ReadTablesAsync(
        DbDataReader reader,
        CancellationToken cancellationToken)
    {
        var ordinals = MetadataOrdinals.Create(reader);
        var tables = new Dictionary<(string Schema, string Table), TableAccumulator>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var schema = reader.GetString(ordinals.Schema);
            var table = reader.GetString(ordinals.Table);
            var key = (schema, table);
            if (!tables.TryGetValue(key, out var accumulator))
            {
                accumulator = new TableAccumulator(schema, table, reader.GetString(ordinals.Kind));
                tables.Add(key, accumulator);
            }

            accumulator.Columns.Add(new RelationalColumn(
                reader.GetString(ordinals.Column),
                reader.GetString(ordinals.DataType),
                reader.GetBoolean(ordinals.Nullable),
                reader.GetInt32(ordinals.Position),
                reader.GetBoolean(ordinals.PrimaryKey)));
        }

        return tables.Values.Select(table => new RelationalTable(
                table.Schema,
                table.Table,
                table.Kind,
                table.Columns.OrderBy(column => column.Ordinal).ToArray()))
            .ToArray();
    }

    private static async ValueTask<IReadOnlyList<RelationalForeignKey>> ReadForeignKeysAsync(
        DbDataReader reader,
        CancellationToken cancellationToken)
    {
        var name = reader.GetOrdinal("constraint_name");
        var sourceSchema = reader.GetOrdinal("source_schema");
        var sourceTable = reader.GetOrdinal("source_table");
        var targetSchema = reader.GetOrdinal("target_schema");
        var targetTable = reader.GetOrdinal("target_table");
        var foreignKeys = new List<RelationalForeignKey>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            foreignKeys.Add(new RelationalForeignKey(
                reader.GetString(name),
                reader.GetString(sourceSchema),
                reader.GetString(sourceTable),
                reader.GetString(targetSchema),
                reader.GetString(targetTable)));
        }

        return foreignKeys;
    }

    private static void ValidateOpen(DbConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.State != ConnectionState.Open)
        {
            throw new InvalidOperationException("Relational source access requires an open, externally managed connection.");
        }
    }

    private sealed class TableAccumulator(string schema, string table, string kind)
    {
        public string Schema { get; } = schema;

        public string Table { get; } = table;

        public string Kind { get; } = kind;

        public List<RelationalColumn> Columns { get; } = [];
    }

    private sealed record MetadataOrdinals(
        int Schema,
        int Table,
        int Kind,
        int Column,
        int DataType,
        int Nullable,
        int Position,
        int PrimaryKey)
    {
        public static MetadataOrdinals Create(DbDataReader reader) => new(
            reader.GetOrdinal("schema_name"),
            reader.GetOrdinal("table_name"),
            reader.GetOrdinal("table_kind"),
            reader.GetOrdinal("column_name"),
            reader.GetOrdinal("data_type"),
            reader.GetOrdinal("is_nullable"),
            reader.GetOrdinal("ordinal_position"),
            reader.GetOrdinal("is_primary_key"));
    }
}
