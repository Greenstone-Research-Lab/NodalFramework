namespace Nodal.Tool;

internal sealed record CliArguments(
    string Area,
    string Command,
    IReadOnlyDictionary<string, string> Options)
{
    public static CliArguments Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count < 2 || arguments[0] is not ("migrations" or "import" or "model"))
        {
            throw new CliUsageException("Expected 'nodal <migrations|import|model> <command>'.");
        }

        var area = arguments[0];
        var command = arguments[1];
        if (command.StartsWith("--", StringComparison.Ordinal))
        {
            throw new CliUsageException(area switch
            {
                "migrations" => "A migration command is required.",
                "import" => "An import command is required.",
                _ => "A model command is required.",
            });
        }

        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 2; index < arguments.Count; index += 2)
        {
            var name = arguments[index];
            if (!name.StartsWith("--", StringComparison.Ordinal) || index + 1 >= arguments.Count)
            {
                throw new CliUsageException("Options must use the '--name value' form.");
            }

            if (!options.TryAdd(name, arguments[index + 1]))
            {
                throw new CliUsageException($"Option '{name}' was specified more than once.");
            }
        }

        return new CliArguments(area, command, options);
    }

    public string Require(string name)
    {
        if (!Options.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new CliUsageException($"Required option '{name}' was not supplied.");
        }

        return value;
    }

    public string Optional(string name, string defaultValue) =>
        Options.TryGetValue(name, out var value) ? value : defaultValue;

    public void EnsureOnly(params string[] supported)
    {
        var allowed = supported.ToHashSet(StringComparer.Ordinal);
        var unknown = Options.Keys.FirstOrDefault(option => !allowed.Contains(option));
        if (unknown is not null)
        {
            throw new CliUsageException($"Unknown option '{unknown}'.");
        }
    }
}

internal sealed class CliUsageException(string message) : Exception(message);
