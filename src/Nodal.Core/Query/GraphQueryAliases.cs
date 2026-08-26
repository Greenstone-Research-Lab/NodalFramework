namespace Nodal.Core.Query;

/// <summary>
/// Validates portable graph-query aliases before a provider compiler receives them.
/// </summary>
internal static class GraphQueryAliases
{
    public static string Validate(string alias, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias, parameterName);
        if (!char.IsLetter(alias[0]) && alias[0] != '_')
        {
            throw new ArgumentException("A graph query alias must begin with a letter or underscore.", parameterName);
        }

        if (alias.Any(character => !char.IsLetterOrDigit(character) && character != '_'))
        {
            throw new ArgumentException(
                "A graph query alias may contain only letters, digits, and underscores.",
                parameterName);
        }

        return alias;
    }
}
