using System.Text.Json.Nodes;
using OpenApiSentinel.Core;

namespace OpenApiSentinel.Reporting;

/// <summary>
/// SARIF 2.1.0 report for GitHub code scanning and other static-analysis consumers.
/// Severity mapping: Critical/High → error, Medium/Low → warning, Info/Verify → note.
/// </summary>
public sealed class SarifReporter : IResultReporter
{
    public const string ToolName = "OpenAPI Sentinel";
    public const string ToolUri = "https://github.com/openapi-sentinel/openapi-sentinel";

    public string FormatName => "sarif";

    public string Render(ComparisonResult result, ReporterOptions options)
    {
        var rules = result.Findings
            .Select(f => f.RuleId)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(id => new JsonObject
            {
                ["id"] = id,
                ["shortDescription"] = new JsonObject
                {
                    ["text"] = result.Findings.First(f => f.RuleId == id).Title,
                },
            })
            .ToArray();

        var results = result.Findings.Select(ToSarifResult).ToArray();

        var root = new JsonObject
        {
            ["$schema"] = "https://json.schemastore.org/sarif-2.1.0.json",
            ["version"] = "2.1.0",
            ["runs"] = new JsonArray(
                new JsonObject
                {
                    ["tool"] = new JsonObject
                    {
                        ["driver"] = new JsonObject
                        {
                            ["name"] = ToolName,
                            ["informationUri"] = ToolUri,
                            ["version"] = result.ToolVersion,
                            ["rules"] = new JsonArray(rules),
                        },
                    },
                    ["invocations"] = new JsonArray(
                        new JsonObject
                        {
                            ["executionSuccessful"] = true,
                            ["endTimeUtc"] = result.GeneratedAtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"),
                        }),
                    ["results"] = new JsonArray(results),
                }),
        };

        return root.ToJsonString(SarifWriteOptions);
    }

    /// <summary>Indented writer settings for SARIF output.</summary>
    public static readonly System.Text.Json.JsonSerializerOptions SarifWriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static JsonObject ToSarifResult(Finding f)
    {
        var location = MarkdownReporter.Describe(f);
        return new JsonObject
        {
            ["ruleId"] = f.RuleId,
            ["level"] = Level(f.Severity),
            ["message"] = new JsonObject
            {
                ["text"] = $"{f.Title}: {f.Explanation} Impact: {f.Impact} Suggested action: {f.SuggestedAction}",
            },
            ["properties"] = new JsonObject
            {
                ["compatibility"] = f.Compatibility.ToString(),
                ["severity"] = f.Severity.ToString(),
                ["direction"] = f.Direction.ToString(),
                ["confidence"] = f.Confidence.ToString(),
                ["oldValue"] = f.OldValue,
                ["newValue"] = f.NewValue,
                // Human-readable location (method + path + sub-location); the URI itself
                // must stay a valid relative reference (no spaces), so it carries only the path.
                ["location"] = location,
            },
            ["locations"] = string.IsNullOrEmpty(f.Path)
                ? new JsonArray()
                : new JsonArray(
                    new JsonObject
                    {
                        ["physicalLocation"] = new JsonObject
                        {
                            // SARIF uris are relative references: escape each segment
                            // (spaces, braces, verbs) while preserving the slash structure.
                            ["artifactLocation"] = new JsonObject { ["uri"] = ToArtifactUri(f.Path) },
                        },
                    }),
        };
    }

    internal static string ToArtifactUri(string path) => string.Join("/",
        path.Split('/').Select(System.Uri.EscapeDataString));

    internal static string Level(Severity severity) => severity switch
    {
        Severity.Critical or Severity.High => "error",
        Severity.Medium or Severity.Low => "warning",
        _ => "note",
    };
}
