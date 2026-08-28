using System.Runtime.CompilerServices;

namespace Nodal.Import.Csv;

/// <summary>Represents one CSV source row addressed by its normalized headers.</summary>
public sealed class CsvImportRecord
{
    /// <summary>Initializes a record and normalizes every supplied column name.</summary>
    /// <param name="values">Source values keyed by original or normalized column name.</param>
    public CsvImportRecord(IReadOnlyDictionary<string, string?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        Values = values.ToDictionary(
            pair => CsvHeaderNormalizer.Normalize(pair.Key),
            pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Gets the normalized column values.</summary>
    public IReadOnlyDictionary<string, string?> Values { get; }

    /// <summary>Gets a column value when its normalized name is present.</summary>
    public bool TryGetValue(string columnName, out string? value) => Values.TryGetValue(CsvHeaderNormalizer.Normalize(columnName), out value);
}

/// <summary>Normalizes common relational and CSV names to PascalCase CLR property conventions.</summary>
public static class CsvHeaderNormalizer
{
    /// <summary>Normalizes names such as <c>customer_id</c> and <c>Customer ID</c> to <c>CustomerId</c>.</summary>
    public static string Normalize(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var words = name.Split(['_', '-', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 1)
        {
            return char.ToUpperInvariant(words[0][0]) + words[0][1..];
        }
        return string.Concat(words.Select(word => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()));
    }
}

/// <summary>Maps normalized CSV columns to writable POCO properties using a cached convention map.</summary>
/// <typeparam name="TRecord">Destination POCO type with a public parameterless constructor.</typeparam>
public sealed class CsvPocoMapper<TRecord>
    where TRecord : new()
{
    private static readonly Dictionary<string, System.Reflection.PropertyInfo> WritableProperties = typeof(TRecord)
        .GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
        .Where(property => property.CanWrite)
        .ToDictionary(property => CsvHeaderNormalizer.Normalize(property.Name), StringComparer.OrdinalIgnoreCase);

    /// <summary>Maps a CSV record by normalized property name and reports values that cannot be converted.</summary>
    /// <param name="source">Normalized CSV source record.</param>
    /// <returns>A mapped POCO and safe conversion diagnostics.</returns>
    public CsvPocoMappingResult<TRecord> Map(CsvImportRecord source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var destination = new TRecord();
        var diagnostics = new List<string>();
        foreach (var pair in source.Values)
        {
            if (!WritableProperties.TryGetValue(pair.Key, out var property))
            {
                continue;
            }
            if (!TryConvert(pair.Value, property.PropertyType, out var value))
            {
                diagnostics.Add($"Column '{pair.Key}' cannot be converted to '{property.PropertyType.Name}'.");
                continue;
            }
            property.SetValue(destination, value);
        }
        return new CsvPocoMappingResult<TRecord>(destination, diagnostics);
    }

    private static bool TryConvert(string? source, Type destinationType, out object? value)
    {
        var type = Nullable.GetUnderlyingType(destinationType) ?? destinationType;
        if (string.IsNullOrWhiteSpace(source))
        {
            value = Nullable.GetUnderlyingType(destinationType) is not null || !type.IsValueType ? null : Activator.CreateInstance(type);
            return true;
        }
        try
        {
            value = type == typeof(string) ? source : type.IsEnum ? Enum.Parse(type, source, ignoreCase: true) : Convert.ChangeType(source, type, System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidCastException or OverflowException)
        {
            value = null;
            return false;
        }
    }
}

/// <summary>Contains a convention-mapped POCO and any safe conversion diagnostics.</summary>
/// <typeparam name="TRecord">Destination POCO type.</typeparam>
public sealed record CsvPocoMappingResult<TRecord>(TRecord Record, IReadOnlyList<string> Diagnostics);

/// <summary>Reads RFC 4180-style comma-separated records without loading the entire source into memory.</summary>
public sealed class CsvImportReader
{
    /// <summary>Reads records from a UTF-8 text reader. The first record supplies the headers.</summary>
    /// <param name="reader">Input reader owned by the caller.</param>
    /// <param name="cancellationToken">Token that stops record production.</param>
    /// <returns>Asynchronous normalized CSV rows.</returns>
    public static async IAsyncEnumerable<CsvImportRecord> ReadAsync(
        TextReader reader,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var headers = await ReadRecordAsync(reader, cancellationToken).ConfigureAwait(false);
        if (headers is null)
        {
            yield break;
        }
        var normalized = headers.Select(CsvHeaderNormalizer.Normalize).ToArray();
        if (normalized.Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalized.Length)
        {
            throw new FormatException("CSV headers are not unique after Nodal convention normalization.");
        }

        while (await ReadRecordAsync(reader, cancellationToken).ConfigureAwait(false) is { } fields)
        {
            if (fields.Count != normalized.Length)
            {
                throw new FormatException($"CSV record contains {fields.Count} fields; expected {normalized.Length}.");
            }
            var values = normalized.Select((header, index) => new KeyValuePair<string, string?>(header, fields[index]))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            yield return new CsvImportRecord(values);
        }
    }

    private static async ValueTask<IReadOnlyList<string>?> ReadRecordAsync(TextReader reader, CancellationToken cancellationToken)
    {
        var fields = new List<string>();
        var field = new System.Text.StringBuilder();
        var quoted = false;
        var seen = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = reader.Read();
            if (value < 0)
            {
                if (!seen && field.Length == 0 && fields.Count == 0)
                {
                    return null;
                }
                if (quoted)
                {
                    throw new FormatException("CSV input ended inside a quoted field.");
                }
                fields.Add(field.ToString());
                return fields;
            }
            seen = true;
            var character = (char)value;
            if (character == '"')
            {
                if (quoted && reader.Peek() == '"')
                {
                    reader.Read();
                    field.Append('"');
                }
                else
                {
                    quoted = !quoted;
                }
                continue;
            }
            if (character == ',' && !quoted)
            {
                fields.Add(field.ToString());
                field.Clear();
                continue;
            }
            if ((character == '\n' || character == '\r') && !quoted)
            {
                if (character == '\r' && reader.Peek() == '\n')
                {
                    reader.Read();
                }
                fields.Add(field.ToString());
                return fields;
            }
            field.Append(character);
        }
    }
}
