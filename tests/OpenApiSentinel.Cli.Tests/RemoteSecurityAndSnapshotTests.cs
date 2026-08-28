using OpenApiSentinel.Cli;

namespace OpenApiSentinel.Cli.Tests;

public sealed class RemoteSecurityAndSnapshotTests
{
    [Fact]
    public async Task HtmlLanguage_InvalidValue_IsConfigurationError()
    {
        using var specs = SpecPair.Create(CommandLineRunnerTests.BaseV1, CommandLineRunnerTests.SafeV2);
        var console = new RecordingConsole();

        var exit = await Task.Run(() => CommandLineRunner.Run(
            ["compare", specs.OldPath, specs.NewPath, "--html-lang", "ja", "--no-color"], console));

        Assert.Equal(ExitCodes.InvalidConfiguration, exit);
        Assert.Contains("Supported HTML languages: zh-TW, en", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public void InsecureTls_DefaultsFalse_And_ConfigParsesExplicitValues()
    {
        var temp = Path.Combine(Path.GetTempPath(), $"sentinel-config-{Guid.NewGuid():N}.yml");
        try
        {
            File.WriteAllText(temp, "remoteReferences:\n  enabled: true\n  insecureTls: true\n");
            var enabled = SentinelConfigLoader.Load(temp);
            Assert.True(enabled.RemoteReferences.InsecureTls);

            File.WriteAllText(temp, "remoteReferences:\n  enabled: true\n  insecureTls: false\n");
            var disabled = SentinelConfigLoader.Load(temp);
            Assert.False(disabled.RemoteReferences.InsecureTls);
            Assert.False(new SentinelConfiguration().RemoteReferences.InsecureTls);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    [Fact]
    public void RemoteSecurityBooleanFlags_DoNotConsumePositionals()
    {
        var parsed = CommandLineParser.Parse([
            "compare", "--allow-remote", "--allow-private-network", "--insecure-tls", "--update-snapshot", "old.yaml", "new.yaml"]);

        Assert.Equal(["old.yaml", "new.yaml"], parsed.Positional);
        Assert.True(parsed.Has("allow-remote"));
        Assert.True(parsed.Has("allow-private-network"));
        Assert.True(parsed.Has("insecure-tls"));
        Assert.True(parsed.Has("update-snapshot"));
    }

    [Fact]
    public async Task Snapshot_Pass_ReplacesOldWithRawNewDocument()
    {
        using var specs = SpecPair.Create(CommandLineRunnerTests.BaseV1, CommandLineRunnerTests.SafeV2);
        var expected = File.ReadAllText(specs.NewPath);
        var console = new RecordingConsole();

        var exit = await Task.Run(() => CommandLineRunner.Run(
            ["compare", specs.OldPath, specs.NewPath, "--fail-on", "high", "--update-snapshot", "--no-color"], console));

        Assert.Equal(ExitCodes.Ok, exit);
        Assert.Equal(expected, File.ReadAllText(specs.OldPath));
        Assert.Contains("Snapshot updated:", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Snapshot_HtmlArtifact_IsFinalizedOnlyAfterSuccessfulAtomicUpdate()
    {
        using var specs = SpecPair.Create(CommandLineRunnerTests.BaseV1, CommandLineRunnerTests.SafeV2);
        var report = Path.Combine(Path.GetDirectoryName(specs.OldPath)!, "report.html");
        var expected = File.ReadAllText(specs.NewPath);
        var console = new RecordingConsole();

        var exit = await Task.Run(() => CommandLineRunner.Run(
            ["compare", specs.OldPath, specs.NewPath, "--html", report, "--update-snapshot", "--fail-on", "high", "--no-color"], console));

        Assert.Equal(ExitCodes.Ok, exit);
        Assert.Equal(expected, File.ReadAllText(specs.OldPath));
        Assert.DoesNotContain("Snapshot 狀態", File.ReadAllText(report), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Snapshot_PolicyFailure_LeavesOldByteIdentical()
    {
        using var specs = SpecPair.Create(CommandLineRunnerTests.BaseV1, CommandLineRunnerTests.BreakingV2);
        var before = File.ReadAllText(specs.OldPath);
        var console = new RecordingConsole();

        var exit = await Task.Run(() => CommandLineRunner.Run(
            ["compare", specs.OldPath, specs.NewPath, "--fail-on", "high", "--update-snapshot", "--no-color"], console));

        Assert.Equal(ExitCodes.PolicyViolated, exit);
        Assert.Equal(before, File.ReadAllText(specs.OldPath));
        Assert.Contains("Snapshot not updated because the comparison policy failed.", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Snapshot_PolicyFailure_HtmlDoesNotChangeSnapshotSafety()
    {
        using var specs = SpecPair.Create(CommandLineRunnerTests.BaseV1, CommandLineRunnerTests.BreakingV2);
        var report = Path.Combine(Path.GetDirectoryName(specs.OldPath)!, "report.html");
        var before = File.ReadAllText(specs.OldPath);
        var console = new RecordingConsole();

        var exit = await Task.Run(() => CommandLineRunner.Run(
            ["compare", specs.OldPath, specs.NewPath, "--html", report, "--update-snapshot", "--fail-on", "high", "--no-color"], console));

        Assert.Equal(ExitCodes.PolicyViolated, exit);
        Assert.Equal(before, File.ReadAllText(specs.OldPath));
        Assert.DoesNotContain("Snapshot 狀態", File.ReadAllText(report), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://example.com/openapi.json")]
    [InlineData("git:HEAD:openapi.yaml")]
    [InlineData("-")]
    public async Task Snapshot_NonLocalOldSource_IsRejected(string oldSource)
    {
        using var specs = SpecPair.Create(CommandLineRunnerTests.BaseV1, CommandLineRunnerTests.SafeV2);
        var console = new RecordingConsole();

        var exit = await Task.Run(() => CommandLineRunner.Run(
            ["compare", oldSource, specs.NewPath, "--update-snapshot", "--no-color"], console));

        Assert.Equal(ExitCodes.InvalidConfiguration, exit);
        Assert.Contains("requires OLD-SPEC to be an existing local file path", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Snapshot_OutputCollision_IsRejectedBeforeAnyWrite()
    {
        using var specs = SpecPair.Create(CommandLineRunnerTests.BaseV1, CommandLineRunnerTests.SafeV2);
        var before = File.ReadAllText(specs.OldPath);
        var console = new RecordingConsole();

        var exit = await Task.Run(() => CommandLineRunner.Run(
            ["compare", specs.OldPath, specs.NewPath, "--html", specs.OldPath, "--update-snapshot", "--no-color"], console));

        Assert.Equal(ExitCodes.InvalidConfiguration, exit);
        Assert.Equal(before, File.ReadAllText(specs.OldPath));
        Assert.Contains("collides with OLD-SPEC", console.ErrorText, StringComparison.Ordinal);
    }
}
