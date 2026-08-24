using Xunit;

namespace OpenApiSentinel.Cli.Tests;

/// <summary>
/// Config-doc drift guard: every configuration key the loader accepts
/// (SentinelConfigLoader's allowed top-level and nested keys) must appear in
/// docs/configuration.md — including nested spellings like `output.console`.
/// Found one gap on introduction: `output.console` was accepted but undocumented.
/// </summary>
public sealed class ConfigDocTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OpenApiSentinel.slnx")))
            dir = dir.Parent!;
        return dir!.FullName;
    }

    private static (HashSet<string> Top, Dictionary<string, HashSet<string>> Nested) LoaderKeys()
    {
        var loader = typeof(SentinelConfigLoader);
        var top = Assert.IsType<HashSet<string>>(
            loader.GetField("TopLevelKeys", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .GetValue(null));
        var nested = Assert.IsType<Dictionary<string, HashSet<string>>>(
            loader.GetField("NestedKeys", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .GetValue(null));
        return (top!, nested!);
    }

    [Fact]
    public void Every_Top_Level_Key_Is_Documented()
    {
        var doc = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "configuration.md"));
        var (top, _) = LoaderKeys();

        Assert.NotEmpty(top);
        foreach (var key in top)
            Assert.True(
                doc.Contains(key, StringComparison.Ordinal),
                $"Configuration key '{key}' is accepted by the loader but missing from docs/configuration.md");
    }

    [Fact]
    public void Every_Nested_Key_Is_Documented()
    {
        var doc = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "configuration.md"));
        var (_, nested) = LoaderKeys();

        Assert.NotEmpty(nested);
        foreach (var (section, keys) in nested)
        foreach (var key in keys)
        {
            var documented =
                doc.Contains($"{section}.{key}", StringComparison.Ordinal) ||
                // Keys shown inside their section's YAML block appear bare; accept section
                // name plus bare key elsewhere in the doc as documentation.
                (doc.Contains(section, StringComparison.Ordinal) && doc.Contains(key, StringComparison.Ordinal));
            Assert.True(documented,
                $"Configuration key '{section}.{key}' is accepted by the loader but missing from docs/configuration.md");
        }
    }

    [Fact]
    public void Output_Console_Is_A_Real_Wired_Feature_Not_Just_An_Accepted_Key()
    {
        // output.console writes the console-format report to a file. Pin that behavior
        // so the documented key cannot silently become an ignored no-op.
        var dir = Path.Combine(Path.GetTempPath(), "sentinel-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var configPath = Path.Combine(dir, "sentinel.yml");
            var outFile = Path.Combine(dir, "console-report.txt");
            File.WriteAllText(configPath, $"""
                failOn: none
                output:
                  console: {outFile.Replace("\\", "/")}
                """);

            var console = new RecordingConsole();
            var code = CommandLineRunner.Run(
            [
                "compare",
                Path.Combine(RepoRoot(), "samples", "basic", "v1.yaml"),
                Path.Combine(RepoRoot(), "samples", "basic", "v2-breaking.yaml"),
                "--config", configPath, "--no-color",
            ], console);

            // failOn: none keeps the run passing while still producing every configured
            // artifact — isolates the output.console behavior under test.
            Assert.Equal(ExitCodes.Ok, code);
            Assert.True(File.Exists(outFile), "output.console artifact was not written");
            var text = File.ReadAllText(outFile);
            // failOn: none ⇒ PASSED verdict; the breaking-pair method removal (CG1002) still appears.
            Assert.Contains("CG1002", text, StringComparison.Ordinal);
            Assert.DoesNotContain("\x1b[", text, StringComparison.Ordinal); // file targets are ANSI-free
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
