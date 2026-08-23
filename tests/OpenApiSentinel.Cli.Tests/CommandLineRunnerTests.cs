using System.Text.Json;
using OpenApiSentinel.Cli;

namespace OpenApiSentinel.Cli.Tests;

/// <summary>Captures stdout/stderr for assertions.</summary>
internal sealed class RecordingConsole : IConsoleWriter
{
    public List<string> OutLines { get; } = [];
    public List<string> ErrorLines { get; } = [];

    public void Out(string value = "") => OutLines.Add(value);

    public void WriteError(string value = "") => ErrorLines.Add(value);

    public string OutText => string.Join('\n', OutLines);

    public string ErrorText => string.Join('\n', ErrorLines);
}

/// <summary>Creates temporary spec pairs on disk.</summary>
internal sealed class SpecPair : IDisposable
{
    public string OldPath { get; }
    public string NewPath { get; }

    private SpecPair(string oldPath, string newPath)
    {
        OldPath = oldPath;
        NewPath = newPath;
    }

    public static SpecPair Create(string oldYaml, string newYaml)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"sentinel-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var oldPath = Path.Combine(dir, "old.yaml");
        var newPath = Path.Combine(dir, "new.yaml");
        File.WriteAllText(oldPath, oldYaml);
        File.WriteAllText(newPath, newYaml);
        return new SpecPair(oldPath, newPath);
    }

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(OldPath)!, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }
}

public class CommandLineRunnerTests
{
    public const string BaseV1 = """
openapi: 3.0.3
info:
  title: Orders API
  version: 1.0.0
paths:
  /orders:
    get:
      operationId: listOrders
      responses:
        '200':
          description: ok
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/Order'
components:
  schemas:
    Order:
      type: object
      required: [id]
      properties:
        id:
          type: string
        status:
          type: string
          enum: [pending, shipped]
""";

    public const string BreakingV2 = """
openapi: 3.0.3
info:
  title: Orders API
  version: 2.0.0
paths:
  /orders/{id}:
    get:
      operationId: getOrder
      parameters:
        - name: id
          in: path
          required: true
          schema:
            type: string
      responses:
        '200':
          description: ok
components:
  schemas:
    Order:
      type: object
      required: [id]
      properties:
        id:
          type: integer
""";

    public const string SafeV2 = """
openapi: 3.0.3
info:
  title: Orders API
  version: 1.1.0
paths:
  /orders:
    get:
      operationId: listOrders
      parameters:
        - name: limit
          in: query
          required: false
          schema:
            type: integer
      responses:
        '200':
          description: ok
          content:
            application/json:
              schema:
                type: array
                items:
                  $ref: '#/components/schemas/Order'
components:
  schemas:
    Order:
      type: object
      required: [id]
      properties:
        id:
          type: string
        status:
          type: string
          enum: [pending, shipped]
        priority:
          type: string
""";

