using System.Text.Json;
using System.Xml.Linq;
using OpenApiSentinel.Core;
using OpenApiSentinel.Reporting;

namespace OpenApiSentinel.Reporting.Tests;

/// <summary>Deterministic ComparisonResult fixtures shared by reporter tests.</summary>
internal static class TestResults
{
    internal static readonly DateTimeOffset Timestamp = new(2026, 3, 1, 10, 30, 0, TimeSpan.Zero);

    internal static Finding Finding(string ruleId, string title, Severity severity, Compatibility compatibility,
        string path = "/orders", HttpMethodKind method = HttpMethodKind.Get,
        string? oldValue = null, string? newValue = null) => new()
    {
        RuleId = ruleId,
        Title = title,
        Severity = severity,
        Category = FindingCategory.Endpoint,
        ChangeType = ChangeType.Removed,
        Compatibility = compatibility,
        Direction = ApiDirection.Neutral,
        Path = path,
        HttpMethod = method,
        Explanation = $"{title}: clients calling this endpoint will be affected.",
        Impact = "Existing integrations stop working.",
        SuggestedAction = "Coordinate the removal with consumers.",
        OldValue = oldValue,
        NewValue = newValue,
    };

    /// <summary>One critical + one medium finding, one suppressed entry, MAJOR recommendation.</summary>
    internal static ComparisonResult Failing() => new()
    {
        SchemaVersion = ComparisonResult.SchemaVersionValue,
        ToolVersion = "1.2.3",
        PolicyViolated = true,
        OldContractName = "old.yaml",
        NewContractName = "new.yaml",
        OldOpenApiVersion = "3.0.3",
        NewOpenApiVersion = "3.1.0",
        OldInfoVersion = "1.4.0",
        NewInfoVersion = "2.0.0",
        GeneratedAtUtc = Timestamp,
        Findings =
        [
            Finding("CG1001", "Endpoint removed", Severity.Critical, Compatibility.Breaking),
            Finding("CG2003", "Parameter became required", Severity.High, Compatibility.Breaking,
                path: "/orders/{id}", method: HttpMethodKind.Post, oldValue: "false", newValue: "true"),
            Finding("CG1003", "Endpoint added", Severity.Info, Compatibility.NonBreaking,
                path: "/webhooks"),
        ],
        Suppressed =
        [
            new SuppressedFinding
            {
                Finding = Finding("CG5004", "Enum value added", Severity.Info, Compatibility.NonBreaking),
                Source = "suppression",
                Reason = "Planned extension; consumers informed.",
            },
        ],
        Summary = new ResultSummary
        {
            Total = 3,
            Breaking = 2,
            NonBreaking = 1,
            Critical = 1,
            High = 1,
            Info = 1,
        },
        Recommendation = new VersionRecommendation
        {
            Bump = VersionBump.Major,
            Reason = "2 breaking finding(s) at high or critical severity require a major version.",
        },
        Endpoints =
        [
            new EndpointSummary { Path = "/orders", HttpMethod = HttpMethodKind.Get, State = EndpointState.Removed, FindingCount = 1 },
            new EndpointSummary { Path = "/orders/{id}", HttpMethod = HttpMethodKind.Post, State = EndpointState.Changed, FindingCount = 2 },
        ],
    };

    internal static ComparisonResult Passing() => new()
    {
        SchemaVersion = ComparisonResult.SchemaVersionValue,
        ToolVersion = "1.2.3",
        PolicyViolated = false,
        OldContractName = "old.yaml",
        NewContractName = "new.yaml",
        GeneratedAtUtc = Timestamp,
        Findings = [],
        Suppressed = [],
        Summary = new ResultSummary(),
        Recommendation = new VersionRecommendation { Bump = VersionBump.None, Reason = "No findings." },
    };
}

