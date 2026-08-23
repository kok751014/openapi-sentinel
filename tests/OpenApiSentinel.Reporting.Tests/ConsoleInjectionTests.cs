using System.Text.Json;
using OpenApiSentinel.Core;
using OpenApiSentinel.Reporting;

namespace OpenApiSentinel.Reporting.Tests;

/// <summary>
/// Terminal escape injection hardening: raw ESC sequences in spec-derived strings
/// (paths, parameter names, enum values) must never reach the terminal, even with
/// --no-color — a hostile spec could otherwise rewrite terminal titles or clear screens.
/// </summary>
public class ConsoleInjectionTests
{
    private const string Esc = "\u001b";

    private static ComparisonResult ResultWithPath(string path) => new()
    {
        SchemaVersion = ComparisonResult.SchemaVersionValue,
        ToolVersion = "1.2.3",
        PolicyViolated = true,
        OldContractName = "old.yaml",
        NewContractName = "new.yaml",
        GeneratedAtUtc = TestResults.Timestamp,
        Findings =
        [
            new Finding
            {
                RuleId = "CG1003",
                Title = "Endpoint added",
                Severity = Severity.Info,
                Category = FindingCategory.Endpoint,
                ChangeType = ChangeType.Added,
                Compatibility = Compatibility.NonBreaking,
                Direction = ApiDirection.Neutral,
                Path = path,
                HttpMethod = HttpMethodKind.Get,
                Explanation = $"New endpoint '{path}' was added.",
                Impact = "Additive change.",
                SuggestedAction = "No action required.",
            },
        ],
        Suppressed = [],
        Summary = new ResultSummary { Total = 1, Info = 1 },
        Endpoints =
        [
            new EndpointSummary { Path = path, HttpMethod = HttpMethodKind.Get, State = EndpointState.Added, FindingCount = 1 },
        ],
    };

    [Fact]
    public void HostilePath_Emits_NoEscapeSequences()
    {
        var hostile = $"/items{Esc}]0;PWNED{Esc}[2J";
        var outText = new ConsoleReporter(supportsColor: false).Render(ResultWithPath(hostile), new ReporterOptions { NoColor = true });

        Assert.DoesNotContain(Esc, outText, StringComparison.Ordinal);
        // The stripped text stays inspectable.
        Assert.Contains("/items]0;PWNED[2J", outText, StringComparison.Ordinal);
    }

    [Fact]
    public void HostilePath_IsStripped_Even_With_ColorSupport()
    {
        // Color support must not re-enable content-borne escapes; only Sentinel's own
        // color codes may appear.
        var hostile = $"{Esc}[31mred{Esc}[0m";
        var outText = new ConsoleReporter(supportsColor: true).Render(ResultWithPath(hostile), new ReporterOptions());

        var sentinelCodes = outText.Split(Esc).Length - 1;
        Assert.DoesNotContain($"{Esc}[31mred", outText, StringComparison.Ordinal);
        // Every ESC present belongs to Sentinel's own SGR wrapping (…m…\u001b[0m pattern).
        Assert.All(outText.Split(Esc).Skip(1), seg => seg.StartsWith('['));
    }

    [Fact]
    public void Clean_Strips_ControlChars_But_Keeps_Newline_Tab_And_Text()
    {
        Assert.Equal("a]0;b[2Jc", ConsoleReporter.Clean($"a{Esc}]0;b{Esc}[2Jc"));
        Assert.Equal("line1\nline2\tcol", ConsoleReporter.Clean("line1\nline2\tcol"));
        Assert.Equal("delchar", ConsoleReporter.Clean("del\u007fchar"));
        Assert.Equal("", ConsoleReporter.Clean(""));
        Assert.Equal("", ConsoleReporter.Clean(null));
    }

    [Fact]
    public void Json_Contract_Preserves_RawValues_For_Machine_Consumers()
    {
        // JSON keeps the payload (its own string encoder makes it safe); sanitization
        // is a console-render concern only. Guards against accidental over-reach.
        var json = new JsonReporter().Render(ResultWithPath("/plain"), new ReporterOptions());
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("/plain", doc.RootElement.GetProperty("findings")[0].GetProperty("path").GetString());
    }
}
