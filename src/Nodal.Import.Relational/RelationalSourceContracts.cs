using System.Data.Common;

namespace Nodal.Import.Relational;

/// <summary>Defines one bounded, deterministic relational table read.</summary>
/// <param name="Schema">Source schema name.</param>
/// <param name="Table">Source table or view name.</param>
/// <param name="Columns">Columns returned in stable ordinal order.</param>
/// <param name="OrderByColumns">Columns that establish deterministic source order.</param>
/// <param name="MaxRows">Hard maximum number of rows returned by the provider.</param>
/// <param name="CommandTimeoutSeconds">Provider command timeout in seconds.</param>
public sealed record RelationalReadRequest(
    string Schema,
    string Table,
    IReadOnlyList<string> Columns,
    IReadOnlyList<string> OrderByColumns,
    int MaxRows = 10_000,
    int CommandTimeoutSeconds = 30)
{
    /// <summary>Validates identifiers and execution bounds before command creation.</summary>
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Schema);
        ArgumentException.ThrowIfNullOrWhiteSpace(Table);
        ArgumentNullException.ThrowIfNull(Columns);
        ArgumentNullException.ThrowIfNull(OrderByColumns);
        if (Columns.Count == 0)
        {
            throw new ArgumentException("At least one source column is required.", nameof(Columns));
        }

        if (OrderByColumns.Count == 0)
        {
            throw new ArgumentException("At least one deterministic order column is required.", nameof(OrderByColumns));
        }

        ValidateNames(Columns, nameof(Columns));
        ValidateNames(OrderByColumns, nameof(OrderByColumns));
        var selected = Columns.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (OrderByColumns.Any(column => !selected.Contains(column)))
        {
            throw new ArgumentException("Every order column must also be selected.", nameof(OrderByColumns));
        }

        if (MaxRows <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxRows), "The maximum row count must be greater than zero.");
        }

        if (CommandTimeoutSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(CommandTimeoutSeconds),
                "The command timeout must be greater than zero.");
        }
    }

    private static void ValidateNames(IReadOnlyList<string> names, string parameterName)
    {
        if (names.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Relational identifiers cannot be empty.", parameterName);
        }

        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count)
        {
            throw new ArgumentException("Relational identifiers must be unique.", parameterName);
        }
    }
}

/// <summary>Contains one immutable row read in provider ordinal order.</summary>
public sealed class RelationalRow
{
    private readonly RelationalRowShape shape;
    private readonly object?[] values;

    internal RelationalRow(RelationalRowShape shape, object?[] values)
    {
        this.shape = shape;
        this.values = values;
    }

    /// <summary>Gets the shared ordered column names for this result set.</summary>
    public IReadOnlyList<string> Columns => shape.Columns;

    /// <summary>Gets the number of values in this row.</summary>
    public int Count => values.Length;

    /// <summary>Gets a value by zero-based provider ordinal.</summary>
    public object? this[int ordinal] => values[ordinal];

    /// <summary>Gets a value by column name using a cached ordinal lookup.</summary>
    public object? this[string column] => values[shape.GetOrdinal(column)];

    /// <summary>Attempts to obtain a value without throwing for an unknown column.</summary>
    public bool TryGetValue(string column, out object? value)
    {
        if (shape.TryGetOrdinal(column, out var ordinal))
        {
            value = values[ordinal];
            return true;
        }

        value = null;
        return false;
    }
}

/// <summary>Reads normalized metadata and bounded rows from one relational database family.</summary>
public interface IRelationalSourceAdapter : IRelationalSchemaReader
{
    /// <summary>Gets the stable provider family name.</summary>
    string ProviderName { get; }

    /// <summary>Streams a bounded table result without materializing the complete source.</summary>
    /// <param name="connection">Open, externally owned pooled connection.</param>
    /// <param name="request">Bounded deterministic read request.</param>
    /// <param name="cancellationToken">Token that cancels command execution and row consumption.</param>
    /// <returns>Rows in deterministic source order.</returns>
    IAsyncEnumerable<RelationalRow> ReadRowsAsync(
        DbConnection connection,
        RelationalReadRequest request,
        CancellationToken cancellationToken = default);
}

internal sealed class RelationalRowShape
{
    private readonly Dictionary<string, int> ordinals;

    public RelationalRowShape(IReadOnlyList<string> columns)
    {
        Columns = columns;
        ordinals = columns.Select((column, ordinal) => new KeyValuePair<string, int>(column, ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<string> Columns { get; }

    public int GetOrdinal(string column) => ordinals.TryGetValue(column, out var ordinal)
        ? ordinal
        : throw new KeyNotFoundException($"Column '{column}' is not present in this relational row.");

    public bool TryGetOrdinal(string column, out int ordinal) => ordinals.TryGetValue(column, out ordinal);
}
