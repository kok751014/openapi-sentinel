using System.Text;
using OpenApiSentinel.Core;

namespace OpenApiSentinel.Reporting;

/// <summary>
/// Professional single-file HTML report with embedded CSS and vanilla JavaScript.
/// No CDN or network dependencies: filters, search, and collapsible details work offline.
/// </summary>
public sealed class HtmlReporter : IResultReporter
{
    public string FormatName => "html";

    public string Render(ComparisonResult result, ReporterOptions options)
    {
        var s = result.Summary;
        var passed = !result.PolicyViolated;

        var w = new StringBuilder(64 * 1024);
        w.AppendLine("<!DOCTYPE html>");
        w.AppendLine("<html lang=\"en\">");
        w.AppendLine("<head>");
        w.AppendLine("<meta charset=\"utf-8\">");
        w.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        w.AppendLine($"<title>OpenAPI Sentinel — {Esc(result.OldContractName)} → {Esc(result.NewContractName)}</title>");
        w.Append(Styles);
        w.AppendLine("</head>");
        w.AppendLine("<body>");
        w.AppendLine("<header class=\"topbar\"><div class=\"wrap brand-row\">");
        w.AppendLine("  <div class=\"brand\"><span class=\"shield\">🛡</span> <strong>OpenAPI Sentinel</strong><span class=\"tag\">API Compatibility Report</span></div>");
        w.AppendLine($"  <div id=\"verdict\" class=\"{(passed ? "pass" : "fail")}\">{(passed ? "PASSED" : "FAILED")}</div>");
        w.AppendLine("</div></header>");

        w.AppendLine("<main class=\"wrap\">");

        // Header card
        w.AppendLine("<section class=\"card head-card\">");
        w.AppendLine("  <div class=\"head-grid\">");
        w.AppendLine("   <div><div class=\"lbl\">Result</div>");
        w.AppendLine($"     <div class=\"{(passed ? "badge ok" : "badge fail")}\">{(passed ? "PASSED" : "FAILED")}</div></div>");
        static string SpecSub(string? specVersion, string? infoVersion)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(specVersion)) parts.Add("spec " + specVersion);
            if (!string.IsNullOrEmpty(infoVersion)) parts.Add("api v" + infoVersion);
            return parts.Count > 0 ? Esc(string.Join(" · ", parts)) : "—";
        }

        w.AppendLine($"   <div><div class=\"lbl\">Old contract</div><div class=\"val mono\">{Esc(result.OldContractName)}</div><div class=\"sub\">{SpecSub(result.OldOpenApiVersion, result.OldInfoVersion)}</div></div>");
        w.AppendLine($"   <div><div class=\"lbl\">New contract</div><div class=\"val mono\">{Esc(result.NewContractName)}</div><div class=\"sub\">{SpecSub(result.NewOpenApiVersion, result.NewInfoVersion)}</div></div>");
        w.AppendLine($"   <div><div class=\"lbl\">Generated (UTC)</div><div class=\"val\">{result.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss}</div><div class=\"sub\">tool {Esc(result.ToolVersion)} · schema {Esc(result.SchemaVersion)}</div></div>");
        w.AppendLine("  </div>");
        if (result.Recommendation is { } rec)
        {
            var bumpClass = rec.Bump switch
            {
                VersionBump.Major => "bump-major",
                VersionBump.Minor => "bump-minor",
                VersionBump.Patch => "bump-patch",
                _ => "",
            };
            w.AppendLine($"  <div class=\"rec\"><span class=\"bump {bumpClass}\">{rec.Display}</span> {Esc(rec.Reason)}</div>");
        }
        w.AppendLine("</section>");

        // Stat cards
        w.AppendLine("<section class=\"stats\">");
        Stat(w, "total", s.Total, "Findings", "stat-total");
        Stat(w, "breaking", s.Breaking, "Breaking", s.Breaking > 0 ? "bad" : "");
        Stat(w, "potentially", s.PotentiallyBreaking, "Potentially breaking", s.PotentiallyBreaking > 0 ? "warn" : "");
        Stat(w, "nonbreaking", s.NonBreaking, "Non-breaking", "");
        Stat(w, "verify", s.Verify + s.Unknown, "Needs review", s.Verify + s.Unknown > 0 ? "warn" : "");
        Stat(w, "suppressed", result.Suppressed.Count, "Suppressed", "");
        w.AppendLine("</section>");

        // Affected endpoints
        if (result.Endpoints is { Count: > 0 } eps)
        {
            w.AppendLine("<section class=\"card\">");
            w.AppendLine("  <h2>Affected endpoints</h2>");
            w.AppendLine("  <table class=\"tbl\"><thead><tr><th>State</th><th>Endpoint</th><th>Findings</th></tr></thead><tbody>");
            foreach (var e in eps.Take(100))
            {
                var method = e.HttpMethod.ToHttpString();
                var name = string.IsNullOrEmpty(method) ? e.Path : $"{method} {e.Path}";
                w.AppendLine($"    <tr><td><span class=\"st st-{e.State.ToString().ToLowerInvariant()}\">{e.State}</span></td>" +
                             $"<td class=\"mono\">{Esc(name)}</td><td>{e.FindingCount}</td></tr>");
            }
            if (eps.Count > 100)
            {
                w.AppendLine($"    <tr><td colspan=\"3\" class=\"muted\">… and {eps.Count - 100} more</td></tr>");
            }
            w.AppendLine("  </tbody></table>");
            w.AppendLine("</section>");
        }

        // Findings
        w.AppendLine("<section class=\"card\">");
        w.AppendLine("  <h2>Findings</h2>");
        if (result.Findings.Count == 0)
        {
            w.AppendLine("  <p class=\"empty\">No compatibility findings. The new contract did not introduce detectable breaking changes.</p>");
        }
        else
        {
            w.AppendLine("  <div class=\"filters\">");
            w.AppendLine("    <input id=\"search\" type=\"search\" placeholder=\"Search path, rule, explanation…\" autocomplete=\"off\">");
            w.AppendLine("    <select id=\"f-severity\"><option value=\"\">All severities</option>" + Options(["CRITICAL", "HIGH", "MEDIUM", "LOW", "INFO", "VERIFY"]) + "</select>");
            w.AppendLine("    <select id=\"f-method\"><option value=\"\">All methods</option>" + Options(["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS", "TRACE"]) + "</select>");
            w.AppendLine("    <select id=\"f-compat\"><option value=\"\">All compatibility</option>" + Options(["Breaking", "PotentiallyBreaking", "NonBreaking", "Informational", "Unknown"]) + "</select>");
            w.AppendLine("    <label class=\"chk\"><input type=\"checkbox\" id=\"f-breaking-only\"> Breaking only</label>");
            w.AppendLine("  </div>");
            w.AppendLine($"  <div class=\"count-line\" id=\"visible-count\"></div>");
            w.AppendLine("  <div id=\"findings\">");
            foreach (var f in result.Findings)
            {
                WriteFindingCard(w, f);
            }
            w.AppendLine("  </div>");
        }

        if (options.IncludeSuppressed && result.Suppressed.Count > 0)
        {
            w.AppendLine("  <h2>Suppressed findings <span class=\"muted\">(acknowledged, not counted)</span></h2>");
            w.AppendLine("  <table class=\"tbl\"><thead><tr><th>Rule</th><th>Location</th><th>Source</th><th>Reason</th></tr></thead><tbody>");
            foreach (var sf in result.Suppressed)
            {
                var f = sf.Finding;
                w.AppendLine($"    <tr><td class=\"mono\">{Esc(f.RuleId)}</td><td class=\"mono\">{Esc(MarkdownReporter.Describe(f))}</td><td>{Esc(sf.Source)}</td><td>{Esc(sf.Reason)}</td></tr>");
            }
            w.AppendLine("  </tbody></table>");
        }

        w.AppendLine("</section>");
        w.AppendLine("<footer class=\"foot\">Generated by OpenAPI Sentinel — deterministic, offline contract analysis. Rule IDs are stable; see docs/rules.md.</footer>");
        w.AppendLine("</main>");
        w.Append(Script);
        w.AppendLine("</body>");
        w.AppendLine("</html>");
        return w.ToString();
    }

    private static void Stat(StringBuilder w, string id, int count, string label, string cls)
    {
        w.AppendLine($"<div class=\"stat {cls}\" data-stat=\"{id}\"><div class=\"n\">{count}</div><div class=\"t\">{Esc(label)}</div></div>");
    }

    private static string Options(IEnumerable<string> values) =>
        string.Join("", values.Select(v => $"<option value=\"{v}\">{v}</option>"));

    private static void WriteFindingCard(StringBuilder w, Finding f)
    {
        var sev = f.Severity.ToString().ToUpperInvariant();
        var method = f.HttpMethod.ToHttpString();
        var location = MarkdownReporter.Describe(f);
        var compatClass = f.Compatibility switch
        {
            Compatibility.Breaking => "c-break",
            Compatibility.PotentiallyBreaking => "c-potential",
            Compatibility.NonBreaking => "c-nonbreak",
            Compatibility.Informational => "c-info",
            _ => "c-unknown",
        };

        w.AppendLine("<details class=\"finding\" " +
                     $"data-sev=\"{sev}\" data-method=\"{method}\" data-compat=\"{f.Compatibility}\" data-rule=\"{Esc(f.RuleId)}\" " +
                     $"data-text=\"{Esc($"{f.Path} {f.Location} {f.RuleId} {f.Title} {f.Explanation}".ToLowerInvariant())}\">");
        w.AppendLine("  <summary>");
        w.AppendLine($"    <span class=\"sev sev-{sev.ToLowerInvariant()}\">{sev}</span>");
        w.AppendLine($"    <span class=\"rule mono\">{Esc(f.RuleId)}</span>");
        w.AppendLine($"    <span class=\"loc mono\">{Esc(location)}</span>");
        w.AppendLine($"    <span class=\"compat {compatClass}\">{CompatLabel(f.Compatibility)}</span>");
        w.AppendLine($"    <span class=\"chevron\">▾</span>");
        w.AppendLine("  </summary>");
        w.AppendLine("  <div class=\"body\">");
        w.AppendLine($"    <p class=\"title\"><strong>{Esc(f.Title)}</strong> <span class=\"muted\">· {f.ChangeType} · direction: {f.Direction} · confidence: {f.Confidence}</span></p>");
        w.AppendLine($"    <p>{Esc(f.Explanation)}</p>");
        if (f.OldValue is not null || f.NewValue is not null)
        {
            w.AppendLine("    <div class=\"values\">");
            w.AppendLine($"      <div class=\"val-old\"><span class=\"lbl\">before</span><pre class=\"mono\">{Esc(f.OldValue ?? "(none)")}</pre></div>");
            w.AppendLine($"      <div class=\"arrow\">→</div>");
            w.AppendLine($"      <div class=\"val-new\"><span class=\"lbl\">after</span><pre class=\"mono\">{Esc(f.NewValue ?? "(removed)")}</pre></div>");
            w.AppendLine("    </div>");
        }
        w.AppendLine("    <div class=\"grid2\">");
        w.AppendLine($"      <div><span class=\"lbl\">Impact</span><p>{Esc(f.Impact)}</p></div>");
        w.AppendLine($"      <div><span class=\"lbl\">Suggested action</span><p>{Esc(f.SuggestedAction)}</p></div>");
        w.AppendLine("    </div>");
        w.AppendLine("  </div>");
        w.AppendLine("</details>");
    }

    private static string CompatLabel(Compatibility c) => c switch
    {
        Compatibility.PotentiallyBreaking => "Potentially breaking",
        Compatibility.NonBreaking => "Non-breaking",
        Compatibility.Informational => "Informational",
        _ => c.ToString(),
    };

    private static string Esc(string text) => text
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal);

    private const string Styles = """
<style>
  :root {
    --bg: #f6f8fa; --card: #ffffff; --ink: #1f2328; --muted: #656d76;
    --line: #d8dee4; --accent: #0969da;
    --red: #cf222e; --orange: #bc4c00; --green: #1a7f37; --purple: #8250df;
  }
  * { box-sizing: border-box; }
  body { margin: 0; font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
         background: var(--bg); color: var(--ink); font-size: 15px; line-height: 1.5; }
  .mono { font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; }
  .wrap { max-width: 1080px; margin: 0 auto; padding: 0 20px; }
  .topbar { background: #10151c; color: #fff; padding: 14px 0; }
  .brand-row { display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap; }
  .brand .shield { filter: drop-shadow(0 0 6px rgba(80,200,255,.55)); }
  .brand strong { letter-spacing: .3px; }
  .brand .tag { margin-left: 10px; color: #9aa7b4; font-size: 13px; }
  #verdict { font-weight: 700; padding: 4px 14px; border-radius: 999px; font-size: 14px; letter-spacing: 1px; }
  #verdict.pass { background: rgba(46,160,67,.25); color: #7ee787; border: 1px solid #2ea043; }
  #verdict.fail { background: rgba(218,54,51,.22); color: #ff8182; border: 1px solid #da3633; }
  main { padding: 24px 20px 40px; display: grid; gap: 18px; }
  .card { background: var(--card); border: 1px solid var(--line); border-radius: 10px; padding: 18px 20px; }
  h2 { font-size: 17px; margin: 26px 0 12px; }
  .head-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: 16px; align-items: start; }
  .lbl { font-size: 11.5px; text-transform: uppercase; letter-spacing: .8px; color: var(--muted); margin-bottom: 4px; }
  .val { font-weight: 600; word-break: break-all; }
  .sub { color: var(--muted); font-size: 12.5px; }
  .badge { display: inline-block; font-weight: 700; padding: 3px 12px; border-radius: 999px; }
  .badge.ok { color: var(--green); background: #dafbe1; border: 1px solid rgba(26,127,55,.35); }
  .badge.fail { color: var(--red); background: #ffebe9; border: 1px solid rgba(207,34,46,.35); }
  .rec { margin-top: 16px; padding-top: 12px; border-top: 1px dashed var(--line); color: var(--ink); font-size: 14px; }
  .bump { display: inline-block; font-weight: 700; border-radius: 6px; padding: 1px 9px; margin-right: 6px; font-size: 13px; }
  .bump-major { background: #ffebe9; color: var(--red); }
  .bump-minor { background: #ddf4ff; color: var(--accent); }
  .bump-patch { background: #eaeef2; color: var(--ink); }
  .stats { display: grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); gap: 12px; }
  .stat { background: var(--card); border: 1px solid var(--line); border-radius: 10px; padding: 12px 16px; }
  .stat .n { font-size: 26px; font-weight: 700; }
  .stat .t { color: var(--muted); font-size: 12.5px; }
  .stat.bad { border-color: rgba(207,34,46,.45); }
  .stat.bad .n { color: var(--red); }
  .stat.warn { border-color: rgba(188,76,0,.45); }
  .stat.warn .n { color: var(--orange); }
  .filters { display: flex; gap: 10px; flex-wrap: wrap; margin: 8px 0 6px; }
  .filters input[type=search] { flex: 1 1 240px; }
  input, select { font: inherit; padding: 7px 10px; border: 1px solid var(--line); border-radius: 8px; background: #fff; color: var(--ink); }
  input:focus, select:focus { outline: 2px solid rgba(9,105,218,.35); outline-offset: 1px; }
  .chk { display: inline-flex; gap: 6px; align-items: center; font-size: 14px; }
  .count-line { color: var(--muted); font-size: 12.5px; margin: 4px 2px 10px; }
  details.finding { border: 1px solid var(--line); border-radius: 8px; margin: 8px 0; background: #fff; overflow: hidden; }
  details.finding[open] { box-shadow: 0 1px 4px rgba(31,35,40,.08); }
  summary { list-style: none; cursor: pointer; display: flex; gap: 10px; align-items: center; padding: 10px 14px; flex-wrap: wrap; }
  summary::-webkit-details-marker { display: none; }
  .chevron { margin-left: auto; color: var(--muted); transition: transform .15s ease; }
  details[open] .chevron { transform: rotate(180deg); }
  .sev { font-size: 11px; font-weight: 800; letter-spacing: .6px; padding: 2px 8px; border-radius: 5px; min-width: 58px; text-align: center; }
  .sev-critical, .sev-high { background: #ffebe9; color: var(--red); }
  .sev-medium { background: #fff1e5; color: var(--orange); }
  .sev-low { background: #eaeef2; color: var(--ink); }
  .sev-info { background: #ddf4ff; color: var(--accent); }
  .sev-verify { background: #fbefff; color: var(--purple); }
  .rule { color: var(--muted); font-size: 12.5px; }
  .loc { font-size: 13.5px; word-break: break-all; }
  .compat { font-size: 11.5px; padding: 2px 8px; border-radius: 999px; white-space: nowrap; }
  .c-break { background: var(--red); color: #fff; }
  .c-potential { background: #fff1e5; color: var(--orange); border: 1px solid rgba(188,76,0,.4); }
  .c-nonbreak { background: #dafbe1; color: var(--green); }
  .c-info { background: #ddf4ff; color: var(--accent); }
  .c-unknown { background: #fbefff; color: var(--purple); }
  .body { border-top: 1px solid var(--line); padding: 12px 16px 16px; }
  .body p { margin: 6px 0; }
  .title { margin-top: 2px; }
  .values { display: flex; gap: 12px; align-items: stretch; margin: 10px 0; flex-wrap: wrap; }
  .values > div { flex: 1 1 220px; min-width: 0; }
  .values pre { margin: 4px 0 0; background: var(--bg); border: 1px solid var(--line); border-radius: 6px; padding: 8px 10px;
                white-space: pre-wrap; word-break: break-all; font-size: 12.5px; }
  .values .arrow { flex: 0 0 auto; align-self: center; color: var(--muted); font-size: 18px; }
  .grid2 { display: grid; grid-template-columns: repeat(auto-fit, minmax(260px, 1fr)); gap: 14px; }
  .grid2 .lbl { margin-bottom: 2px; }
  .grid2 p { color: #3a444d; }
  .muted { color: var(--muted); font-weight: 400; }
  .empty { color: var(--green); }
  table.tbl { width: 100%; border-collapse: collapse; font-size: 13.5px; }
  table.tbl th, table.tbl td { text-align: left; border-bottom: 1px solid var(--line); padding: 7px 8px; vertical-align: top; }
  table.tbl th { color: var(--muted); font-weight: 600; font-size: 12px; text-transform: uppercase; letter-spacing: .5px; }
  .foot { text-align: center; color: var(--muted); font-size: 12.5px; padding: 8px 0 20px; }
  .st { font-size: 11px; font-weight: 700; letter-spacing: .5px; padding: 2px 8px; border-radius: 5px; }
  .st-removed { background: #ffebe9; color: var(--red); }
  .st-added { background: #dafbe1; color: var(--green); }
  .st-changed { background: #ddf4ff; color: var(--accent); }
  .st-unchanged { background: #eaeef2; color: var(--ink); }
</style>
""";

    private const string Script = """
<script>
(function () {
  "use strict";
  var search = document.getElementById("search");
  var fSeverity = document.getElementById("f-severity");
  var fMethod = document.getElementById("f-method");
  var fCompat = document.getElementById("f-compat");
  var fBreakingOnly = document.getElementById("f-breaking-only");
  var countLine = document.getElementById("visible-count");
  var cards = Array.prototype.slice.call(document.querySelectorAll("#findings > details.finding"));
  function apply() {
    var q = (search && search.value || "").trim().toLowerCase();
    var sev = fSeverity ? fSeverity.value : "";
    var met = fMethod ? fMethod.value : "";
    var cmp = fCompat ? fCompat.value : "";
    var brk = fBreakingOnly ? fBreakingOnly.checked : false;
    var visible = 0;
    cards.forEach(function (el) {
      var ok = true;
      if (q && el.getAttribute("data-text").indexOf(q) === -1) ok = false;
      if (ok && sev && el.getAttribute("data-sev") !== sev) ok = false;
      if (ok && met && el.getAttribute("data-method") !== met) ok = false;
      if (ok && cmp && el.getAttribute("data-compat") !== cmp) ok = false;
      if (ok && brk) {
        var c = el.getAttribute("data-compat");
        if (c !== "Breaking" && c !== "PotentiallyBreaking") ok = false;
      }
      el.style.display = ok ? "" : "none";
      if (ok) visible++;
    });
    if (countLine) {
      var total = cards.length;
      countLine.textContent = visible === total
        ? "Showing all " + total + " finding(s)"
        : "Showing " + visible + " of " + total + " finding(s)";
    }
  }
  [search, fSeverity, fMethod, fCompat].forEach(function (ctl) {
    if (!ctl) return;
    ctl.addEventListener(ctl.tagName === "INPUT" && ctl.type === "search" ? "input" : "change", apply);
  });
  if (fBreakingOnly) fBreakingOnly.addEventListener("change", apply);
  apply();
})();
</script>
""";
}
