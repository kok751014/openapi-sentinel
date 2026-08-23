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

        public FailurePolicy? FailOnOverride { get; init; }

        public string? BaselinePathOverride { get; init; }

        public string? UpdateBaselinePath { get; init; }

        public bool ReportUnknownKinds { get; init; }

        /// <summary>Omit the suppressed-findings section from reports.</summary>
        public bool HideSuppressed { get; init; }
    }

    public static async Task<int> RunAsync(Request request, IConsoleWriter console, CancellationToken ct = default)
    {
        var config = request.Config;

        var oldOutcome = await AnalysisPipeline.LoadDocumentAsync(
            request.OldSource, config.RemoteReferences, config.HeaderEnv, request.AllowRemoteOverride, config.Limits, ct);
        var newOutcome = await AnalysisPipeline.LoadDocumentAsync(
            request.NewSource, config.RemoteReferences, config.HeaderEnv, request.AllowRemoteOverride, config.Limits, ct);
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

        RenderOutputs(evaluation.Result, request, console);

        return evaluation.Result.PolicyViolated ? ExitCodes.PolicyViolated : ExitCodes.Ok;
    }

    internal static void RenderOutputs(ComparisonResult result, Request request, IConsoleWriter console)
    {
        var options = new ReporterOptions
        {
            NoColor = request.NoColor,
            Verbose = request.Verbose,
            IncludeSuppressed = !request.HideSuppressed,
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
                : RenderFormat(format, result, new ReporterOptions { NoColor = true, Verbose = request.Verbose });
            WriteArtifact(path, content, format);
            if (format != "console")
            {
                Console.Error.WriteLine($"[sentinel] wrote {format}: {path}");
            }
        }
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
