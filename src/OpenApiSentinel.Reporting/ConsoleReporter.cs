using OpenApiSentinel.Core;

namespace OpenApiSentinel.Reporting;

/// <summary>
/// Terminal reporter. Severity is always carried by text labels so the output remains
/// readable without color (CI logs, redirected output, color-blind users).
/// </summary>
public sealed class ConsoleReporter : IResultReporter
{
    private readonly bool _supportsColor;

    public ConsoleReporter(bool supportsColor = true)
    {
        _supportsColor = supportsColor;
    }

    public string FormatName => "console";

    public string Render(ComparisonResult result, ReporterOptions options)
    {
        var color = !options.NoColor && _supportsColor;
        var w = new StringWriter();
        var c = new Colors(color);

        c.Dim(w, "─".Repeat(72));
        w.WriteLine($"OpenAPI Sentinel  {result.ToolVersion}");
        c.Dim(w, $"  {result.OldContractName}  →  {result.NewContractName}   (generated {result.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss} UTC)");
        if (!string.IsNullOrEmpty(result.OldOpenApiVersion) || !string.IsNullOrEmpty(result.NewOpenApiVersion))
        {
            c.Dim(w, $"  versions: {result.OldOpenApiVersion ?? "?"}  →  {result.NewOpenApiVersion ?? "?"}");
        }
        c.Dim(w, "─".Repeat(72));

        var s = result.Summary;
        var verdict = result.PolicyViolated ? "FAILED" : "PASSED";
        var hasRisk = s.Breaking > 0 || s.PotentiallyBreaking > 0;
        Action<TextWriter, string> paintVerdict = result.PolicyViolated ? c.Red
            : hasRisk ? c.Yellow
            : c.Green;

        w.WriteLine();
        w.Write("Result: ");
        paintVerdict(w, verdict);
        w.WriteLine();

        w.WriteLine();
        w.WriteLine("Summary");
        w.WriteLine($"  Findings: {s.Total}");
        w.WriteLine($"  Severity: {SeverityLabel(Severity.Critical, s.Critical)} {SeverityLabel(Severity.High, s.High)} {SeverityLabel(Severity.Medium, s.Medium)} {SeverityLabel(Severity.Low, s.Low)} {SeverityLabel(Severity.Info, s.Info)} {SeverityLabel(Severity.Verify, s.Verify)}");
        w.WriteLine($"  Compatibility: {s.Breaking} breaking, {s.PotentiallyBreaking} potentially breaking, {s.NonBreaking} non-breaking, {s.Informational} informational, {s.Unknown} unknown");
        if (options.IncludeSuppressed && result.Suppressed.Count > 0)
        {
            w.WriteLine($"  Suppressed: {result.Suppressed.Count} (acknowledged — see list below)");
        }
        if (result.Recommendation is { } rec)
        {
            w.WriteLine($"  Recommended version bump: {rec.Display} — {rec.Reason}");
        }

        if (s.Total > 0)
        {
            w.WriteLine();
            w.WriteLine("Findings");
            foreach (var f in result.Findings)
            {
                WriteFinding(w, c, f, options.Verbose);
            }
        }

        if (options.Verbose && result.Endpoints is { Count: > 0 } endpoints)
        {
            w.WriteLine();
            w.WriteLine("Affected endpoints");
            foreach (var e in endpoints.Take(25))
            {
                var name = e.HttpMethod == HttpMethodKind.None
                    ? e.Path
                    : $"{e.HttpMethod.ToHttpString()} {e.Path}";
                w.WriteLine($"  [{e.State}] {name} — {e.FindingCount} finding(s)");
            }

            if (endpoints.Count > 25)
            {
                w.WriteLine($"  … and {endpoints.Count - 25} more");
            }
        }

        if (options.IncludeSuppressed && result.Suppressed.Count > 0)
        {
            w.WriteLine();
            w.WriteLine($"Suppressed ({result.Suppressed.Count}) — acknowledged, not counted");
            foreach (var sf in result.Suppressed)
            {
                var f = sf.Finding;
                c.Dark(w, $"  [{f.Severity.ToString().ToUpperInvariant()}] {f.RuleId} {Describe(f)} — {sf.Source}: {sf.Reason}");
            }
        }

        w.WriteLine();
        c.Dim(w, "─".Repeat(72));
        return w.ToString();
    }

    private static string SeverityLabel(Severity severity, int count) =>
        $"{severity switch
        {
            Severity.Critical => "critical",
            Severity.High => "high",
            Severity.Medium => "medium",
            Severity.Low => "low",
            Severity.Verify => "verify",
            _ => "info",
        }}: {count}";

    private static string Describe(Finding f)
    {
        var d = f.HttpMethod == HttpMethodKind.None ? f.Path : $"{f.HttpMethod.ToHttpString()} {f.Path}";
        return d.Length == 0 ? "(document)" : d;
    }

    private static void WriteFinding(TextWriter w, Colors c, Finding f, bool verbose)
    {
        var location = string.IsNullOrEmpty(f.Location) ? Describe(f) : $"{Describe(f)} → {f.Location}";
        var sev = f.Severity.ToString().ToUpperInvariant();
        w.Write(c.MarkSeverity(f.Severity, $"[{sev}]"));
        w.WriteLine($" {f.RuleId}  {location}");
        w.WriteLine($"        {f.Explanation}");
        if (verbose)
        {
            if (f.OldValue is not null || f.NewValue is not null)
            {
                w.WriteLine($"        values: {f.OldValue ?? "(none)"} → {f.NewValue ?? "(removed)"}");
            }
            w.WriteLine($"        impact: {f.Impact}");
            w.WriteLine($"        action: {f.SuggestedAction}");
        }
    }

    private sealed class Colors
    {
        private readonly bool _enabled;
        internal Colors(bool enabled) => _enabled = enabled;

        public static string Paint(string text) => text;

        public void Dim(TextWriter w, string text) => w.WriteLine(_enabled ? $"\u001b[90m{text}\u001b[0m" : text);

        public void Dark(TextWriter w, string text) => Dim(w, text);

        public void Red(TextWriter w, string text) => w.WriteLine(_enabled ? $"\u001b[31;1m{text}\u001b[0m" : text);

        public void Green(TextWriter w, string text) => w.WriteLine(_enabled ? $"\u001b[32;1m{text}\u001b[0m" : text);

        public void Yellow(TextWriter w, string text) => w.WriteLine(_enabled ? $"\u001b[33;1m{text}\u001b[0m" : text);

        public string MarkSeverity(Severity severityLevel, string text) => !_enabled
            ? text
            : severityLevel switch
            {
                Severity.Critical or Severity.High => $"\u001b[31;1m{text}\u001b[0m",
                Severity.Medium => $"\u001b[33;1m{text}\u001b[0m",
                Severity.Verify => $"\u001b[35;1m{text}\u001b[0m",
                _ => text,
            };
    }
}

internal static class StringRepeatExtensions
{
    public static string Repeat(this string value, int count) => string.Concat(Enumerable.Repeat(value, count));
}
