using OpenApiSentinel.Core;
using OpenApiSentinel.Reporting;

namespace OpenApiSentinel.Reporting.Tests;

/// <summary>
/// Markdown/PR-summary injection hardening (§30: specs are untrusted input).
/// Spec-derived values must not be able to inject live links, raw HTML, or
/// terminate code spans in rendered markdown.
/// </summary>
public class MarkdownInjectionTests
{
    private static ComparisonResult ResultWith(Finding finding) => new()
    {
        SchemaVersion = ComparisonResult.SchemaVersionValue,
        ToolVersion = "1.2.3",
        PolicyViolated = true,
        OldContractName = "old.yaml",
        NewContractName = "new.yaml",
        GeneratedAtUtc = TestResults.Timestamp,
        Findings = [finding],
        Suppressed = [],
        Summary = new ResultSummary { Total = 1, Breaking = 1, Critical = 1 },
    };

    private static Finding HostileFinding(string enumPayload, bool includeValues = true) => new()
    {
        RuleId = "CG2007",
        Title = "Enum value removed",
        Severity = Severity.Critical,
        Category = FindingCategory.Parameter,
        ChangeType = ChangeType.Removed,
        Compatibility = Compatibility.Breaking,
        Direction = ApiDirection.Request,
        Path = "/items",
        HttpMethod = HttpMethodKind.Get,
        Location = "parameters.kind",
        Explanation = $"New enum value \"{enumPayload}\" is accepted by parameter 'kind'.",
        Impact = "Consumers may receive unexpected values.",
        SuggestedAction = "Update consumers.",
        OldValue = includeValues ? enumPayload : null,
        NewValue = null,
    };

    private const string LinkPayload = "click [here](http://evil.example)";

    [Fact]
    public void LinkInjection_InValues_IsNeutralized()
    {
        // Prose-only finding: the payload must not survive as renderable link syntax anywhere.
        var md = new MarkdownReporter().Render(ResultWith(HostileFinding(LinkPayload, includeValues: false)), new ReporterOptions());

        Assert.DoesNotContain("[here](http://evil.example)", md, StringComparison.Ordinal);
        Assert.Contains("\\[here\\]", md, StringComparison.Ordinal);
    }

    [Fact]
    public void LinkInjection_InPrSummary_IsNeutralized()
    {
        var pr = MarkdownReporter.RenderPrSummary(ResultWith(HostileFinding(LinkPayload, includeValues: false)));

        Assert.DoesNotContain("[here](http://evil.example)", pr, StringComparison.Ordinal);
        Assert.Contains("\\[here\\]", pr, StringComparison.Ordinal);
    }

    [Fact]
    public void HtmlInjection_InProse_IsEscaped()
    {
        var md = new MarkdownReporter().Render(
            ResultWith(HostileFinding("<img src=x onerror=alert(1)>", includeValues: false)), new ReporterOptions());

        // The escaped form "\<img" is correct; a raw occurrence would follow the quote directly.
        Assert.Contains("\\<img src=x", md, StringComparison.Ordinal);
        Assert.DoesNotContain("\"<img src=x", md, StringComparison.Ordinal);
    }

    [Fact]
    public void Backtick_Payload_CannotTerminate_CodeSpan()
    {
        const string payload = "`injected *markdown*`";
        var md = new MarkdownReporter().Render(ResultWith(HostileFinding(payload)), new ReporterOptions());

        // The change line must use a longer fence; the inner backticks stay literal content.
        Assert.Contains("`` `injected *markdown*` ``", md, StringComparison.Ordinal);
    }

    [Fact]
    public void CodeSpan_Fence_Grows_With_Longest_Backtick_Run()
    {
        Assert.Equal("`abc`", MarkdownReporter.Code("abc"));
        Assert.Equal("``a`b``", MarkdownReporter.Code("a`b"));
        Assert.Equal("```x``y```", MarkdownReporter.Code("x``y"));
        Assert.Equal("`` `  ``", MarkdownReporter.Code("` "));
    }

    [Fact]
    public void Benign_Output_Unchanged_Shape()
    {
        // Normal values keep the classic single-backtick rendering (no churn for honest specs).
        Assert.Equal("`integer`", MarkdownReporter.Code("integer"));
        Assert.Equal("'x' was removed.", MarkdownReporter.Prose("'x' was removed."));
    }
}

/// <summary>Location rendering must not repeat the endpoint prefix (display dedup).</summary>
[Trait("Category", "LocationComposition")]
public class LocationCompositionTests
{
    private static Finding WithLocation(string method, string path, string location) => new()
    {
        RuleId = "CG5001",
        Title = "Type changed",
        Severity = Severity.Critical,
        Category = FindingCategory.Response,
        ChangeType = ChangeType.Changed,
        Compatibility = Compatibility.Breaking,
        Direction = ApiDirection.Response,
        Path = path,
        HttpMethod = Enum.Parse<HttpMethodKind>(method, ignoreCase: true),
        Location = location,
        Explanation = "x",
        Impact = "y",
        SuggestedAction = "z",
    };

    [Fact]
    public void EngineStyle_Location_Prefix_Is_Stripped()
    {
        var f = WithLocation("Get", "/orders", "GET /orders responses.200.headers.X-Total-Count");
        Assert.Equal("GET /orders → responses.200.headers.X-Total-Count", ConsoleReporter.ComposeLocation(f));
    }

    [Fact]
    public void Relative_Location_Is_Appended()
    {
        var f = WithLocation("Post", "/orders/{id}", "body.signature");
        Assert.Equal("POST /orders/{id} → body.signature", ConsoleReporter.ComposeLocation(f));
    }

    [Fact]
    public void Document_Level_And_Empty_Locations()
    {
        Assert.Equal("(document)", ConsoleReporter.ComposeLocation(WithLocation("None", "", "")));
        var f = WithLocation("Get", "/x", "");
        Assert.Equal("GET /x", ConsoleReporter.ComposeLocation(f));
    }
}
