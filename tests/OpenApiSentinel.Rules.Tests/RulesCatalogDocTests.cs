using System.Text.RegularExpressions;
using OpenApiSentinel.Rules;

namespace OpenApiSentinel.Rules.Tests;

/// <summary>
/// Prevents documentation drift: every rule in the registry must be present in
/// docs/rules.md. If this test fails after adding a rule, regenerate the catalog
/// (openapi-sentinel rules --format json → docs/rules.md).
/// </summary>
public class RulesCatalogDocTests
{
    [Fact]
    public void DocsRulesMarkdown_ContainsEveryRegisteredRuleId()
    {
        var repoRoot = RepoRoot();
        var docsPath = Path.Combine(repoRoot, "docs", "rules.md");
        Assert.True(File.Exists(docsPath), $"Missing rule catalog: {docsPath}");
        var markdown = File.ReadAllText(docsPath);

        var missing = RuleRegistry.CreateDefault().All
            .Select(r => r.Descriptor.Id)
            .Where(id => !Regex.IsMatch(markdown, $@"\b{Regex.Escape(id)}\b"))
            .OrderBy(id => id)
            .ToList();

        Assert.True(missing.Count == 0,
            "docs/rules.md is missing rules: " + string.Join(", ", missing)
            + ". Regenerate the catalog.");
    }

    [Fact]
    public void DocsRulesMarkdown_RuleCount_MatchesRegistry()
    {
        var all = RuleRegistry.CreateDefault().All.ToList();
        var coreCount = all.Count(r => !r.Descriptor.Id.StartsWith("CG9"));
        var metaCount = all.Count - coreCount;
        var markdown = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "rules.md"));

        var declaredCore = Regex.Match(markdown, @"\*\*(\d+) built-in rules\*\*");
        Assert.True(declaredCore.Success, "catalog header does not declare a rule count");
        Assert.Equal(coreCount.ToString(), declaredCore.Groups[1].Value);

        if (metaCount > 0)
        {
            // Engine meta-checks are listed separately from classification rules.
            foreach (var id in all.Where(r => r.Descriptor.Id.StartsWith("CG9")).Select(r => r.Descriptor.Id))
                Assert.Contains(id, markdown, StringComparison.Ordinal);
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OpenApiSentinel.slnx")))
            dir = dir.Parent!;
        return dir!.FullName;
    }
}
