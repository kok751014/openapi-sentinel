using Xunit;

namespace OpenApiSentinel.Cli.Tests;

/// <summary>
/// Guards the composite GitHub Action (action/action.yml). The action's shell script is only
/// executed on real runners, so structural invariants are pinned here instead:
/// - exactly one sentinel step whose single env block carries ALL inputs it reads
///   (a duplicate env key once silently dropped --fail-on: last-key-wins in YAML);
/// - the annotation filter matches the JSON contract's lowercase compatibility values;
/// - declared outputs are exactly the ones the script writes to GITHUB_OUTPUT.
/// </summary>
public sealed class ActionYamlTests
{
    private static Dictionary<string, object?> LoadAction()
    {
        var path = FindRepoRoot();
        var yaml = File.ReadAllText(Path.Combine(path, "action", "action.yml"));
        return new YamlDotNet.Serialization.DeserializerBuilder().Build()
            .Deserialize<Dictionary<string, object?>>(yaml);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OpenApiSentinel.slnx")))
            dir = dir.Parent!;
        return dir!.FullName;
    }

    [Fact]
    public void Sentinel_Step_Has_Single_Complete_Env_Block()
    {
        var steps = LoadAction()["runs"]!
            .AsCompositeSteps();
        var step = steps.SentinelStep();

        var env = Assert.IsType<Dictionary<object, object?>>(step!["env"]);
        var required = new[]
        {
            "INPUT_BASE", "INPUT_HEAD", "INPUT_FAILON",
            "INPUT_CONFIG", "INPUT_REPORT_DIR", "INPUT_ALLOW_REMOTE",
        };
        foreach (var key in required)
            Assert.True(env.ContainsKey(key), $"missing env '{key}'");

        // Every ${{ inputs.x }} binding must live in that one block; the legacy
        // misspelled variable must stay gone from both env and script.
        var run = Assert.IsType<string>(step["run"]);
        Assert.DoesNotContain("INPUTS_FAILON", run, StringComparison.Ordinal);
        Assert.Contains("INPUT_FAILON", run, StringComparison.Ordinal);
    }

    [Fact]
    public void Annotation_Filter_Matches_Json_Contract_Casing()
    {
        var steps = LoadAction()["runs"]!.AsCompositeSteps();
        var run = steps.SentinelStep()["run"] as string ?? "";

        // JSON contract emits lowercase compatibility values ("breaking"/"potentiallyBreaking").
        Assert.Contains(".lower()", run, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Breaking\"", run, StringComparison.Ordinal);
    }

    [Fact]
    public void Declared_Outputs_Match_Github_Output_Writes()
    {
        var action = LoadAction();
        var declared = (action["outputs"] as Dictionary<object, object?>)!
            .Keys.Select(k => k.ToString()!).ToHashSet(StringComparer.Ordinal);

        var steps = action["runs"]!.AsCompositeSteps();
        var run = steps.SentinelStep()["run"] as string ?? "";

        foreach (var name in declared)
            Assert.True(
                run.Contains($"{name}=", StringComparison.Ordinal),
                $"output '{name}' is declared but never written");
    }
}

internal static class ActionYamlTestExtensions
{
    public static Dictionary<object, object?> SentinelStep(
        this IEnumerable<Dictionary<object, object?>> steps)
    {
        var match = Assert.Single(steps, s => s.TryGetValue("id", out var v) && Equals(v, "sentinel"));
        return match;
    }

    /// <summary>Unwraps the runs.steps node regardless of deserializer element typing.</summary>
    public static IEnumerable<Dictionary<object, object?>> AsCompositeSteps(this object runs)
    {
        var runsDict = Assert.IsType<Dictionary<object, object?>>(runs);
        var steps = Assert.IsType<List<object?>>(runsDict["steps"]);
        return steps.Select(Assert.IsType<Dictionary<object, object?>>);
    }
}