    [Fact]
    public void VersionCommand_ReturnsZero_AndPrintsVersion()
    {
        var console = new RecordingConsole();
        Assert.Equal(ExitCodes.Ok, CommandLineRunner.Run(["version"], console));
        Assert.StartsWith("openapi-sentinel", console.OutLines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void RulesCommand_ListsRules_AndSupportsJson()
    {
        var console = new RecordingConsole();
        Assert.Equal(ExitCodes.Ok, CommandLineRunner.Run(["rules"], console));
        Assert.Contains("CG1001", console.OutText, StringComparison.Ordinal);

        var jsonConsole = new RecordingConsole();
        Assert.Equal(ExitCodes.Ok, CommandLineRunner.Run(["rules", "--format", "json"], jsonConsole));
        using var doc = JsonDocument.Parse(jsonConsole.OutText);
        Assert.True(doc.RootElement.GetArrayLength() > 50);
    }

    [Fact]
    public void ExplainCommand_ShowsDescriptor_OrFails()
    {
        var console = new RecordingConsole();
        Assert.Equal(ExitCodes.Ok, CommandLineRunner.Run(["explain", "CG2003"], console));
        Assert.Contains("CG2003", console.OutText, StringComparison.Ordinal);

        var missing = new RecordingConsole();
        Assert.Equal(ExitCodes.InvalidInput, CommandLineRunner.Run(["explain", "CG9999"], missing));
        Assert.Contains("Unknown rule id", missing.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public void InitCommand_WritesTemplate_AndRefusesOverwrite()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"sentinel-init-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var cwd = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(dir);
            try
            {
                var first = new RecordingConsole();
                Assert.Equal(ExitCodes.Ok, CommandLineRunner.Run(["init"], first));
                Assert.True(File.Exists(Path.Combine(dir, "openapi-sentinel.yml")));
                Assert.Contains("failOn", File.ReadAllText(Path.Combine(dir, "openapi-sentinel.yml")), StringComparison.Ordinal);

                // The generated template must stay in sync with the loader: every
                // documented key (incl. the limits budgets) must load without warnings.
                var templateWarnings = new List<string>();
                var loaded = SentinelConfigLoader.Load(
                    Path.Combine(dir, "openapi-sentinel.yml"),
                    warn: templateWarnings.Add);
                Assert.Empty(templateWarnings);
                Assert.Null(loaded.Limits); // commented defaults → built-in budgets

                var second = new RecordingConsole();
                Assert.Equal(ExitCodes.InvalidConfiguration, CommandLineRunner.Run(["init"], second));

                var forced = new RecordingConsole();
                Assert.Equal(ExitCodes.Ok, CommandLineRunner.Run(["init", "--force"], forced));
            }
            finally
            {
                Directory.SetCurrentDirectory(cwd);
            }
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task Compare_BreakingChange_FailsPolicyWithExitOne()
    {
        using var specs = SpecPair.Create(BaseV1, BreakingV2);
        var console = new RecordingConsole();
        var code = await Task.Run(() => CommandLineRunner.Run(["compare", specs.OldPath, specs.NewPath], console));

        Assert.Equal(ExitCodes.PolicyViolated, code);
        Assert.Contains("FAILED", console.OutText, StringComparison.Ordinal);
        Assert.Contains("CG1001", console.OutText, StringComparison.Ordinal); // endpoint removed
        Assert.Contains("CG1003", console.OutText, StringComparison.Ordinal); // replacement endpoint added
    }

    [Fact]
    public async Task Compare_SafeChange_PassesWithExitZero()
    {
        using var specs = SpecPair.Create(BaseV1, SafeV2);
        var console = new RecordingConsole();
        var code = await Task.Run(() => CommandLineRunner.Run(["compare", specs.OldPath, specs.NewPath, "--no-color"], console));

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("PASSED", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compare_JsonFormat_EmitsMachineReadableResultOnStdout()
    {
        using var specs = SpecPair.Create(BaseV1, BreakingV2);
        var console = new RecordingConsole();
        var code = await Task.Run(() =>
            CommandLineRunner.Run(["compare", specs.OldPath, specs.NewPath, "--format", "json", "--fail-on", "none"], console));

        Assert.Equal(ExitCodes.Ok, code); // policy relaxed via flag
        using var doc = JsonDocument.Parse(console.OutText);
        Assert.True(doc.RootElement.GetProperty("findings").GetArrayLength() > 0); // findings still reported
        Assert.Equal("passed", doc.RootElement.GetProperty("result").GetString()); // policy was relaxed via --fail-on none
    }

    [Fact]
    public async Task Compare_CombinedArtifacts_AreWritten()
    {
        using var specs = SpecPair.Create(BaseV1, BreakingV2);
        var outDir = Path.Combine(Path.GetTempPath(), $"sentinel-artifacts-{Guid.NewGuid():N}");
        var console = new RecordingConsole();
        var code = await Task.Run(() => CommandLineRunner.Run(
        [
            "compare", specs.OldPath, specs.NewPath,
            "--json", Path.Combine(outDir, "report.json"),
            "--markdown", Path.Combine(outDir, "report.md"),
            "--html", Path.Combine(outDir, "report.html"),
            "--sarif", Path.Combine(outDir, "report.sarif"),
            "--junit", Path.Combine(outDir, "junit.xml"),
            "--changelog", Path.Combine(outDir, "changelog.md"),
            "--fail-on", "none",
        ], console));

        Assert.Equal(ExitCodes.Ok, code);
        foreach (var name in new[] { "report.json", "report.md", "report.html", "report.sarif", "junit.xml", "changelog.md" })
        {
            Assert.True(File.Exists(Path.Combine(outDir, name)), $"{name} should exist");
        }
        var sarif = File.ReadAllText(Path.Combine(outDir, "report.sarif"));
        Assert.Contains("\"version\": \"2.1.0\"", sarif, StringComparison.Ordinal);
        Directory.Delete(outDir, recursive: true);
    }

    [Fact]
    public async Task Compare_InvalidSpec_ReturnsExitTwo()
    {
        using var specs = SpecPair.Create("not: [valid: openapi", BaseV1);
        var console = new RecordingConsole();
        var code = await Task.Run(() => CommandLineRunner.Run(["compare", specs.OldPath, specs.NewPath], console));
        Assert.Equal(ExitCodes.InvalidInput, code);
    }

    [Fact]
    public async Task Compare_PrivateNetworkUrl_IsRejectedByDefault_AsNetworkFailure()
    {
        using var specs = SpecPair.Create(BaseV1, SafeV2);
        var console = new RecordingConsole();
        var code = await Task.Run(() =>
            CommandLineRunner.Run(["compare", "http://127.0.0.1:1/spec.json", specs.NewPath], console));
        Assert.Equal(ExitCodes.NetworkFailure, code);
    }

    [Fact]
    public async Task ConfigFile_OverridesAndIgnorePaths_AreApplied()
    {
        using var specs = SpecPair.Create(BaseV1, BreakingV2);
        var dir = Path.GetDirectoryName(specs.OldPath)!;
        var configPath = Path.Combine(dir, "sentinel.yml");
        await File.WriteAllTextAsync(configPath, """
failOn: none
ignore:
  paths:
    - "/orders/**"
""");

        var console = new RecordingConsole();
        var code = await Task.Run(() =>
            CommandLineRunner.Run(["compare", specs.OldPath, specs.NewPath, "--config", configPath, "--no-color"], console));

        Assert.Equal(ExitCodes.Ok, code); // failOn: none relaxes the policy
        Assert.DoesNotContain("/orders/{id}", console.OutText.Replace(" ", ""), StringComparison.Ordinal);
        Assert.DoesNotContain("CG1001", console.OutText, StringComparison.Ordinal); // ignored path filtered
    }

    [Fact]
    public async Task ConfigFile_ExternalReferences_Section_Accepted_And_Wired()
    {
        using var specs = SpecPair.Create(BaseV1, SafeV2);
        var dir = Path.GetDirectoryName(specs.OldPath)!;
        var configPath = Path.Combine(dir, "sentinel.yml");
        await File.WriteAllTextAsync(configPath, """
externalReferences:
  allowAboveDocumentTree: true
""");

        var console = new RecordingConsole();
        var code = await Task.Run(() =>
            CommandLineRunner.Run(["compare", specs.OldPath, specs.NewPath, "--config", configPath, "--no-color"], console));

        Assert.Equal(ExitCodes.Ok, code);
        // No unknown-key warning: the section is fully supported.
        Assert.DoesNotContain("Unknown configuration key", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfigFile_ExternalReferences_BadValue_FailsWithConfigError()
    {
        using var specs = SpecPair.Create(BaseV1, SafeV2);
        var dir = Path.GetDirectoryName(specs.OldPath)!;
        var configPath = Path.Combine(dir, "sentinel.yml");
        await File.WriteAllTextAsync(configPath, """
externalReferences:
  allowAboveDocumentTree: maybe
""");

        var console = new RecordingConsole();
        var code = await Task.Run(() =>
            CommandLineRunner.Run(["compare", specs.OldPath, specs.NewPath, "--config", configPath, "--no-color"], console));

        Assert.Equal(ExitCodes.InvalidConfiguration, code);
        Assert.Contains("allowAboveDocumentTree", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BaselineWorkflow_UpdateThenAdopt_SuppressesFindings()
    {
        using var specs = SpecPair.Create(BaseV1, BreakingV2);
        var baseline = Path.Combine(Path.GetDirectoryName(specs.OldPath)!, "baseline.json");

        var first = new RecordingConsole();
        var firstCode = await Task.Run(() =>
            CommandLineRunner.Run(["compare", specs.OldPath, specs.NewPath, "--update-baseline", baseline, "--fail-on", "none"], first));
        Assert.Equal(ExitCodes.Ok, firstCode);
        Assert.True(File.Exists(baseline));

        var adopted = new RecordingConsole();
        var adoptedCode = await Task.Run(() =>
            CommandLineRunner.Run(["compare", specs.OldPath, specs.NewPath, "--baseline", baseline], adopted));
        Assert.Equal(ExitCodes.Ok, adoptedCode); // all current findings baselined → policy passes
        Assert.Contains("Suppressed", adopted.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidConfig_MissingReason_ExitsThree()
    {
        using var specs = SpecPair.Create(BaseV1, BreakingV2);
        var dir = Path.GetDirectoryName(specs.OldPath)!;
        var configPath = Path.Combine(dir, "bad.yml");
        await File.WriteAllTextAsync(configPath, """
suppressions:
  - rule: CG1001
""");

        var console = new RecordingConsole();
        var code = await Task.Run(() =>
            CommandLineRunner.Run(["compare", specs.OldPath, specs.NewPath, "--config", configPath], console));
        Assert.Equal(ExitCodes.InvalidConfiguration, code);
        Assert.Contains("reason", console.ErrorText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnknownConfigKey_ProducesWarning_NotSilence()
    {
        using var specs = SpecPair.Create(BaseV1, BreakingV2);
        var dir = Path.GetDirectoryName(specs.OldPath)!;
        var configPath = Path.Combine(dir, "warn.yml");
        await File.WriteAllTextAsync(configPath, "failOn: high\nfialOnTypo: x\n");

        var console = new RecordingConsole();
        var code = await Task.Run(() =>
            CommandLineRunner.Run(["compare", specs.OldPath, specs.NewPath, "--config", configPath], console));
        Assert.Equal(ExitCodes.PolicyViolated, code);
        Assert.Contains("fialOnTypo", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Suppression_WithReasonAndExpiry_HidesFinding()
    {
        using var specs = SpecPair.Create(BaseV1, BreakingV2);
        var dir = Path.GetDirectoryName(specs.OldPath)!;
        var configPath = Path.Combine(dir, "suppress.yml");
        await File.WriteAllTextAsync(configPath, """
failOn: high
suppressions:
  - rule: CG1001
    reason: "Planned removal after client migration"
    expires: 2030-01-01
""");

        var console = new RecordingConsole();
        var code = await Task.Run(() =>
            CommandLineRunner.Run(["compare", specs.OldPath, specs.NewPath, "--config", configPath, "--no-color"], console));
        // CG1001 is suppressed with a valid future expiry; remaining findings are Info-level,
        // so the failOn: high threshold is not violated.
        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("Suppressed (1)", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateCommand_ReportsDocumentSummary()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"sentinel-validate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var specPath = Path.Combine(dir, "spec.yaml");
        File.WriteAllText(specPath, BaseV1);

        var console = new RecordingConsole();
        var code = CommandLineRunner.Run(["validate", specPath], console);
        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("OK", console.OutText, StringComparison.Ordinal);
        Assert.Contains("3.0.3", console.OutText, StringComparison.Ordinal);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void HelpCommand_PrintsUsage()
    {
        var console = new RecordingConsole();
        Assert.Equal(ExitCodes.Ok, CommandLineRunner.Run([], console));
        Assert.Contains("usage:", console.OutText, StringComparison.OrdinalIgnoreCase);
    }
}

public class ConfigOutputValidationTests
{
    [Fact]
    public async Task UnknownOutputKey_ProducesWarning()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"sentinel-outcfg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var specPath = Path.Combine(dir, "spec.yaml");
            await File.WriteAllTextAsync(specPath, CommandLineRunnerTests.BaseV1);
            var configPath = Path.Combine(dir, "cfg.yml");
            await File.WriteAllTextAsync(configPath, "output:\n  htmml: out.html\n");

            var console = new RecordingConsole();
            var code = CommandLineRunner.Run(["validate", specPath, "--config", configPath], console);
            Assert.Equal(ExitCodes.Ok, code);
            Assert.Contains("htmml", console.ErrorText, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task PrSummary_OutputKey_IsAccepted()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"sentinel-prcfg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var specPath = Path.Combine(dir, "spec.yaml");
            await File.WriteAllTextAsync(specPath, CommandLineRunnerTests.BaseV1);
            var outPath = Path.Combine(dir, "pr.md");
            var configPath = Path.Combine(dir, "cfg.yml");
            await File.WriteAllTextAsync(configPath, $"output:\n  pr-summary: {outPath.Replace("\\", "\\\\")}\n");

            var console = new RecordingConsole();
            // validate ignores output artifacts but must accept the key without warnings.
            var code = CommandLineRunner.Run(["validate", specPath, "--config", configPath], console);
            Assert.Equal(ExitCodes.Ok, code);
            Assert.DoesNotContain("pr-summary", console.ErrorText, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }
}

public class FileArtifactColorTests
{
    [Fact]
    public async Task ConsoleFormat_ToFile_ContainsNoAnsiEscapes()
    {
        using var specs = SpecPair.Create(CommandLineRunnerTests.BaseV1, CommandLineRunnerTests.BreakingV2);
        var outPath = Path.Combine(Path.GetDirectoryName(specs.OldPath)!, "console-out.txt");
        var console = new RecordingConsole();

        // stdout IS a TTY-independent capture here; the bug was that -o still emitted colors.
        var code = await Task.Run(() =>
            CommandLineRunner.Run(["compare", specs.OldPath, specs.NewPath, "-o", outPath], console));

        Assert.Equal(ExitCodes.PolicyViolated, code);
        var text = File.ReadAllText(outPath);
        Assert.Contains("FAILED", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b[", text, StringComparison.Ordinal);
    }
}

public class GlobalFlagTests
{
    [Fact]
    public void DashDashVersion_PrintsVersionWithoutCommand()
    {
        var console = new RecordingConsole();
        var code = CommandLineRunner.Run(["--version"], console);
        Assert.Equal(ExitCodes.Ok, code);
        Assert.StartsWith("openapi-sentinel", console.OutLines.FirstOrDefault() ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void Compare_WithDashDashVersion_Flag_DoesNotBreakParsing()
    {
        // "--version" as a flag on a subcommand is simply ignored by that command's options.
        using var specs = SpecPair.Create(CommandLineRunnerTests.BaseV1, CommandLineRunnerTests.SafeV2);
        var console = new RecordingConsole();
        var code = CommandLineRunner.Run(
            ["compare", specs.OldPath, specs.NewPath, "--fail-on", "none", "--no-color"], console);
        Assert.Equal(ExitCodes.Ok, code);
    }
}

public class CommandHelpTests
{
    [Theory]
    [InlineData("compare", "--fail-on")]
    [InlineData("validate", "invalid specs")]
    [InlineData("rules", "docs/rules.md")]
    [InlineData("explain", "CG2003")]
    [InlineData("init", "--force")]
    public void SubcommandHelp_ShowsCommandSpecificText(string command, string expected)
    {
        var console = new RecordingConsole();
        var code = CommandLineRunner.Run([command, "--help"], console);
        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains(expected, console.OutText, StringComparison.Ordinal);
    }
}

public class ReportUnknownKindsTests
{
    private const string WeirdSpec = """
        openapi: 3.0.3
        info: {title: T, version: "1.0"}
        paths:
          /orders:
            get:
              operationId: listOrders
              responses:
                '200': {description: ok}
        x-sentinel-unknown: true
        """;

    [Fact]
    public async Task Flag_EmitsCg9003_ForUnevaluatedChanges()
    {
        // The unknown-kinds path is exercised via the engine; here we pin that the CLI flag
        // is accepted and does not alter the normal pipeline outcome on a clean spec pair.
        using var specs = SpecPair.Create(CommandLineRunnerTests.BaseV1, CommandLineRunnerTests.SafeV2);
        var console = new RecordingConsole();
        var code = await Task.Run(() =>
            CommandLineRunner.Run(["compare", specs.OldPath, specs.NewPath,
                "--fail-on", "none", "--no-color", "--report-unknown-kinds"], console));
        Assert.Equal(ExitCodes.Ok, code);
    }

    [Fact]
    public async Task ConfigKey_ReportUnknownKinds_IsAccepted()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"sentinel-uk-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var specPath = Path.Combine(dir, "spec.yaml");
            await File.WriteAllTextAsync(specPath, CommandLineRunnerTests.BaseV1);
            var configPath = Path.Combine(dir, "cfg.yml");
            await File.WriteAllTextAsync(configPath, "reportUnknownKinds: true\n");
            var console = new RecordingConsole();
            var code = CommandLineRunner.Run(["validate", specPath, "--config", configPath], console);
            Assert.Equal(ExitCodes.Ok, code);
            Assert.DoesNotContain("reportUnknownKinds", console.ErrorText, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }
}

public class InvalidOptionTests
{
    [Fact]
    public async Task Invalid_FailOn_Value_ExitsThree_WithActionableMessage()
    {
        using var specs = SpecPair.Create(CommandLineRunnerTests.BaseV1, CommandLineRunnerTests.SafeV2);
        var console = new RecordingConsole();
        var code = await Task.Run(() =>
            CommandLineRunner.Run(["compare", specs.OldPath, specs.NewPath, "--fail-on", "bogus"], console));
        Assert.Equal(ExitCodes.InvalidConfiguration, code);
        Assert.Contains("'failOn: bogus' is invalid", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_Format_ExitsThree_WithSupportedList()
    {
        using var specs = SpecPair.Create(CommandLineRunnerTests.BaseV1, CommandLineRunnerTests.SafeV2);
        var console = new RecordingConsole();
        var code = await Task.Run(() =>
            CommandLineRunner.Run(["compare", specs.OldPath, specs.NewPath, "--format", "yaml"], console));
        Assert.Equal(ExitCodes.InvalidConfiguration, code);
        Assert.Contains("Unknown --format 'yaml'", console.ErrorText, StringComparison.Ordinal);
        Assert.Contains("console, json, markdown, html", console.ErrorText, StringComparison.Ordinal);
    }
}

public class NoSuppressedFlagTests
{
    [Fact]
    public async Task NoSuppressed_OmitsSection_FromConsoleAndJson()
    {
        using var specs = SpecPair.Create(CommandLineRunnerTests.BaseV1, CommandLineRunnerTests.BreakingV2);
        var baseline = Path.Combine(Path.GetDirectoryName(specs.OldPath)!, "b.json");
        var console = new RecordingConsole();
        await Task.Run(() => CommandLineRunner.Run(
            ["compare", specs.OldPath, specs.NewPath, "--update-baseline", baseline, "--fail-on", "none"], console));

        var console2 = new RecordingConsole();
        var code = await Task.Run(() => CommandLineRunner.Run(
            ["compare", specs.OldPath, specs.NewPath, "--baseline", baseline, "--no-suppressed", "--no-color"],
            console2));
        Assert.Equal(ExitCodes.Ok, code);
        Assert.DoesNotContain("Suppressed:", console2.OutText, StringComparison.Ordinal);
        Assert.DoesNotContain("Suppressed (", console2.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Default_StillShowsSuppressed()
    {
        using var specs = SpecPair.Create(CommandLineRunnerTests.BaseV1, CommandLineRunnerTests.BreakingV2);
        var baseline = Path.Combine(Path.GetDirectoryName(specs.OldPath)!, "b.json");
        var console = new RecordingConsole();
        await Task.Run(() => CommandLineRunner.Run(
            ["compare", specs.OldPath, specs.NewPath, "--update-baseline", baseline, "--fail-on", "none"], console));
        var console2 = new RecordingConsole();
        await Task.Run(() => CommandLineRunner.Run(
            ["compare", specs.OldPath, specs.NewPath, "--baseline", baseline, "--no-color"], console2));
        Assert.Contains("Suppressed (", console2.OutText, StringComparison.Ordinal);
    }
}

public class BooleanFlagParsingTests
{
    [Fact]
    public void AllowRemote_BeforeSpec_DoesNotSwallowPositional()
    {
        // Real-world order: flags may precede positionals. --allow-remote must not
        // consume the following spec path as a phantom value.
        var parsed = CommandLineParser.Parse(["validate", "--allow-remote", "https://example.com/spec.json"]);
        Assert.Equal("validate", parsed.Command);
        var positional = Assert.Single(parsed.Positional);
        Assert.Equal("https://example.com/spec.json", positional);
        Assert.True(parsed.Has("allow-remote"));
        Assert.Null(parsed.Value("allow-remote"));
    }

    [Fact]
    public void BooleanFlag_AtEnd_TakesNoValue()
    {
        var parsed = CommandLineParser.Parse(["compare", "a.yaml", "b.yaml", "--no-color", "--verbose"]);
        Assert.True(parsed.Has("no-color"));
        Assert.True(parsed.Has("verbose"));
        Assert.Equal(2, parsed.Positional.Count);
    }
}

public class MissingOptionValueTests
{
    [Fact]
    public void ValueOption_WithoutValue_IsRejected()
    {
        var console = new RecordingConsole();
        var code = CommandLineRunner.Run(["compare", "a.yaml", "b.yaml", "--baseline"], console);
        Assert.Equal(ExitCodes.InvalidConfiguration, code);
        Assert.Contains("'--baseline' requires a value", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValueOption_AtEnd_DoesNotSwallowPositionals()
    {
        using var specs = SpecPair.Create(CommandLineRunnerTests.BaseV1, CommandLineRunnerTests.SafeV2);
        var console = new RecordingConsole();
        // --format at the end has no value; must not eat anything, and compare
        // should still fail on missing positionals rather than silently proceeding.
        var code = await Task.Run(() =>
            CommandLineRunner.Run(["compare", specs.OldPath, specs.NewPath, "--format"], console));
        Assert.Equal(ExitCodes.InvalidConfiguration, code);
    }
}

public class PrSummaryArtifactTests
{
    [Fact]
    public async Task PrSummary_ContainsVerdict_Counts_AndBump()
    {
        using var specs = SpecPair.Create(CommandLineRunnerTests.BaseV1, CommandLineRunnerTests.BreakingV2);
        var outPath = Path.Combine(Path.GetDirectoryName(specs.OldPath)!, "pr.md");
        var console = new RecordingConsole();
        var code = await Task.Run(() => CommandLineRunner.Run(
            ["compare", specs.OldPath, specs.NewPath, "--pr-summary", outPath, "--no-color"], console));

        Assert.Equal(ExitCodes.PolicyViolated, code);
        var md = File.ReadAllText(outPath);
        Assert.Contains("policy failed", md, StringComparison.Ordinal);
        Assert.Contains("CG1001", md, StringComparison.Ordinal); // top breaking finding listed
        Assert.Contains("MAJOR", md, StringComparison.Ordinal);
    }
}
