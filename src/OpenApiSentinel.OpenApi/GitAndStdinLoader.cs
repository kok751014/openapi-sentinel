using System.Net;
using System.Net.Sockets;

namespace OpenApiSentinel.OpenApi;

/// <summary>
/// Security policy for fetching remote documents and remote $ref targets.
/// </summary>
public sealed record RemoteFetchPolicy
{
    public bool Enabled { get; init; }

    /// <summary>Allow plain HTTP (default false: HTTPS only).</summary>
    public bool AllowHttp { get; init; }

    /// <summary>Allow requests to localhost / private / link-local network ranges (SSRF guard).</summary>
    public bool AllowPrivateNetwork { get; init; }

    /// <summary>Explicitly bypass HTTPS certificate validation. This dangerous option is
    /// independent from remote-loading and private-network opt-ins.</summary>
    public bool InsecureTls { get; init; }

    public IReadOnlySet<string>? AllowedHosts { get; init; }

    public int TimeoutSeconds { get; init; } = 30;

    public int MaxResponseBytes { get; init; } = 20 * 1024 * 1024;

    public static RemoteFetchPolicy Disabled() => new() { Enabled = false };
}

/// <summary>An authentication header resolved from an environment variable.</summary>
public sealed record EnvHeader(string HeaderName, string EnvironmentVariable)
{
    /// <summary>Resolves the actual value; never logged.</summary>
    public (string Name, string Value)? Resolve()
    {
        var v = System.Environment.GetEnvironmentVariable(EnvironmentVariable);
        return string.IsNullOrEmpty(v) ? null : (HeaderName, v);
    }
}

public sealed record LoadedSpec(
    string SourceName,
    string Content,
    string? BaseDirectoryOrNull,
    string? BaseUriOrNull = null);

/// <summary>
/// Loads specifications from files, URLs, stdin or git revisions.
/// </summary>
public sealed partial class SpecLoader
{
    private readonly RemoteFetchPolicy _remotePolicy;
    private readonly IReadOnlyList<EnvHeader> _envHeaders;

    public SpecLoader(RemoteFetchPolicy remotePolicy, IEnumerable<EnvHeader>? envHeaders = null)
    {
        _remotePolicy = remotePolicy;
        _envHeaders = envHeaders?.ToList() ?? [];
    }

    public async Task<LoadedSpec> LoadAsync(string source, CancellationToken ct = default)
    {
        if (source == "-" || source == "stdin")
            return await ReadStdinAsync();

        if (source.StartsWith("git:", StringComparison.Ordinal))
            return LoadFromGit(source);

        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            return await LoadFromUrlAsync(uri, ct);

        if (Uri.TryCreate(source, UriKind.Absolute, out var other) &&
            !string.IsNullOrEmpty(other.Scheme) && other.Scheme != Uri.UriSchemeFile)
            throw new SpecParseException(
                $"Unsupported source '{source}': only http(s) URLs, local paths, '-' (stdin) and 'git:<rev>:<path>' are accepted.");

        return LoadFromFile(source);
    }

    private static LoadedSpec LoadFromFile(string path)
    {
        if (!File.Exists(path))
            throw new SpecParseException($"Specification file not found: '{path}'.");
        string content;
        try
        {
            content = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new SpecParseException($"Cannot read specification file '{path}': {ex.Message}", ex);
        }
        var baseDir = Path.GetDirectoryName(Path.GetFullPath(path));
        return new LoadedSpec(path, content, string.IsNullOrEmpty(baseDir) ? null : baseDir);
    }

    private static async Task<LoadedSpec> ReadStdinAsync()
    {
        using var reader = new StreamReader(Console.OpenStandardInput());
        var content = await reader.ReadToEndAsync();
        return new LoadedSpec("<stdin>", content, null);
    }

    private static LoadedSpec LoadFromGit(string source)
    {
        // Format: git:<revision>:<path>   e.g. git:origin/main:swagger.json
        var body = source["git:".Length..];
        var sep = body.IndexOf(':');
        if (sep <= 0 || sep == body.Length - 1)
            throw new SpecParseException(
                $"Invalid git source '{source}'. Expected format: git:<revision>:<path>, e.g. git:origin/main:openapi.json.");

        var revision = body[..sep];
        var path = body[(sep + 1)..];

        var gitPath = FindGitBinary();
        if (gitPath is null)
            throw new SpecParseException(
                $"Cannot load '{source}': the 'git' executable was not found on PATH. Install Git or compare local/URL sources instead.");

        // Verify the file existed in that revision with a friendly error.
        var existsProbe = RunGit(gitPath, ["cat-file", "-e", $"{revision}:{path}"]);
        if (!existsProbe.Success)
            throw new SpecParseException(
                $"Cannot load '{source}': file '{path}' does not exist in revision '{revision}'. " +
                "Check the revision name and whether the file was renamed.");

        var show = RunGit(gitPath, ["show", $"{revision}:{path}"]);
        if (!show.Success)
            throw new SpecParseException(
                $"Cannot load '{source}': 'git show {revision}:{path}' failed: {show.Error.Trim()}");

        return new LoadedSpec($"{revision}:{path}", show.Output, null);
    }

    private static string? FindGitBinary()
    {
        var pathEnv = System.Environment.GetEnvironmentVariable("PATH") ?? "";
        var candidates = OperatingSystem.IsWindows()
            ? new[] { "git.exe", "git.cmd" }
            : new[] { "git" };
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            foreach (var candidate in candidates)
            {
                var p = Path.Combine(dir, candidate);
                if (File.Exists(p)) return p;
            }
        return null;
    }

    private static (bool Success, string Output, string Error) RunGit(string gitPath, string[] args)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = gitPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var proc = System.Diagnostics.Process.Start(psi)!;
            var output = proc.StandardOutput.ReadToEnd();
            var error = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            return (proc.ExitCode == 0, output, error);
        }
        catch (Exception ex)
        {
            return (false, "", ex.Message);
        }
    }
}
