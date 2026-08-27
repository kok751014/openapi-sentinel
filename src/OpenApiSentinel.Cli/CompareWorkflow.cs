using OpenApiSentinel.Core;
using OpenApiSentinel.OpenApi;
using OpenApiSentinel.Reporting;
using OpenApiSentinel.Rules;

namespace OpenApiSentinel.Cli;

/// <summary>
/// Runs the full compare workflow and renders every requested output artifact.
/// Returns the process exit code for the comparison.
/// </summary>
public static class CompareWorkflow
{
    public sealed record Request
    {
        public required string OldSource { get; init; }

        public required string NewSource { get; init; }

        public SentinelConfiguration Config { get; init; } = new();

        /// <summary>Extra output paths from the command line, keyed by format; override config paths.</summary>
        public IReadOnlyDictionary<string, string> OutputPaths { get; init; } =
            new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Primary console format: console|json|markdown|html.</summary>
        public string Format { get; init; } = "console";

        public bool NoColor { get; init; }

        public bool Verbose { get; init; }

        public bool AllowRemoteOverride { get; init; }

        public bool AllowPrivateNetworkOverride { get; init; }

        public bool InsecureTlsOverride { get; init; }

        /// <summary>Advance a local OLD source to the raw NEW entry document only after
        /// every report succeeds and the configured comparison policy passes.</summary>
        public bool UpdateSnapshot { get; init; }

        public FailurePolicy? FailOnOverride { get; init; }

        public string? BaselinePathOverride { get; init; }

        public string? UpdateBaselinePath { get; init; }

        public bool ReportUnknownKinds { get; init; }

        /// <summary>Omit the suppressed-findings section from reports.</summary>
        public bool HideSuppressed { get; init; }

        /// <summary>Language for HTML output only.</summary>
        public HtmlLanguage HtmlLanguage { get; init; } = HtmlLanguage.ZhTw;
    }

    public static async Task<int> RunAsync(Request request, IConsoleWriter console, CancellationToken ct = default)
    {
        var config = request.Config;

        if (request.UpdateSnapshot)
        {
            ValidateSnapshotRequest(request);
        }

        RemoteSecurityWarnings.WriteIfInsecure(config.RemoteReferences, request.InsecureTlsOverride, console);

        var oldOutcome = await AnalysisPipeline.LoadDocumentAsync(
            request.OldSource, config.RemoteReferences, config.HeaderEnv, request.AllowRemoteOverride,
            request.AllowPrivateNetworkOverride, request.InsecureTlsOverride,
            config.Limits, config.AllowAboveDocumentTree, ct);
        var newOutcome = await AnalysisPipeline.LoadDocumentAsync(
            request.NewSource, config.RemoteReferences, config.HeaderEnv, request.AllowRemoteOverride,
            request.AllowPrivateNetworkOverride, request.InsecureTlsOverride,
            config.Limits, config.AllowAboveDocumentTree, ct);
        var oldDoc = oldOutcome.Document;
        var newDoc = newOutcome.Document;

        var changes = new ComparisonEngine().Compare(oldDoc, newDoc);

        // Baseline: CLI --baseline wins over config baseline path.
        var baselinePath = request.BaselinePathOverride ?? config.BaselinePath;
        var fingerprints = baselinePath is null ? [] : BaselineStore.Read(baselinePath);

        // CLI flag wins over config; config wins over the built-in default.
        var policy = request.FailOnOverride ?? config.Policy ?? FailurePolicy.Default();

        var engine = new RulesEngine(RuleRegistry.CreateDefault());
        var evaluation = engine.Evaluate(changes, new EngineOptions
        {
            OldContractName = DisplayName(request.OldSource),
            NewContractName = DisplayName(request.NewSource),
            OldOpenApiVersion = oldDoc.RawVersion,
            NewOpenApiVersion = newDoc.RawVersion,
            OldInfoVersion = oldDoc.InfoVersion,
            NewInfoVersion = newDoc.InfoVersion,
            ToolVersion = ToolVersion.Get(),
            Overrides = config.Overrides,
            IgnorePathGlobs = config.IgnorePaths,
            Suppressions = config.Suppressions,
            BaselineFingerprints = fingerprints,
            Policy = policy,
            ReportUnknownChangeKinds = request.ReportUnknownKinds,
        });

        // --update-baseline keeps existing baseline entries and adds the currently reported findings.
        if (request.UpdateBaselinePath is { } updatePath)
        {
            var current = evaluation.Result.Findings.Select(f => f.Fingerprint);
            BaselineStore.Write(updatePath, fingerprints.Concat(current));
            console.Out($"Baseline written: {updatePath} ({fingerprints.Count + evaluation.Result.Findings.Count} entries).");
        }

        var snapshotStatus = request.UpdateSnapshot
            ? (evaluation.Result.PolicyViolated ? HtmlSnapshotStatus.PolicyFailed : HtmlSnapshotStatus.Pending)
            : HtmlSnapshotStatus.NotRequested;
        RenderOutputs(evaluation.Result, request, console, snapshotStatus);

        if (evaluation.Result.PolicyViolated)
        {
            if (request.UpdateSnapshot)
            {
                console.Out("Snapshot not updated because the comparison policy failed.");
            }
            return ExitCodes.PolicyViolated;
        }

        if (request.UpdateSnapshot)
        {
            // A loaded entry is the literal source body, never the normalized/resolved model.
            // This write is deliberately last, after every requested report completed.
            var loaded = newOutcome.Loaded ?? throw new SentinelConfigException(
                "Cannot update snapshot: the NEW source did not provide a raw entry document.");
            AtomicFileWriter.Write(request.OldSource, loaded.Content);
            // Keep the initial report valid until the snapshot has been atomically advanced,
            // then atomically replace only HTML artifacts with their final presentation status.
            FinalizeHtmlArtifacts(evaluation.Result, request);
            console.Out($"Snapshot updated:{Environment.NewLine}  {request.OldSource}{Environment.NewLine}Source:{Environment.NewLine}  {request.NewSource}");
        }

        return ExitCodes.Ok;
    }