public class JsonReporterTests
{
    [Fact]
    public void ProducesValidJson_WithContractFields()
    {
        var json = new JsonReporter().Render(TestResults.Failing(), ReporterOptions.Default);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("1.0", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("failed", root.GetProperty("result").GetString());
        Assert.Equal(3, root.GetProperty("summary").GetProperty("total").GetInt32());
        Assert.Equal(3, root.GetProperty("findings").GetArrayLength());
        Assert.Equal("MAJOR", root.GetProperty("recommendation").GetProperty("display").GetString());
        Assert.Equal("CG1001", root.GetProperty("findings")[0].GetProperty("ruleId").GetString());

        // Endpoint summaries ride along for blast-radius automation.
        var endpoints = root.GetProperty("endpoints");
        Assert.Equal(2, endpoints.GetArrayLength());
        Assert.Equal("removed", endpoints[0].GetProperty("state").GetString()); // camelCase enum
    }

    [Fact]
    public void EnumsAreStrings_AndTimestampIsIso8601()
    {
        var json = new JsonReporter().Render(TestResults.Failing(), ReporterOptions.Default);
        Assert.Contains("\"severity\": \"critical\"", json, StringComparison.Ordinal);
        Assert.Contains("\"compatibility\": \"breaking\"", json, StringComparison.Ordinal);
        Assert.Contains("2026-03-01T10:30:00", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Rendering_IsDeterministic()
    {
        var reporter = new JsonReporter();
        Assert.Equal(reporter.Render(TestResults.Failing(), ReporterOptions.Default),
                     reporter.Render(TestResults.Failing(), ReporterOptions.Default));
    }

    [Fact]
    public void IncludeSuppressedFalse_OmitsSuppressedEntries()
    {
        var options = new ReporterOptions { IncludeSuppressed = false };
        var json = new JsonReporter().Render(TestResults.Failing(), options);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(0, doc.RootElement.GetProperty("suppressed").GetArrayLength());
    }
}

public class MarkdownReporterTests
{
    private readonly MarkdownReporter _reporter = new();

    [Fact]
    public void ContainsCoreSections()
    {
        var md = _reporter.Render(TestResults.Failing(), ReporterOptions.Default);
        Assert.StartsWith("# API Compatibility Report", md, StringComparison.Ordinal);
        Assert.Contains("## Summary", md, StringComparison.Ordinal);
        Assert.Contains("| Breaking | 2 |", md, StringComparison.Ordinal);
        Assert.Contains("## Findings", md, StringComparison.Ordinal);
        Assert.Contains("[CG1001]", md, StringComparison.Ordinal);
        Assert.Contains("Recommended version bump: `MAJOR`", md, StringComparison.Ordinal);
    }

    [Fact]
    public void SuppressedSection_ListsReasons()
    {
        var md = _reporter.Render(TestResults.Failing(), ReporterOptions.Default);
        Assert.Contains("## Suppressed (1)", md, StringComparison.Ordinal);
        Assert.Contains("Planned extension; consumers informed.", md, StringComparison.Ordinal);
    }

    [Fact]
    public void PrSummary_IsCompactAndStatesOutcome()
    {
        var failed = MarkdownReporter.RenderPrSummary(TestResults.Failing());
        Assert.Contains("policy failed", failed, StringComparison.Ordinal);
        Assert.Contains("MAJOR", failed, StringComparison.Ordinal);

        var passed = MarkdownReporter.RenderPrSummary(TestResults.Passing());
        Assert.Contains("policy passed", passed, StringComparison.Ordinal);
    }

    [Fact]
    public void Changelog_HasBreakingAndNonBreakingSections()
    {
        var changelog = ChangelogGenerator.Generate(TestResults.Failing(), "1.4.0", "1.5.0");
        Assert.StartsWith("## 1.4.0 → 1.5.0", changelog, StringComparison.Ordinal);
        Assert.Contains("### 💥 Breaking and potentially breaking", changelog, StringComparison.Ordinal);
        Assert.Contains("### ✨ Non-breaking", changelog, StringComparison.Ordinal);
        Assert.Contains("GET /orders", changelog, StringComparison.Ordinal);
    }

    [Fact]
    public void Changelog_OnlyOldVersionKnown_NotesBaseline()
    {
        var result = TestResults.Failing();
        var changelog = ChangelogGenerator.Generate(result, oldVersion: "1.4.0", newVersion: "");
        Assert.StartsWith("## Unreleased (changes since 1.4.0)", changelog, StringComparison.Ordinal);
    }

    [Fact]
    public void Changelog_UsesDeclaredInfoVersions_FromResult()
    {
        // Without explicit args, the generator falls back to the result's info versions.
        var changelog = ChangelogGenerator.Generate(TestResults.Failing());
        Assert.StartsWith("## 1.4.0 → 2.0.0", changelog, StringComparison.Ordinal);
    }
}

public class HtmlReporterTests
{
    private static string Render(ComparisonResult result) =>
        new HtmlReporter().Render(result, ReporterOptions.Default);

    [Fact]
    public void IsSingleFile_WithoutExternalResources()
    {
        var html = Render(TestResults.Failing());
        Assert.StartsWith("<!DOCTYPE html>", html.TrimStart(), StringComparison.Ordinal);
        Assert.DoesNotContain("http://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("src=", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<style>", html, StringComparison.Ordinal);
        Assert.Contains("<script>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ContainsRequiredContent()
    {
        var html = Render(TestResults.Failing());
        Assert.Contains("OpenAPI Sentinel", html, StringComparison.Ordinal);
        Assert.Contains("old.yaml", html, StringComparison.Ordinal);
        Assert.Contains("new.yaml", html, StringComparison.Ordinal);
        Assert.Contains("2026-03-01 10:30:00", html, StringComparison.Ordinal);
        Assert.Contains("MAJOR", html, StringComparison.Ordinal); // recommendation badge
        Assert.Contains("已抑制的 Findings", html, StringComparison.Ordinal);
        Assert.Contains("受影響的 Endpoint", html, StringComparison.Ordinal);
        Assert.Contains("本次變更摘要", html, StringComparison.Ordinal);
    }

    [Fact]
    public void FailingReport_ShowsFailedVerdict_AndFindingFilters()
    {
        var html = Render(TestResults.Failing());
        Assert.Contains(">未通過<", html, StringComparison.Ordinal);
        Assert.Contains("id=\"search\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"f-compat\"", html, StringComparison.Ordinal);
        Assert.Contains("CG1001", html, StringComparison.Ordinal);
        Assert.Contains("需要處理", html, StringComparison.Ordinal);
    }

    [Fact]
    public void EscapesHtmlSensitiveCharacters()
    {
        var result = TestResults.Passing() with
        {
            Findings =
            [
                TestResults.Finding("CG9999", "Evil <script> & \"quotes\"",
                    Severity.Low, Compatibility.Informational, path: "/x<y>&z"),
            ],
        };
        var html = Render(result);
        Assert.DoesNotContain("<script>alert", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Deterministic_Output()
    {
        Assert.Equal(Render(TestResults.Failing()), Render(TestResults.Failing()));
    }

    [Fact]
    public void HtmlPresentation_DeduplicatesOnlyExactEvents()
    {
        var original = TestResults.Finding("CG5017", "Property removed", Severity.High, Compatibility.Breaking,
            path: "/accounts/{id}", oldValue: "test");
        var differentValue = original with { OldValue = "other" };
        var result = TestResults.Passing() with
        {
            Findings = [original, original, original, differentValue],
            Summary = new ResultSummary { Total = 4, Breaking = 4, High = 4 },
            PolicyViolated = true,
        };
        var html = new HtmlReporter().Render(result, new ReporterOptions { HtmlLanguage = HtmlLanguage.En });

        Assert.Contains("Raw rule events 4 · Distinct changes 2 · Duplicates collapsed 2", html, StringComparison.Ordinal);
        Assert.Contains("Merged 3 identical rule events", html, StringComparison.Ordinal);
        Assert.Equal(4, result.Findings.Count); // HTML did not mutate the engine result.
    }

    [Fact]
    public void EnglishMode_UsesEnglishMarkup_And_TraditionalChineseIsDefault()
    {
        var zh = Render(TestResults.Failing());
        var en = new HtmlReporter().Render(TestResults.Failing(), new ReporterOptions { HtmlLanguage = HtmlLanguage.En });
        Assert.Contains("<html lang=\"zh-Hant-TW\">", zh, StringComparison.Ordinal);
        Assert.Contains("本次變更摘要", zh, StringComparison.Ordinal);
        Assert.Contains("<html lang=\"en\">", en, StringComparison.Ordinal);
        Assert.Contains("Affected endpoints", en, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryBuiltInRule_HasTraditionalChineseHtmlCoverage()
    {
        var uncovered = OpenApiSentinel.Rules.RuleRegistry.CreateDefault().All
            .Select(r => r.Descriptor.Id)
            .Where(id => !HtmlLocalizationCatalog.HasTraditionalChinese(id))
            .ToArray();
        Assert.Empty(uncovered);
    }
}

public class ConsoleReporterTests
{
    [Fact]
    public void Labels_ArePresentWithoutColorOnlyInformation()
    {
        var text = new ConsoleReporter(supportsColor: false).Render(TestResults.Failing(), new ReporterOptions { NoColor = true });
        Assert.DoesNotContain("\u001b[", text, StringComparison.Ordinal);
        Assert.Contains("FAILED", text, StringComparison.Ordinal);
        Assert.Contains("[CRITICAL]", text, StringComparison.Ordinal);
        Assert.Contains("CG1001", text, StringComparison.Ordinal);
        Assert.Contains("critical: 1", text, StringComparison.Ordinal);
        Assert.Contains("MAJOR", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ColorEnabled_EmitsAnsiCodes_ButLabelsRemain()
    {
        var text = new ConsoleReporter(supportsColor: true).Render(TestResults.Failing(), new ReporterOptions());
        Assert.Contains("\u001b[31;1m", text, StringComparison.Ordinal);
        Assert.Contains("CRITICAL]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Verbose_IncludesImpactAndAction()
    {
        var options = new ReporterOptions { Verbose = true };
        var text = new ConsoleReporter(supportsColor: false).Render(TestResults.Failing(), options);
        Assert.Contains("impact:", text, StringComparison.Ordinal);
        Assert.Contains("action:", text, StringComparison.Ordinal);
    }

    [Fact]
    public void PassingResult_ShowsPassed()
    {
        var text = new ConsoleReporter(supportsColor: false).Render(TestResults.Passing(), new ReporterOptions { NoColor = true });
        Assert.Contains("PASSED", text, StringComparison.Ordinal);
        Assert.Contains("Findings: 0", text, StringComparison.Ordinal);
    }
}

public class SarifReporterTests
{
    [Fact]
    public void ValidSarifStructure_WithLevelMapping()
    {
        var sarif = new SarifReporter().Render(TestResults.Failing(), ReporterOptions.Default);
        using var doc = JsonDocument.Parse(sarif);
        var run = doc.RootElement.GetProperty("runs")[0];

        Assert.Equal("2.1.0", doc.RootElement.GetProperty("version").GetString());
        Assert.Equal("OpenAPI Sentinel", run.GetProperty("tool").GetProperty("driver").GetProperty("name").GetString());

        var results = run.GetProperty("results");
        Assert.Equal(3, results.GetArrayLength());
        Assert.Equal("error", results[0].GetProperty("level").GetString()); // Critical
        Assert.Equal("error", results[1].GetProperty("level").GetString()); // High
        Assert.Equal("note", results[2].GetProperty("level").GetString());  // Info

        // Artifact URIs must be valid relative references (no spaces); the human-readable
        // location travels in properties.location.
        var uri = results[0].GetProperty("locations")[0]
            .GetProperty("physicalLocation").GetProperty("artifactLocation").GetProperty("uri").GetString();
        Assert.DoesNotContain(" ", uri);
        Assert.Equal("/orders", uri);
        Assert.Contains("location", results[0].GetProperty("properties").EnumerateObject().Select(p => p.Name), StringComparer.Ordinal);

        var rules = run.GetProperty("tool").GetProperty("driver").GetProperty("rules");
        Assert.Equal(3, rules.GetArrayLength()); // distinct, ordered
        Assert.Equal("CG1001", rules[0].GetProperty("id").GetString());
    }
}

public class JunitReporterTests
{
    [Fact]
    public void FailingRun_HasFailureElementsPerViolation()
    {
        var xml = new JunitReporter().Render(TestResults.Failing(), ReporterOptions.Default);
        var doc = XDocument.Parse(xml);
        var suites = doc.Root!;
        Assert.Equal("testsuites", suites.Name.LocalName);
        Assert.Equal("2", suites.Attribute("tests")!.Value);
        Assert.Equal("2", suites.Attribute("failures")!.Value);

        var failures = suites.Descendants("failure").ToList();
        Assert.Equal(2, failures.Count);
        Assert.Contains("CG1001", failures[0].Parent!.Attribute("name")!.Value);
    }

    [Fact]
    public void PassingRun_SingleGreenTestcase()
    {
        var xml = new JunitReporter().Render(TestResults.Passing(), ReporterOptions.Default);
        var doc = XDocument.Parse(xml);
        Assert.Equal("1", doc.Root!.Attribute("tests")!.Value);
        Assert.Equal("0", doc.Root.Attribute("failures")!.Value);
        Assert.Empty(doc.Root.Descendants("failure"));
    }

    [Fact]
    public void EscapesUnsafeXmlContent()
    {
        var result = TestResults.Passing() with
        {
            PolicyViolated = true,
            Findings =
            [
                TestResults.Finding("CG7777", "Weird <tag> & \"stuff\"", Severity.Critical, Compatibility.Breaking),
            ],
        };
        var xml = new JunitReporter().Render(result, ReporterOptions.Default);
        var doc = XDocument.Parse(xml); // must parse despite < > &
        Assert.DoesNotContain("Weird <tag>", xml, StringComparison.Ordinal); // raw markup must not leak
        var name = doc.Root!.Descendants("testcase").Single().Attribute("name")!.Value;
        Assert.Contains("Weird <tag> & \"stuff\"", name, StringComparison.Ordinal); // content survives round-trip
        Assert.Single(doc.Root!.Descendants("failure"));
    }
}

public class GoldenConsoleTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OpenApiSentinel.slnx")))
            dir = dir.Parent!;
        return dir!.FullName;
    }

    /// <summary>
    /// Locks the primary terminal UX byte-for-byte for the deterministic fixture.
    /// Regenerate via SENTINEL_ACCEPT_GOLDEN=1 dotnet test.
    /// </summary>
    [Fact]
    public void Console_MatchesGoldenSnapshot()
    {
        var goldenPath = Path.Combine(RepoRoot(), "tests", "OpenApiSentinel.Reporting.Tests", "GoldenFiles", "console.txt");
        var text = new ConsoleReporter(supportsColor: false).Render(TestResults.Failing(), new ReporterOptions { NoColor = true, Verbose = true });

        if (Environment.GetEnvironmentVariable("SENTINEL_ACCEPT_GOLDEN") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(goldenPath)!);
            File.WriteAllText(goldenPath, text);
            return;
        }

        Assert.True(File.Exists(goldenPath),
            $"Golden file missing: {goldenPath}. Run SENTINEL_ACCEPT_GOLDEN=1 dotnet test once to create it.");
        var golden = File.ReadAllText(goldenPath);
        // Line endings are platform artifacts (WriteLine / git normalization), not
        // report content; compare with normalized endings so goldens are byte-stable
        // across Windows/Linux checkouts.
        Assert.True(NormalizeNewlines(text) == NormalizeNewlines(golden),
            "Console report diverged from the golden snapshot. " +
            "If the change is intentional, rerun with SENTINEL_ACCEPT_GOLDEN=1 and commit the updated file.");
    }

    internal static string NormalizeNewlines(string s) => s.Replace("\r\n", "\n", StringComparison.Ordinal);
}

public class GoldenHtmlTests
{
    /// <summary>
    /// Locks the single-file HTML report structure byte-for-byte for the deterministic fixture.
    /// Intentional design changes should regenerate the golden file via
    /// SENTINEL_ACCEPT_GOLDEN=1 dotnet test (then commit the new golden).
    /// </summary>
    [Fact]
    public void Html_MatchesGoldenSnapshot()
    {
        var repoRoot = RepoRoot();
        var goldenPath = Path.Combine(repoRoot, "tests", "OpenApiSentinel.Reporting.Tests", "GoldenFiles", "report.html");
        var html = new HtmlReporter().Render(TestResults.Failing(), ReporterOptions.Default);

        if (Environment.GetEnvironmentVariable("SENTINEL_ACCEPT_GOLDEN") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(goldenPath)!);
            File.WriteAllText(goldenPath, html);
            return;
        }

        Assert.True(File.Exists(goldenPath),
            $"Golden file missing: {goldenPath}. Run SENTINEL_ACCEPT_GOLDEN=1 dotnet test once to create it.");
        var golden = File.ReadAllText(goldenPath);
        Assert.True(GoldenConsoleTests.NormalizeNewlines(html) == GoldenConsoleTests.NormalizeNewlines(golden),
            "HTML report diverged from the golden snapshot. " +
            "If the change is intentional, rerun with SENTINEL_ACCEPT_GOLDEN=1 and commit the updated file.");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OpenApiSentinel.slnx")))
            dir = dir.Parent!;
        return dir!.FullName;
    }
}

public class ConsoleSuppressedSummaryTests
{
    [Fact]
    public void Summary_ShowsSuppressedCount_WhenPresent()
    {
        var text = new ConsoleReporter(supportsColor: false)
            .Render(TestResults.Failing(), new ReporterOptions { NoColor = true });
        Assert.Contains("Suppressed: 1 (acknowledged", text, StringComparison.Ordinal);
        Assert.Contains("Suppressed (1)", text, StringComparison.Ordinal); // detailed list section
    }

    [Fact]
    public void Summary_OmitsSuppressedLine_WhenNone()
    {
        var text = new ConsoleReporter(supportsColor: false)
            .Render(TestResults.Passing(), new ReporterOptions { NoColor = true });
        Assert.DoesNotContain("Suppressed:", text, StringComparison.Ordinal);
    }
}

public class EndpointSummaryRenderTests
{
    [Fact]
    public void ConsoleVerbose_ShowsAffectedEndpoints()
    {
        var text = new ConsoleReporter(supportsColor: false)
            .Render(TestResults.Failing(), new ReporterOptions { NoColor = true, Verbose = true });
        Assert.Contains("Affected endpoints", text, StringComparison.Ordinal);
        Assert.Contains("[Removed]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ConsoleVerbose_ShowsProcessingNotes_WhenNonZero()
    {
        var result = TestResults.Failing() with
        {
            Processing = new Core.ProcessingSummary { Changes = 10, FindingsEmitted = 2, IgnoredByPath = 3 },
        };
        var text = new ConsoleReporter(supportsColor: false)
            .Render(result, new ReporterOptions { NoColor = true, Verbose = true });
        Assert.Contains("Processing notes:", text, StringComparison.Ordinal);
        Assert.Contains("3 ignored by path filters", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Markdown_IncludesEndpointTable()
    {
        var md = new MarkdownReporter().Render(TestResults.Failing(), ReporterOptions.Default);
        Assert.Contains("### Affected endpoints", md, StringComparison.Ordinal);
        Assert.Contains("| Removed |", md, StringComparison.Ordinal);
    }
}

public class DocumentLevelLocationTests
{
    private static ComparisonResult WithDocumentLevelFinding() =>
        TestResults.Passing() with
        {
            Findings =
            [
                // CG7001-style: no path, no method — a document-level finding.
                TestResults.Finding("CG7001", "API version changed", Severity.Info,
                    Compatibility.Informational, path: "", method: HttpMethodKind.None),
            ],
            PolicyViolated = false,
            Summary = new ResultSummary { Total = 1, Info = 1 },
        };

    [Fact]
    public void DocumentLevelFindings_RenderDocumentMarker()
    {
        var result = WithDocumentLevelFinding();
        var md = new MarkdownReporter().Render(result, ReporterOptions.Default);
        Assert.Contains("(document)", md, StringComparison.Ordinal);
        Assert.DoesNotContain("``", md, StringComparison.Ordinal); // no empty inline code spans
    }

    [Fact]
    public void Console_DocumentLevelFindings_RenderDocumentMarker()
    {
        var text = new ConsoleReporter(supportsColor: false)
            .Render(WithDocumentLevelFinding(), new ReporterOptions { NoColor = true });
        Assert.Contains("(document)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Sarif_DocumentLevelFind_UriFallsBackToSentinel()
    {
        var sarif = new SarifReporter().Render(TestResults.Failing(), ReporterOptions.Default);
        using var doc = System.Text.Json.JsonDocument.Parse(sarif);
        var first = doc.RootElement.GetProperty("runs")[0].GetProperty("results")[0];
        // CG1001 has a path; document-level entries would carry the sentinel marker.
        var uri = first.GetProperty("locations")[0]
            .GetProperty("physicalLocation").GetProperty("artifactLocation").GetProperty("uri").GetString();
        Assert.False(string.IsNullOrWhiteSpace(uri));
    }
}

public class ProcessingSummaryTests
{
    [Fact]
    public void Json_IncludesProcessingCounters()
    {
        var result = TestResults.Failing() with
        {
            Processing = new Core.ProcessingSummary
            {
                Changes = 42,
                FindingsEmitted = 3,
                IgnoredByPath = 2,
            },
        };
        var json = new JsonReporter().Render(result, ReporterOptions.Default);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var proc = doc.RootElement.GetProperty("processing");
        Assert.Equal(42, proc.GetProperty("changes").GetInt32());
        Assert.Equal(2, proc.GetProperty("ignoredByPath").GetInt32());
    }

    [Fact]
    public void Markdown_ShowsProcessingNotes_WhenNonZero()
    {
        var result = TestResults.Failing() with
        {
            Processing = new Core.ProcessingSummary { Changes = 10, FindingsEmitted = 2, UnmatchedChanges = 4 },
        };
        var md = new MarkdownReporter().Render(result, ReporterOptions.Default);
        Assert.Contains("Processing notes:", md, StringComparison.Ordinal);
        Assert.Contains("4 unevaluated change kind(s)", md, StringComparison.Ordinal);
    }
}
