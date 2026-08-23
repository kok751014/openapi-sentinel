using System.Text;
using OpenApiSentinel.Core;

namespace OpenApiSentinel.Reporting;

/// <summary>
/// Generates an API-focused changelog with explicit Breaking and Non-breaking sections.
/// Output is deterministic and suitable for release notes or CHANGELOG.md entries.
/// </summary>
public static class ChangelogGenerator
{
    public static string Generate(ComparisonResult result, string? oldVersion = null, string? newVersion = null)
    {
        // Prefer explicitly supplied versions, else fall back to the result's declared info versions.
        oldVersion ??= result.OldInfoVersion;
        newVersion ??= result.NewInfoVersion;

        var w = new StringBuilder();
        string heading;
        if (!string.IsNullOrEmpty(newVersion))
        {
            heading = string.IsNullOrEmpty(oldVersion)
                ? $"## {newVersion}"
                : $"## {oldVersion} → {newVersion}";
        }
        else if (!string.IsNullOrEmpty(oldVersion))
        {
            // Only the starting point is known — make clear nothing was released yet.
            heading = $"## Unreleased (changes since {oldVersion})";
        }
        else
        {
            heading = "## Unreleased";
        }

        w.AppendLine(heading);
        w.AppendLine();

        var breaking = result.Findings.Where(f => f.Compatibility is Compatibility.Breaking or Compatibility.PotentiallyBreaking).ToList();
        var nonBreaking = result.Findings.Except(breaking).ToList();

        if (breaking.Count > 0)
        {
            w.AppendLine("### 💥 Breaking and potentially breaking");
            w.AppendLine();
            foreach (var f in breaking)
            {
                w.AppendLine($"- **{f.Title}** (`{f.RuleId}`) at `{Esc(MarkdownReporter.Describe(f))}` — {f.Explanation}");
            }
            w.AppendLine();
        }

        if (nonBreaking.Count > 0)
        {
            w.AppendLine("### ✨ Non-breaking");
            w.AppendLine();
            foreach (var f in nonBreaking)
            {
                w.AppendLine($"- **{f.Title}** (`{f.RuleId}`) at `{Esc(MarkdownReporter.Describe(f))}` — {f.Explanation}");
            }
            w.AppendLine();
        }

        if (breaking.Count == 0 && nonBreaking.Count == 0)
        {
            w.AppendLine("No observable API changes.");
            w.AppendLine();
        }

        if (result.Recommendation is { } rec && rec.Bump != VersionBump.None)
        {
            w.AppendLine($"**Recommended version bump: {rec.Display}.** {rec.Reason}");
            w.AppendLine();
        }

        return w.ToString();
    }

    private static string Esc(string text) => text.Replace("`", "\\`", StringComparison.Ordinal);
}
