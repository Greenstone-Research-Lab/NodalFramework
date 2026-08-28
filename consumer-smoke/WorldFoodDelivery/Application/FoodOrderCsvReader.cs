using Nodal.Import.Csv;

namespace WorldFoodDelivery.Application;

internal sealed class FoodOrderCsvReader
{
    public async ValueTask<IReadOnlyList<FoodOrderCsvRow>> ReadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream);
        var mapper = new CsvPocoMapper<FoodOrderCsvRow>();
        var rows = new List<FoodOrderCsvRow>();

        await foreach (var record in CsvImportReader.ReadAsync(reader, cancellationToken))
        {
            var mapped = mapper.Map(record);
            if (mapped.Diagnostics.Count > 0)
            {
                throw new FormatException(string.Join(Environment.NewLine, mapped.Diagnostics));
            }

            rows.Add(mapped.Record);
        }

        return rows;
    }
}