    private static void ValidateSnapshotRequest(Request request)
    {
        if (request.OldSource is "-" or "stdin" || request.OldSource.StartsWith("git:", StringComparison.Ordinal) ||
            (Uri.TryCreate(request.OldSource, UriKind.Absolute, out var uri) &&
             uri.Scheme is "http" or "https"))
        {
            throw new SentinelConfigException(
                "--update-snapshot requires OLD-SPEC to be an existing local file path; URL, stdin, and git sources do not have a safe snapshot destination.");
        }

        if (!File.Exists(request.OldSource))
        {
            throw new SentinelConfigException($"--update-snapshot requires an existing local OLD-SPEC file: '{request.OldSource}'.");
        }

        var oldPath = NormalizePathForCollision(request.OldSource);
        var outputs = MergeOutputs(request.Config.Output, request.OutputPaths).Values;
        if (outputs.Any(path => PathsCollide(oldPath, NormalizePathForCollision(path))) ||
            (request.UpdateBaselinePath is { } updateBaseline && PathsCollide(oldPath, NormalizePathForCollision(updateBaseline))))
        {
            throw new SentinelConfigException(
                "--update-snapshot refuses an output or update-baseline path that collides with OLD-SPEC; choose a separate report/baseline destination.");
        }
    }

    private static bool PathsCollide(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string NormalizePathForCollision(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        return info.Exists && info.ResolveLinkTarget(returnFinalTarget: true) is { } target
            ? target.FullName
            : fullPath;
    }

    internal static void RenderOutputs(ComparisonResult result, Request request, IConsoleWriter console, HtmlSnapshotStatus snapshotStatus = HtmlSnapshotStatus.NotRequested)
    {
        var options = new ReporterOptions
        {
            NoColor = request.NoColor,
            Verbose = request.Verbose,
            IncludeSuppressed = !request.HideSuppressed,
            HtmlLanguage = request.HtmlLanguage,
            SnapshotStatus = snapshotStatus,
            SnapshotPath = request.OldSource,
            SnapshotSource = request.NewSource,
        };
        var allOutputs = MergeOutputs(request.Config.Output, request.OutputPaths);

        // Primary format goes to stdout unless it also has an explicit file target.
        var primaryToStdout = !allOutputs.ContainsKey(request.Format);
        // Anything written to a FILE must be plain text: color auto-detection keys off the
        // terminal (which may still be a TTY even though this render is going to disk).
        var primaryOptions = new ReporterOptions
        {
            NoColor = request.NoColor || !primaryToStdout,
            Verbose = request.Verbose,
            IncludeSuppressed = !request.HideSuppressed,
            HtmlLanguage = request.HtmlLanguage,
            SnapshotStatus = snapshotStatus,
            SnapshotPath = request.OldSource,
            SnapshotSource = request.NewSource,
        };
        var primary = RenderFormat(request.Format, result, primaryOptions);
        if (primaryToStdout)
        {
            console.Out(primary);
        }

        foreach (var (format, path) in allOutputs)
        {
            // File artifacts are always color-free regardless of terminal detection.
            var content = format == request.Format && primaryToStdout
                ? primary
                : RenderFormat(format, result, new ReporterOptions
                {
                    NoColor = true,
                    Verbose = request.Verbose,
                    IncludeSuppressed = !request.HideSuppressed,
                    HtmlLanguage = request.HtmlLanguage,
                    SnapshotStatus = snapshotStatus,
                    SnapshotPath = request.OldSource,
                    SnapshotSource = request.NewSource,
                });
            WriteArtifact(path, content, format);
            if (format != "console")
            {
                Console.Error.WriteLine($"[sentinel] wrote {format}: {path}");
            }
        }
    }

    private static void FinalizeHtmlArtifacts(ComparisonResult result, Request request)
    {
        var outputs = MergeOutputs(request.Config.Output, request.OutputPaths);
        if (!outputs.TryGetValue("html", out var path)) return;
        var html = RenderFormat("html", result, new ReporterOptions
        {
            NoColor = true,
            Verbose = request.Verbose,
            IncludeSuppressed = !request.HideSuppressed,
            HtmlLanguage = request.HtmlLanguage,
            SnapshotStatus = HtmlSnapshotStatus.Updated,
            SnapshotPath = request.OldSource,
            SnapshotSource = request.NewSource,
        });
        AtomicFileWriter.Write(path, html);
    }

    private static Dictionary<string, string> MergeOutputs(IReadOnlyDictionary<string, string> fromConfig, IReadOnlyDictionary<string, string> fromCli)
    {
        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (k, v) in fromConfig) merged[k] = v;
        foreach (var (k, v) in fromCli) merged[k] = v; // CLI overrides config
        return merged;
    }

