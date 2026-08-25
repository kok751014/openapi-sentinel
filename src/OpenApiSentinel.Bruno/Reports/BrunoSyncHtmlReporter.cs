using System.Net;
using System.Text;

namespace OpenApiSentinel.Bruno.Reports;

/// <summary>
/// Renders the single-file, offline HTML sync report. UI wording is Traditional Chinese
/// (zh-TW); every spec-derived string is HTML-encoded.
/// </summary>
public static class BrunoSyncHtmlReporter
{
    public static string Render(BrunoSyncReport report)
    {
        var sb = new StringBuilder(64 * 1024);
        var s = report.Summary;

        string verdict = report.Skipped
            ? "尚未變更"
            : report.DryRun ? "預覽（Dry Run）" : s.Conflicts > 0 ? "完成（有衝突）" : "同步完成";

        sb.Append("""
            <!DOCTYPE html>
            <html lang="zh-TW">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>OpenAPI Sentinel Bruno 同步報告</title>
            <style>
            :root { color-scheme: light; --bg:#f6f7f9; --card:#fff; --ink:#1c2330; --muted:#66707f;
              --add:#0d7a3f; --upd:#8a5b00; --stale:#a12622; --same:#4b5563; --line:#e3e6ea; }
            * { box-sizing: border-box; }
            body { margin:0; padding:2rem; background:var(--bg); color:var(--ink);
              font-family:"Segoe UI","Microsoft JhengHei","PingFang TC",system-ui,sans-serif; line-height:1.5; }
            main { max-width: 1080px; margin: 0 auto; }
            h1 { font-size: 1.45rem; margin: 0 0 .25rem; }
            .sub { color: var(--muted); font-size:.85rem; margin-bottom: 1.25rem; }
            .cards { display:flex; flex-wrap:wrap; gap:.75rem; margin-bottom:1.25rem; }
            .card { background:var(--card); border:1px solid var(--line); border-radius:.6rem;
              padding:.75rem 1rem; min-width:130px; }
            .card .n { font-size:1.35rem; font-weight:600; }
            .card .t { color:var(--muted); font-size:.78rem; }
            .card.add .n { color:var(--add); } .card.upd .n { color:var(--upd); }
            .card.stale .n { color:var(--stale); } .card.same .n { color:var(--same); }
            .badge { display:inline-block; border-radius:.4rem; padding:.05rem .5rem; font-size:.75rem;
              font-weight:600; color:#fff; background:var(--same); }
            .badge.add{background:var(--add);} .badge.upd{background:var(--upd);}
            .badge.stale{background:var(--stale);}
            section { background:var(--card); border:1px solid var(--line); border-radius:.6rem;
              padding:1rem 1.25rem; margin-bottom:1.25rem; }
            h2 { font-size:1.02rem; margin:.15rem 0 .75rem; }
            table { width:100%; border-collapse:collapse; font-size:.86rem; }
            th, td { text-align:left; padding:.42rem .55rem; border-bottom:1px solid var(--line);
              vertical-align:top; }
            th { color:var(--muted); font-weight:600; white-space:nowrap; }
            code, pre { font-family:Consolas,"Cascadia Mono",monospace; font-size:.84em; }
            pre { background:#f2f3f5; border-radius:.4rem; padding:.5rem .7rem; overflow:auto; margin:.25rem 0; }
            ul { margin:.25rem 0; padding-left:1.25rem; }
            .ok::before { content:"✓ "; color:var(--add); font-weight:700; }
            .warn { color:var(--stale); }
            .muted { color:var(--muted); }
            footer { color:var(--muted); font-size:.78rem; margin-top:1rem; text-align:center; }
            details summary { cursor:pointer; }
            </style>
            </head>
            <body>
            <main>
            <h1>OpenAPI Sentinel — Bruno 同步報告
            """);
        sb.Append("</h1>\n<div class=\"sub\">");
        sb.Append(WebUtility.HtmlEncode(verdict));
        if (report.DryRun)
        {
            sb.Append(" · 未寫入任何檔案");
        }

        sb.Append(" · ").Append(WebUtility.HtmlEncode(report.GeneratedAtUtc));
        sb.Append("</div>\n");

        // ---- summary cards ----
        sb.Append("<div class=\"cards\">");
        Card(sb, "新增 API", s.OperationsAdded, "add");
        Card(sb, "更新 API", s.OperationsUpdated, "upd");
        Card(sb, "刪除／失效 API", s.OperationsStale + s.OperationsRemoved, "stale");
        Card(sb, "未變更 API", s.OperationsUnchanged, "same");
        Card(sb, "新增測試範例", s.ExamplesAdded, "add");
        Card(sb, "更新測試範例", s.ExamplesUpdated, "upd");
        Card(sb, "保留人工設定", s.OperationsWithManualPreserved, "same");
        Card(sb, "同步衝突", s.Conflicts, s.Conflicts > 0 ? "stale" : "same");
        Card(sb, "警告", s.Warnings, s.Warnings > 0 ? "upd" : "same");
        sb.Append("</div>\n");

        // ---- meta ----
        sb.Append("<section><h2>結果資訊</h2><table>");
        Row(sb, "來源 OpenAPI", report.Source);
        Row(sb, "Bruno Collection 路徑", report.CollectionPath);
        Row(sb, "工具版本", report.ToolVersion);
        Row(sb, "模式", report.DryRun ? "dry-run（預覽，不修改檔案）" : report.Skipped ? "增量略過（無合約變更）" : "同步");
        sb.Append("</table></section>\n");

        // ---- operations ----
        if (report.Operations.Count > 0)
        {
            sb.Append("<section><h2>API 詳細項目</h2>\n");
            foreach (var op in report.Operations)
            {
                var (label, cls) = op.Action switch
                {
                    "added" => ("新增", "add"),
                    "updated" => ("更新", "upd"),
                    "stale" => ("失效", "stale"),
                    "removed" => ("已刪除", "stale"),
                    _ => ("未變更", "same"),
                };

                sb.Append("<details").Append(op.Action is "added" or "updated" or "stale" or "removed" ? "" : " open")
                    .Append("><summary><span class=\"badge ").Append(cls).Append("\">")
                    .Append(label).Append("</span> <code>")
                    .Append(WebUtility.HtmlEncode($"{op.Method} {op.Path}"))
                    .Append("</code>");
                if (!string.IsNullOrEmpty(op.OperationId))
                {
                    sb.Append(" <span class=\"muted\">").Append(WebUtility.HtmlEncode(op.OperationId!)).Append("</span>");
                }

                sb.Append("</summary><div style=\"padding:.35rem .25rem 0\">");

                if (op.RequestName is { Length: > 0 })
                {
                    Row(sb, "Bruno Request", op.RequestName);
                }

                if (op.Changes.Count > 0)
                {
                    sb.Append("<div style=\"margin-top:.35rem\"><b>變更</b><ul>");
                    foreach (var change in op.Changes)
                    {
                        sb.Append("<li>").Append(WebUtility.HtmlEncode(change)).Append("</li>");
                    }

                    sb.Append("</ul></div>");
                }

                if (op.Preserved.Count > 0)
                {
                    sb.Append("<div style=\"margin-top:.35rem\"><b>保留</b><ul class=\"ok\" style=\"list-style:none;padding-left:0\">");
                    foreach (var kept in op.Preserved)
                    {
                        sb.Append("<li>").Append(WebUtility.HtmlEncode(kept)).Append("</li>");
                    }

                    sb.Append("</ul></div>");
                }

                if (op.Examples.Count > 0)
                {
                    sb.Append("<div style=\"margin-top:.35rem\"><b>測試範例</b><ul style=\"list-style:none;padding-left:0\">");
                    foreach (var example in op.Examples)
                    {
                        sb.Append("<li class=\"").Append(example.Action == "kept" ? "" : "ok").Append("\">")
                            .Append(example.Action switch
                            {
                                "added" => "✓ 新增 ",
                                "updated" => "↻ 更新 ",
                                _ => "＝ 保留 ",
                            })
                            .Append(WebUtility.HtmlEncode(example.Name)).Append("</li>");
                    }

                    sb.Append("</ul></div>");
                }

                if (op.Warnings.Count > 0)
                {
                    sb.Append("<div style=\"margin-top:.35rem\"><b class=\"warn\">警告</b><ul>");
                    foreach (var warning in op.Warnings)
                    {
                        sb.Append("<li class=\"warn\">").Append(WebUtility.HtmlEncode(warning)).Append("</li>");
                    }

                    sb.Append("</ul></div>");
                }

                sb.Append("</div></details>\n");
            }

            sb.Append("</section>\n");
        }

        // ---- conflicts ----
        if (report.Conflicts.Count > 0)
        {
            sb.Append("<section><h2 class=\"warn\">同步衝突</h2><ul>");
            foreach (var conflict in report.Conflicts)
            {
                sb.Append("<li><code>").Append(WebUtility.HtmlEncode(conflict.Kind))
                    .Append("</code>：").Append(WebUtility.HtmlEncode(conflict.Detail)).Append("</li>");
            }

            sb.Append("</ul></section>\n");
        }

        if (report.Warnings.Count > 0)
        {
            sb.Append("<section><h2 class=\"warn\">警告</h2><ul>");
            foreach (var warning in report.Warnings)
            {
                sb.Append("<li class=\"warn\">").Append(WebUtility.HtmlEncode(warning)).Append("</li>");
            }

            sb.Append("</ul></section>\n");
        }

        sb.Append("<footer>由 OpenAPI Sentinel 產生 · 離線單檔報告 · 繁體中文介面</footer>");
        sb.Append("\n</main>\n</body>\n</html>\n");
        return sb.ToString();
    }

    private static void Card(StringBuilder sb, string title, int value, string cls)
    {
        sb.Append("<div class=\"card ").Append(cls).Append("\"><div class=\"n\">")
            .Append(value.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Append("</div><div class=\"t\">").Append(WebUtility.HtmlEncode(title)).Append("</div></div>");
    }

    private static void Row(StringBuilder sb, string key, string? value)
    {
        sb.Append("<tr><th>").Append(WebUtility.HtmlEncode(key)).Append("</th><td>")
            .Append(WebUtility.HtmlEncode(value ?? "")).Append("</td></tr>");
    }
}
