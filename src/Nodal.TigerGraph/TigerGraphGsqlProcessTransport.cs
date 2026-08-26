using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Nodal.Core.Migrations;

namespace Nodal.TigerGraph;

/// <summary>Configures a supported GSQL command-line process used for schema administration.</summary>
public sealed class TigerGraphGsqlProcessOptions
{
    /// <summary>Gets the executable to start, such as <c>gsql</c>, <c>java</c>, or <c>docker</c>.</summary>
    public required string FileName { get; init; }

    /// <summary>
    /// Gets arguments inserted before GSQL authentication and command arguments. This supports
    /// remote-client jars and containerized clients such as <c>docker exec container gsql</c>.
    /// </summary>
    public IReadOnlyList<string> PrefixArguments { get; init; } = [];

    /// <summary>Gets the optional GSQL username.</summary>
    public string? Username { get; init; }

    /// <summary>Gets the optional GSQL password.</summary>
    public string? Password { get; init; }

    /// <summary>Gets the optional GSQL authentication token, preferred over username/password.</summary>
    public string? AccessToken { get; init; }

    /// <summary>Gets the optional graph scope supplied with <c>-g</c>.</summary>
    public string? GraphName { get; init; }

    /// <summary>Gets the optional process working directory.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>Gets the server version verified by deployment configuration.</summary>
    public string VerifiedServerVersion { get; init; } = "unknown";
}

/// <summary>
/// Executes privileged migration commands through TigerGraph's documented GSQL command-line client.
/// </summary>
/// <remarks>
/// For the local Docker stack, configure <c>FileName = "docker"</c> and prefix arguments
/// <c>["exec", "nodal-tigergraph", "gsql"]</c>. Managed deployments can continue to supply
/// a platform-specific <see cref="ITigerGraphAdministrativeTransport"/>.
/// </remarks>
public sealed class TigerGraphGsqlProcessTransport : ITigerGraphAdministrativeControlPlane
{
    private static readonly TimeSpan IdentifierRegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> MigrationLocks =
        new(StringComparer.Ordinal);
    private readonly TigerGraphGsqlProcessOptions options;

    /// <summary>Initializes a process transport with an explicit executable and argument policy.</summary>
    public TigerGraphGsqlProcessTransport(TigerGraphGsqlProcessOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.FileName);
        if (string.IsNullOrWhiteSpace(options.Username) != string.IsNullOrWhiteSpace(options.Password))
        {
            throw new ArgumentException("GSQL username and password must be supplied together.", nameof(options));
        }

        this.options = options;
    }

    /// <inheritdoc />
    public async ValueTask ExecuteAsync(MigrationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        var result = await RunAsync(command.Text, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result);
    }

    /// <inheritdoc />
    public async ValueTask<TigerGraphAdministrativeCapabilities> DiscoverCapabilitiesAsync(
        string graphName,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(graphName, nameof(graphName));
        var result = await RunAsync($"SHOW GRAPH {graphName}", cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result);
        return new TigerGraphAdministrativeCapabilities(
            options.VerifiedServerVersion,
            CanReadSchema: true,
            CanWriteSchema: true,
            CanInspectJobs: true,
            CanCleanupJobs: true,
            TigerGraphMigrationLockScope.Process);
    }

    /// <inheritdoc />
    public async ValueTask<bool> SchemaJobExistsAsync(
        string graphName,
        string jobName,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(graphName, nameof(graphName));
        ValidateIdentifier(jobName, nameof(jobName));
        var result = await RunAsync($"USE GRAPH {graphName}\nSHOW JOB {jobName}", cancellationToken)
            .ConfigureAwait(false);
        var diagnostic = $"{result.StandardOutput}\n{result.StandardError}";
        if (diagnostic.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
            diagnostic.Contains("doesn't exist", StringComparison.OrdinalIgnoreCase) ||
            diagnostic.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
            diagnostic.Contains("cannot find", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        EnsureSuccess(result);
        return true;
    }

    /// <inheritdoc />
    public async ValueTask<IAsyncDisposable> AcquireMigrationLockAsync(
        string graphName,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(graphName, nameof(graphName));
        var semaphore = MigrationLocks.GetOrAdd(graphName, static _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new SemaphoreLease(semaphore);
    }

    private async ValueTask<ProcessResult> RunAsync(string commandText, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = options.FileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (!string.IsNullOrWhiteSpace(options.WorkingDirectory))
        {
            startInfo.WorkingDirectory = options.WorkingDirectory;
        }

        foreach (var argument in options.PrefixArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        if (!string.IsNullOrWhiteSpace(options.AccessToken))
        {
            startInfo.ArgumentList.Add("--token");
            startInfo.ArgumentList.Add(options.AccessToken);
        }
        else if (!string.IsNullOrWhiteSpace(options.Username))
        {
            startInfo.ArgumentList.Add("-u");
            startInfo.ArgumentList.Add(options.Username);
            startInfo.ArgumentList.Add("-p");
            startInfo.ArgumentList.Add(options.Password!);
        }
        if (!string.IsNullOrWhiteSpace(options.GraphName))
        {
            startInfo.ArgumentList.Add("-g");
            startInfo.ArgumentList.Add(options.GraphName);
        }
        startInfo.ArgumentList.Add(commandText);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException(
            $"GSQL administrative process '{options.FileName}' could not be started.");
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        var output = await standardOutput.ConfigureAwait(false);
        var error = await standardError.ConfigureAwait(false);
        return new ProcessResult(process.ExitCode, output, error);
    }

    private static void EnsureSuccess(ProcessResult result)
    {
        if (result.ExitCode == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"GSQL administrative command failed with exit code {result.ExitCode}. " +
            $"{(string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError)}");
    }

    private static void ValidateIdentifier(string identifier, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier, parameterName);
        if (!Regex.IsMatch(
                identifier,
                "^[A-Za-z_][A-Za-z0-9_]*$",
                RegexOptions.CultureInvariant,
                IdentifierRegexTimeout))
        {
            throw new ArgumentException($"'{identifier}' is not a valid TigerGraph identifier.", parameterName);
        }
    }

    private sealed class SemaphoreLease(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        private int disposed;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                semaphore.Release();
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
