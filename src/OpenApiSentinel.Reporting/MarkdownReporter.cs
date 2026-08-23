using System.Text;
using OpenApiSentinel.Core;

namespace OpenApiSentinel.Reporting;

/// <summary>Markdown report plus a compact PR-summary variant.</summary>
public sealed class MarkdownReporter : IResultReporter
{
    public string FormatName => "markdown";

    public string Render(ComparisonResult result, ReporterOptions options)
    {
        var w = new StringBuilder();
        var s = result.Summary;

        w.AppendLine("# API Compatibility Report");
        w.AppendLine();
        w.AppendLine($"**Result:** {(result.PolicyViolated ? "❌ FAILED" : "✅ PASSED")} — {Code(result.OldContractName)} → {Code(result.NewContractName)}  ");
        w.AppendLine($"**Generated:** {result.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss} UTC by OpenAPI Sentinel {result.ToolVersion}  ");
        if (!string.IsNullOrEmpty(result.OldOpenApiVersion) || !string.IsNullOrEmpty(result.NewOpenApiVersion))
        {
            w.AppendLine($"**Spec versions:** {result.OldOpenApiVersion ?? "?"} → {result.NewOpenApiVersion ?? "?"}");
            w.AppendLine();
        }

        w.AppendLine("## Summary");
        w.AppendLine();
        w.AppendLine("| Metric | Count |");
        w.AppendLine("|--------|------:|");
        w.AppendLine($"| Total findings | {s.Total} |");
        w.AppendLine($"| Breaking | {s.Breaking} |");
        w.AppendLine($"| Potentially breaking | {s.PotentiallyBreaking} |");
        w.AppendLine($"| Non-breaking | {s.NonBreaking} |");
        w.AppendLine($"| Informational | {s.Informational} |");
        if (s.Verify > 0)
        {
            w.AppendLine($"| Needs verification | {s.Verify} |");
        }
        w.AppendLine();
        w.AppendLine($"**Severity:** critical {s.Critical} · high {s.High} · medium {s.Medium} · low {s.Low} · info {s.Info}" +
                     (s.Verify > 0 ? $" · verify {s.Verify}" : ""));
        if (result.Recommendation is { } rec)
        {
            w.AppendLine();
            w.AppendLine($"**Recommended version bump: `{rec.Display}`** — {rec.Reason}");
        }

        if (result.Processing is { } p)
        {
            var bits = new List<string>();
            if (p.UnmatchedChanges > 0) bits.Add($"{p.UnmatchedChanges} unevaluated change kind(s)");
            if (p.DisabledByOverride > 0) bits.Add($"{p.DisabledByOverride} disabled by override");
            if (p.IgnoredByPath > 0) bits.Add($"{p.IgnoredByPath} filtered by ignore paths");
            if (bits.Count > 0)
            {
                w.AppendLine();
                w.AppendLine($"*Processing notes: {string.Join(" · ", bits)}.*");
            }
        }

        w.AppendLine();

        if (result.Endpoints is { Count: > 0 })
        {
            w.AppendLine("### Affected endpoints");
            w.AppendLine();
            w.AppendLine("| State | Endpoint | Findings |");
            w.AppendLine("|-------|----------|---------:|");
            foreach (var e in result.Endpoints.Take(50))
            {
                var name = e.HttpMethod == HttpMethodKind.None
                    ? Code(Esc(e.Path))
                    : Code(Esc($"{e.HttpMethod.ToHttpString()} {e.Path}"));
                w.AppendLine($"| {e.State} | {name} | {e.FindingCount} |");
            }
            w.AppendLine();
        }

        if (s.Total > 0)
        {
            w.AppendLine("## Findings");
            w.AppendLine();
            foreach (var group in result.Findings.GroupBy(f => f.Severity).OrderByDescending(g => g.Key))
            {
                w.AppendLine($"### {group.Key.ToString().ToUpperInvariant()}");
                w.AppendLine();
                foreach (var f in group)
                {
                    WriteFinding(w, f, options.Verbose);
                }
            }
        }
        else
        {
            w.AppendLine("## Findings");
            w.AppendLine();
            w.AppendLine("No compatibility findings. 🎉");
            w.AppendLine();
        }

        if (options.IncludeSuppressed && result.Suppressed.Count > 0)
        {
            w.AppendLine($"## Suppressed ({result.Suppressed.Count})");
            w.AppendLine();
            w.AppendLine("| Rule | Location | Source | Reason |");
            w.AppendLine("|------|----------|--------|--------|");
            foreach (var sf in result.Suppressed)
            {
                var f = sf.Finding;
                w.AppendLine($"| {f.RuleId} | {Code(Esc(Describe(f)))} | {sf.Source} | {Code(Esc(sf.Reason))} |");
            }
            w.AppendLine();
        }

        return w.ToString();
    }

