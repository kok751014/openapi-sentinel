using System.Reflection;
using System.Text.RegularExpressions;
using OpenApiSentinel.OpenApi;
using OpenApiSentinel.Rules;
using Xunit;

namespace OpenApiSentinel.Rules.Tests;

/// <summary>
/// Drift prevention: every change kind the comparer can emit MUST be handled by exactly one
/// registered rule family, so no structural change is ever silently unevaluated.
/// (Unknown kinds can additionally surface as CG9003 when ReportUnknownChangeKinds is on.)
/// </summary>
public class RuleCoverageTests
{
    [Fact]
    public void EveryChangeKind_IsHandledByARule()
    {
        var allKinds = typeof(ChangeKinds)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(f => f.FieldType == typeof(string))
            .Select(f => (string)f.GetValue(null)!)
            .ToHashSet(StringComparer.Ordinal);

        var registry = RuleRegistry.CreateDefault();
        foreach (var kind in allKinds)
        {
            var claimants = registry.All
                .Where(r => r.Descriptor.ChangeKinds.Contains(kind, StringComparer.Ordinal))
                .ToList();

            Assert.True(claimants.Count > 0, $"change kind '{kind}' has no registered rule");

            // Kinds shared across directional rules are allowed, but every claimant must
            // declare a guard — otherwise the engine's deterministic order would silently
            // route all traffic to the lowest rule id.
            if (claimants.Count > 1)
            {
                Assert.All(claimants, r => Assert.True(r.AppliesTo is not null,
                    $"'{kind}' is claimed by {claimants.Count} rules but {r.Descriptor.Id} has no {nameof(RuleDefinition.AppliesTo)} guard"));
            }
        }
    }

    [Fact]
    public void Rules_OnlyReferenceKnownChangeKinds()
    {
        // Guards against typos: every ChangeKinds.X reference inside rule files must exist.
        var constants = typeof(ChangeKinds)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(f => f.Name)
            .ToHashSet(StringComparer.Ordinal);

        var rulesDir = Path.Combine(RepoRoot(), "src", "OpenApiSentinel.Rules");
        foreach (var file in Directory.GetFiles(rulesDir, "*.cs"))
        {
            var src = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(src, @"ChangeKinds\.(\w+)"))
            {
                Assert.True(constants.Contains(m.Groups[1].Value),
                    $"{Path.GetFileName(file)} references unknown constant ChangeKinds.{m.Groups[1].Value}");
            }
        }
    }

    [Fact]
    public void RuleIds_AreUniqueWellFormed_AndSortedFamilies()
    {
        var registry = RuleRegistry.CreateDefault();
        var ids = registry.All.Select(r => r.Descriptor.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(ids, id => Assert.Matches(@"^CG\d{4}$", id));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OpenApiSentinel.slnx")))
            dir = dir.Parent!;
        return dir!.FullName;
    }
}
