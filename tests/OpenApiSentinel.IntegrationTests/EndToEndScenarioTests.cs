using System.Text.Json;
using OpenApiSentinel.Cli;

namespace OpenApiSentinel.IntegrationTests;

/// <summary>Captures stdout/stderr for assertions (local copy for this project).</summary>
internal sealed class RecordingConsole : IConsoleWriter
{
    public List<string> OutLines { get; } = [];
    public List<string> ErrorLines { get; } = [];

    public void Out(string value = "") => OutLines.Add(value);

    public void WriteError(string value = "") => ErrorLines.Add(value);

    public string OutText => string.Join('\n', OutLines);
}

/// <summary>
/// <summary>
/// End-to-end scenario from the master spec: baseline API → realistic modifications →
/// compare → detect breaking + non-breaking changes → generate artifacts → CI exit code.
/// Exercises the sample Order Management API contracts.
/// </summary>
public class EndToEndScenarioTests : IDisposable
{
    private readonly string _artifactDir;

    public EndToEndScenarioTests()
    {
        _artifactDir = Path.Combine(Path.GetTempPath(), $"sentinel-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_artifactDir);
    }

    private static string P(string file) => Path.Combine(RepoRoot(), "samples", "basic", file);

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OpenApiSentinel.slnx")))
            dir = dir.Parent!;
        return dir!.FullName;
    }

    [Fact]
    public void BreakingEvolution_FailsPolicy_AndProducesArtifacts()
    {
        var console = new RecordingConsole();
        var code = CommandLineRunner.Run(
        [
            "compare", P("v1.yaml"), P("v2-breaking.yaml"),
            "--json", Path.Combine(_artifactDir, "report.json"),
            "--markdown", Path.Combine(_artifactDir, "report.md"),
            "--html", Path.Combine(_artifactDir, "report.html"),
            "--no-color",
        ], console);

        Assert.Equal(ExitCodes.PolicyViolated, code);

        // JSON artifact: machine-readable, versioned, contains the headline breaking changes.
        var json = File.ReadAllText(Path.Combine(_artifactDir, "report.json"));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("1.0", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("failed", root.GetProperty("result").GetString());
        Assert.Equal("MAJOR", root.GetProperty("recommendation").GetProperty("display").GetString());

        var findings = root.GetProperty("findings");
        var ruleIds = findings.EnumerateArray().Select(f => f.GetProperty("ruleId").GetString()).ToHashSet();
        Assert.Contains("CG1002", ruleIds);   // cancelOrder operation removed
        Assert.Contains("CG2002", ruleIds);   // channel became required
        Assert.Contains("CG2007", ruleIds);   // status enum contraction (request filter)
        Assert.Contains("CG5001", ruleIds);   // Money.amount number → integer
        Assert.Contains("CG6001", ruleIds);   // OAuth requirement added
        Assert.Contains("CG4005", ruleIds);   // X-Total-Count header removed

        // Markdown + HTML artifacts exist and carry the report structure.
        var md = File.ReadAllText(Path.Combine(_artifactDir, "report.md"));
        Assert.Contains("# API Compatibility Report", md, StringComparison.Ordinal);
        Assert.Contains("FAILED", md, StringComparison.Ordinal);

        var html = File.ReadAllText(Path.Combine(_artifactDir, "report.html"));
        Assert.Contains(">未通過<", html, StringComparison.Ordinal);
        Assert.Contains("本次變更摘要", html, StringComparison.Ordinal);
        // Offline single-file report: no external scripts/styles/fonts (spec data URLs are fine).
        Assert.DoesNotContain("<script src=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<link", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("src=\"http", html, StringComparison.OrdinalIgnoreCase);

        // Console output shows the human summary.
        Assert.Contains("FAILED", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public void SafeEvolution_PassesPolicy_WithMinorRecommendation()
    {
        var console = new RecordingConsole();
        var code = CommandLineRunner.Run(
        [
            "compare", P("v1.yaml"), P("v2-safe.yaml"),
            "--json", Path.Combine(_artifactDir, "safe.json"),
            "--no-color",
        ], console);

        Assert.Equal(ExitCodes.Ok, code);
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(_artifactDir, "safe.json")));
        var rec = doc.RootElement.GetProperty("recommendation");
        Assert.Equal("MINOR", rec.GetProperty("display").GetString());
        Assert.Equal(0, doc.RootElement.GetProperty("summary").GetProperty("breaking").GetInt32());
    }

    [Fact]
    public void BaselineWorkflow_AcknowledgesKnownBreaks_ThenFlagsNewOnes()
    {
        // Step 1: adopt the current breaking diff as the baseline.
        var baseline = Path.Combine(_artifactDir, "baseline.json");
        var adopt = new RecordingConsole();
        var adoptCode = CommandLineRunner.Run(
        [
            "compare", P("v1.yaml"), P("v2-breaking.yaml"),
            "--update-baseline", baseline,
            "--fail-on", "none",
        ], adopt);
        Assert.Equal(ExitCodes.Ok, adoptCode);
        Assert.True(File.Exists(baseline));

        // Step 2: same comparison again — everything baselined, policy passes.
        var rerun = new RecordingConsole();
        var rerunCode = CommandLineRunner.Run(
        [
            "compare", P("v1.yaml"), P("v2-breaking.yaml"),
            "--baseline", baseline,
            "--no-color",
        ], rerun);
        Assert.Equal(ExitCodes.Ok, rerunCode);
        Assert.Contains("Suppressed (", rerun.OutText, StringComparison.Ordinal);

        // Step 3: a NEW safe evolution on top still surfaces only its own findings —
        // the baseline must not mask genuinely new changes (v1 → v2-safe here).
        var fresh = new RecordingConsole();
        var freshCode = CommandLineRunner.Run(
        [
            "compare", P("v1.yaml"), P("v2-safe.yaml"),
            "--baseline", baseline,
            "--no-color",
        ], fresh);
        Assert.Equal(ExitCodes.Ok, freshCode);
        Assert.DoesNotContain("CG1002", fresh.OutText, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        try { Directory.Delete(_artifactDir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }
}
