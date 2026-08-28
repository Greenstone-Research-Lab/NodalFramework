using System.Reflection;

namespace Nodal.Tool;

internal static class CliCompositionHostLoader
{
    public static THost Load<THost>(
        Func<string, string?> readVariable,
        string assemblyVariable,
        string typeVariable,
        string purpose,
        string contractError,
        string creationError)
        where THost : class
    {
        ArgumentNullException.ThrowIfNull(readVariable);
        var assemblyPath = readVariable(assemblyVariable);
        var typeName = readVariable(typeVariable);
        if (string.IsNullOrWhiteSpace(assemblyPath) || string.IsNullOrWhiteSpace(typeName))
        {
            throw new InvalidOperationException(
                $"{purpose} requires trusted host variables '{assemblyVariable}' and '{typeVariable}'.");
        }

        var assembly = Assembly.LoadFrom(Path.GetFullPath(assemblyPath));
        var type = assembly.GetType(typeName, throwOnError: false, ignoreCase: false);
        if (type is null || type.IsAbstract || !typeof(THost).IsAssignableFrom(type))
        {
            throw new InvalidOperationException(contractError);
        }

        try
        {
            return (THost)(Activator.CreateInstance(type)
                ?? throw new InvalidOperationException(creationError));
        }
        catch (TargetInvocationException exception)
        {
            throw new InvalidOperationException(creationError, exception.InnerException ?? exception);
        }
    }
}