    /// <summary>Compact variant for GitHub PR comments / step summaries.</summary>
    public static string RenderPrSummary(ComparisonResult result)
    {
        var w = new StringBuilder();
        var s = result.Summary;
        var icon = result.PolicyViolated ? "❌" : (s.Breaking + s.PotentiallyBreaking > 0 ? "⚠️" : "✅");

        w.AppendLine($"## {icon} OpenAPI Sentinel: {(result.PolicyViolated ? "policy failed" : "policy passed")}");
        w.AppendLine();
        w.AppendLine($"`{result.OldContractName}` → `{result.NewContractName}` — **{s.Total} finding(s)** " +
                     $"({s.Breaking} breaking, {s.PotentiallyBreaking} potentially breaking)");
        if (result.Recommendation is { } rec && rec.Bump != VersionBump.None)
        {
            w.AppendLine();
            w.AppendLine($"Suggested version bump: **{rec.Display}**");
        }

        var top = result.Findings.Where(f => f.Compatibility is Compatibility.Breaking or Compatibility.PotentiallyBreaking)
            .Take(10).ToList();
        if (top.Count > 0)
        {
            w.AppendLine();
            w.AppendLine("<details>");
            w.AppendLine("<summary>Top breaking/potentially-breaking findings</summary>");
            w.AppendLine();
            foreach (var f in top)
            {
                w.AppendLine($"- **[{f.RuleId}] {Prose(f.Title)}** — {Code(Describe(f))}: {Prose(f.Explanation)}");
            }
            w.AppendLine();
            w.AppendLine("</details>");
        }

        return w.ToString();
    }

    private static void WriteFinding(StringBuilder w, Finding f, bool verbose)
    {
        w.AppendLine($"#### [{f.RuleId}] {Prose(f.Title)} — {Code(Describe(f))}");
        w.AppendLine();
        w.AppendLine(Prose(f.Explanation));
        w.AppendLine();
        w.AppendLine($"- **Compatibility:** {f.Compatibility} · **Confidence:** {f.Confidence} · **Direction:** {f.Direction}");
        if (f.OldValue is not null || f.NewValue is not null)
        {
            w.AppendLine($"- **Change:** {Code(Esc(f.OldValue ?? "(none)"))} → {Code(Esc(f.NewValue ?? "(removed)"))}");
        }
        if (verbose)
        {
            w.AppendLine($"- **Impact:** {Prose(f.Impact)}");
            w.AppendLine($"- **Suggested action:** {Prose(f.SuggestedAction)}");
        }
        w.AppendLine();
    }

    internal static string Describe(Finding f) => ConsoleReporter.ComposeLocation(f);

    /// <summary>Flattens newlines and escapes table-cell pipes (GFM: works inside code spans too).</summary>
    private static string Esc(string text) => (text ?? "")
        .Replace("|", "\\|", StringComparison.Ordinal)
        .Replace("\n", " ", StringComparison.Ordinal);

    /// <summary>
    /// Renders text as a CommonMark code span that survives embedded backticks:
    /// fence length = longest backtick run + 1, space-padded when content touches the fence.
    /// Spec-derived values are untrusted (§30); naive backtick fencing let payloads
    /// terminate the span and inject arbitrary markdown.
    /// </summary>
    public static string Code(string? text)
    {
        var content = (text ?? "").Replace("\n", " ", StringComparison.Ordinal);
        var longestRun = 0;
        var run = 0;
        foreach (var ch in content)
        {
            run = ch == '`' ? run + 1 : 0;
            if (run > longestRun) longestRun = run;
        }

        var fence = new string('`', longestRun + 1);
        var pad = content.Length > 0 && (content[0] == '`' || content[^1] == '`') ? " " : "";
        return $"{fence}{pad}{content}{pad}{fence}";
    }

    /// <summary>
    /// Neutralizes markdown link syntax and raw HTML in prose that embeds spec-derived
    /// values: without this, a crafted enum/property value like
    /// "[click](https://evil.example)" rendered as a live link in PR summaries.
    /// Backslash is escaped first so the escapes themselves are literal.
    /// </summary>
    public static string Prose(string? text) => (text ?? "")
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("<", "\\<", StringComparison.Ordinal)
        .Replace("[", "\\[", StringComparison.Ordinal)
        .Replace("]", "\\]", StringComparison.Ordinal);
}