    /// <summary>Renders any supported artifact format. "changelog" is generated from findings.</summary>
    internal static string RenderFormat(string format, ComparisonResult result, bool noColor, bool verbose)
    {
        return RenderFormat(format, result, new ReporterOptions { NoColor = noColor, Verbose = verbose });
    }

    private static string RenderFormat(string format, ComparisonResult result, ReporterOptions options)
    {
        return format switch
        {
            "console" => new ConsoleReporter(supportsColor: !options.NoColor && !System.Console.IsOutputRedirected).Render(result, options),
            "json" => new JsonReporter().Render(result, options),
            "markdown" => new MarkdownReporter().Render(result, options),
            "html" => new HtmlReporter().Render(result, options),
            "sarif" => new SarifReporter().Render(result, options),
            "junit" => new JunitReporter().Render(result, options),
            "changelog" => ChangelogGenerator.Generate(result),
            "pr-summary" => MarkdownReporter.RenderPrSummary(result),
            _ => throw new SentinelConfigException(
                $"Unknown output format '{format}'. Supported: console, json, markdown, html, sarif, junit, changelog."),
        };
    }

    private static void WriteArtifact(string path, string content, string format)
    {
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, content);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Cannot write {format} output to '{path}': {ex.Message}", ex);
        }
    }

    internal static string DisplayName(string source) =>
        source == "-" || source == "stdin" ? "<stdin>" : source;
}

internal static class ToolVersion
{
    private static string? _version;

    /// <summary>Tool version from assembly metadata; overridable for tests.</summary>
    public static string Get()
    {
        if (_version is null)
        {
            var asm = typeof(ToolVersion).Assembly;
            var info = System.Reflection.CustomAttributeExtensions
                .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(asm)?
                .InformationalVersion;
            _version = string.IsNullOrWhiteSpace(info)
                ? asm.GetName().Version?.ToString(3) ?? "0.0.0"
                : info.Split('+')[0];
        }
        return _version;
    }

    public static void SetForTests(string version) => _version = version;
}
