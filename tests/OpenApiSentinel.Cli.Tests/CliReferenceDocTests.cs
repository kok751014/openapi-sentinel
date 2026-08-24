using Xunit;

namespace OpenApiSentinel.Cli.Tests;

/// <summary>
/// Doc-drift guard: every option flag advertised by a command's --help output must appear
/// in docs/cli-reference.md (mirrors the rules-catalog guard). Prevents the docs and the
/// CLI surface from diverging silently when flags are added or renamed.
/// </summary>
public sealed class CliReferenceDocTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OpenApiSentinel.slnx")))
            dir = dir.Parent!;
        return dir!.FullName;
    }

    private static string HelpFor(params string[] args)
    {
        var console = new RecordingConsole();
        var code = CommandLineRunner.Run([.. args, "--help"], console);
        Assert.Equal(ExitCodes.Ok, code);
        return console.OutText;
    }

    public static IEnumerable<object[]> CommandsWithOptions() =>
    [
        [new object[] { "compare" }],
        [new object[] { "rules" }],
        [new object[] { "init" }],
    ];

    [Theory]
    [MemberData(nameof(CommandsWithOptions))]
    public void Every_Advertised_Flag_Is_Documented(string[] args)
    {
        var doc = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "cli-reference.md"));
        var help = HelpFor(args);

        var flags = System.Text.RegularExpressions.Regex.Matches(help, @"--[a-z][a-z0-9-]*")
            .Select(m => m.Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(flags);
        foreach (var flag in flags)
            Assert.True(
                doc.Contains(flag, StringComparison.Ordinal),
                $"Flag '{flag}' shown in '{string.Join(' ', args)} --help' is not documented in docs/cli-reference.md");
    }

    [Fact]
    public void Every_Command_Is_Documented()
    {
        var doc = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "cli-reference.md"));
        foreach (var command in new[] { "compare", "validate", "rules", "explain", "init", "version" })
            Assert.True(
                doc.Contains($"### `{command}", StringComparison.Ordinal),
                $"Command '{command}' has no section in docs/cli-reference.md");
    }
}
