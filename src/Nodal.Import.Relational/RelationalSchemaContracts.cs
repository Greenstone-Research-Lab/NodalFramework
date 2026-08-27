using System.Data;
using System.Data.Common;

namespace Nodal.Import.Relational;

/// <summary>Describes a provider-neutral snapshot of relational database metadata.</summary>
public sealed record RelationalSchemaSnapshot(
    string? DatabaseName,
    IReadOnlyList<RelationalTable> Tables,
    IReadOnlyList<RelationalForeignKey> ForeignKeys,
    IReadOnlyList<string> Diagnostics);

/// <summary>Describes a relational table or view.</summary>
public sealed record RelationalTable(string Schema, string Name, string Kind, IReadOnlyList<RelationalColumn> Columns);

/// <summary>Describes a relational column and its portable metadata.</summary>
public sealed record RelationalColumn(string Name, string DataType, bool IsNullable, int Ordinal, bool IsPrimaryKey);

/// <summary>Describes a foreign-key edge between two relational tables.</summary>
public sealed record RelationalForeignKey(string Name, string SourceSchema, string SourceTable, string TargetSchema, string TargetTable);

/// <summary>Reads database metadata through ADO.NET without exposing a vendor client from the import contract.</summary>
public interface IRelationalSchemaReader
{
    /// <summary>Discovers tables, columns, and foreign keys from an open connection.</summary>
    /// <param name="connection">Open ADO.NET connection.</param>
    /// <param name="cancellationToken">Token that cancels discovery.</param>
    /// <returns>A best-effort schema snapshot with explicit diagnostics for unavailable collections.</returns>
    ValueTask<RelationalSchemaSnapshot> ReadAsync(DbConnection connection, CancellationToken cancellationToken = default);
}

/// <summary>Uses standard ADO.NET schema collections to discover portable relational metadata.</summary>
public sealed class AdoNetRelationalSchemaReader : IRelationalSchemaReader
{
    /// <inheritdoc />
    public ValueTask<RelationalSchemaSnapshot> ReadAsync(DbConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        cancellationToken.ThrowIfCancellationRequested();
        if (connection.State != ConnectionState.Open)
        {
            throw new InvalidOperationException("Relational metadata discovery requires an open connection.");
        }

        var diagnostics = new List<string>();
        var tableData = ReadCollection(connection, "Tables", diagnostics);
        var columnData = ReadCollection(connection, "Columns", diagnostics);
        var foreignKeyData = ReadCollection(connection, "ForeignKeys", diagnostics);
        var primaryKeyData = ReadCollection(connection, "IndexColumns", diagnostics);
        var primaryKeys = ReadPrimaryKeys(primaryKeyData);
        var columns = ReadColumns(columnData, primaryKeys);
        var tables = ReadTables(tableData, columns);
        var foreignKeys = ReadForeignKeys(foreignKeyData);
        return ValueTask.FromResult(new RelationalSchemaSnapshot(connection.Database, tables, foreignKeys, diagnostics));
    }

    private static DataTable? ReadCollection(DbConnection connection, string collection, List<string> diagnostics)
    {
        try { return connection.GetSchema(collection); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or DbException)
        {
            diagnostics.Add($"Metadata collection '{collection}' is unavailable: {exception.GetType().Name}.");
            return null;
        }
    }

    private static HashSet<string> ReadPrimaryKeys(DataTable? data)
    {
        if (data is null) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return data.Rows.Cast<DataRow>()
            .Where(row => ReadBoolean(row, "PRIMARY_KEY") || ReadBoolean(row, "IS_PRIMARY_KEY"))
            .Select(row => Key(ReadString(row, "TABLE_SCHEMA"), ReadString(row, "TABLE_NAME"), ReadString(row, "COLUMN_NAME")))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, IReadOnlyList<RelationalColumn>> ReadColumns(DataTable? data, HashSet<string> primaryKeys)
    {
        if (data is null) return new Dictionary<string, IReadOnlyList<RelationalColumn>>();
        return data.Rows.Cast<DataRow>().GroupBy(row => Key(ReadString(row, "TABLE_SCHEMA"), ReadString(row, "TABLE_NAME")))
            .ToDictionary(group => group.Key, group => (IReadOnlyList<RelationalColumn>)group.Select(row => new RelationalColumn(
                ReadString(row, "COLUMN_NAME"), ReadString(row, "DATA_TYPE"), ReadBoolean(row, "IS_NULLABLE"), ReadInt32(row, "ORDINAL_POSITION"),
                primaryKeys.Contains(Key(ReadString(row, "TABLE_SCHEMA"), ReadString(row, "TABLE_NAME"), ReadString(row, "COLUMN_NAME")))))
                .OrderBy(column => column.Ordinal).ToArray(), StringComparer.OrdinalIgnoreCase);
    }

    private static RelationalTable[] ReadTables(DataTable? data, IReadOnlyDictionary<string, IReadOnlyList<RelationalColumn>> columns) =>
        data is null ? [] : data.Rows.Cast<DataRow>().Where(row =>
            string.Equals(ReadString(row, "TABLE_TYPE"), "TABLE", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ReadString(row, "TABLE_TYPE"), "VIEW", StringComparison.OrdinalIgnoreCase))
            .Select(row => new RelationalTable(ReadString(row, "TABLE_SCHEMA"), ReadString(row, "TABLE_NAME"), ReadString(row, "TABLE_TYPE"),
                columns.GetValueOrDefault(Key(ReadString(row, "TABLE_SCHEMA"), ReadString(row, "TABLE_NAME")), []))).ToArray();

    private static RelationalForeignKey[] ReadForeignKeys(DataTable? data) => data is null ? [] : data.Rows.Cast<DataRow>()
        .Select(row => new RelationalForeignKey(ReadString(row, "CONSTRAINT_NAME"), ReadString(row, "TABLE_SCHEMA"), ReadString(row, "TABLE_NAME"),
            ReadString(row, "REFERENCED_TABLE_SCHEMA"), ReadString(row, "REFERENCED_TABLE_NAME"))).ToArray();

    private static string ReadString(DataRow row, string name) => row.Table.Columns.Contains(name) && row[name] is not DBNull ? Convert.ToString(row[name], System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty : string.Empty;
    private static bool ReadBoolean(DataRow row, string name) => row.Table.Columns.Contains(name) && row[name] is not DBNull && Convert.ToBoolean(row[name], System.Globalization.CultureInfo.InvariantCulture);
    private static int ReadInt32(DataRow row, string name) => row.Table.Columns.Contains(name) && row[name] is not DBNull ? Convert.ToInt32(row[name], System.Globalization.CultureInfo.InvariantCulture) : 0;
    private static string Key(string schema, string table, string? column = null) => string.Join(".", [schema, table, column ?? string.Empty]);
}

/// <summary>Creates a reviewable graph-oriented draft from relational metadata; it never mutates a graph.</summary>
public static class RelationalGraphImportPlanBuilder
{
    /// <summary>Builds a deterministic node and relation draft from tables and foreign keys.</summary>
    /// <param name="schema">Relational metadata snapshot.</param>
    /// <returns>Human-reviewable node and relation proposals.</returns>
    public static RelationalGraphImportPlan Build(RelationalSchemaSnapshot schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return new RelationalGraphImportPlan(
            schema.Tables.Select(table => new GraphNodeImportProposal(table.Schema, table.Name, table.Columns.Select(column => column.Name).ToArray())).ToArray(),
            schema.ForeignKeys.Select(foreignKey => new GraphRelationImportProposal(foreignKey.Name, foreignKey.SourceTable, foreignKey.TargetTable)).ToArray());
    }
}

/// <summary>Contains a non-destructive graph import draft generated from relational metadata.</summary>
public sealed record RelationalGraphImportPlan(IReadOnlyList<GraphNodeImportProposal> Nodes, IReadOnlyList<GraphRelationImportProposal> Relations);

/// <summary>Proposes a graph node mapping for one relational table.</summary>
public sealed record GraphNodeImportProposal(string Schema, string Table, IReadOnlyList<string> Properties);

/// <summary>Proposes a graph relation mapping for one relational foreign key.</summary>
public sealed record GraphRelationImportProposal(string Name, string SourceTable, string TargetTable);
