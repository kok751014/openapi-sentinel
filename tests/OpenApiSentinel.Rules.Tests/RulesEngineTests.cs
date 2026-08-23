using OpenApiSentinel.Core;
using OpenApiSentinel.OpenApi;
using OpenApiSentinel.Rules;

namespace OpenApiSentinel.Rules.Tests;

internal sealed class FakeClock(DateTimeOffset utcNow) : ISentinelClock
{
    public static readonly DateTimeOffset Fixed = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
    public DateTimeOffset UtcNow { get; } = utcNow;
}

internal static class Change
{
    public static SemanticChange Make(
        string kind,
        string path = "/orders",
        HttpMethodKind method = HttpMethodKind.None,
        string location = "",
        ApiDirection direction = ApiDirection.Neutral,
        string? oldValue = null,
        string? newValue = null,
        string? parameterName = null,
        IReadOnlyDictionary<string, string>? details = null) => new()
    {
        Kind = kind,
        Category = FindingCategory.Endpoint,
        Path = path,
        HttpMethod = method,
        Location = location,
        Direction = direction,
        OldValue = oldValue,
        NewValue = newValue,
        ParameterName = parameterName,
        Details = details is null ? FrozenMap.Empty : new FrozenMap(new Dictionary<string, string>(details)),
    };
}

public class RulesEngineTests
{
    private static EngineOptions Options(Func<EngineOptions, EngineOptions>? mutate = null)
    {
        EngineOptions options = new()
        {
            OldContractName = "old.yaml",
            NewContractName = "new.yaml",
            ToolVersion = "test",
        };
        return mutate?.Invoke(options) ?? options;
    }

    private static RulesEngine CreateEngine() => new(RuleRegistry.CreateDefault(), new FakeClock(FakeClock.Fixed));

    [Fact]
    public void EndpointRemoval_ProducesCriticalFinding()
    {
        var engine = CreateEngine();
        var evaluation = engine.Evaluate([Change.Make(ChangeKinds.EndpointRemoved, "/orders", HttpMethodKind.Get)], Options());

        var finding = Assert.Single(evaluation.Result.Findings);
        Assert.Equal("CG1001", finding.RuleId);
        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal(Compatibility.Breaking, finding.Compatibility);
    }

    [Fact]
    public void UnknownKinds_AreCounted_AndOptionallyReported()
    {
        var engine = CreateEngine();
        var evaluation = engine.Evaluate([Change.Make("totally.unknown.kind")], Options());
        Assert.Empty(evaluation.Result.Findings);
        Assert.Equal(1, evaluation.Statistics.UnmatchedChangeCount);

        var reported = engine.Evaluate([Change.Make("totally.unknown.kind")], Options(o => o with { ReportUnknownChangeKinds = true }));
        var finding = Assert.Single(reported.Result.Findings);
        Assert.Equal("CG9003", finding.RuleId);
        Assert.Equal(Severity.Verify, finding.Severity);
    }

    [Fact]
    public void OverrideOff_DisablesRule()
    {
        var engine = CreateEngine();
        var changes = new[] { Change.Make(ChangeKinds.EndpointRemoved), Change.Make(ChangeKinds.EndpointAdded) };
        var evaluation = engine.Evaluate(changes,             Options(o => o with { Overrides = [new RuleOverride { RuleId = "CG1001", Action = "off" }] }));

        Assert.DoesNotContain(evaluation.Result.Findings, f => f.RuleId == "CG1001");
        Assert.Contains(evaluation.Result.Findings, f => f.RuleId == "CG1003");
        Assert.Equal(1, evaluation.Statistics.DisabledByOverrideCount);
    }

    [Fact]
    public void OverrideSeverity_RemapsFinding()
    {
        var engine = CreateEngine();
        var evaluation = engine.Evaluate([Change.Make(ChangeKinds.EndpointAdded)],
            Options(o => o with { Overrides = [new RuleOverride { RuleId = "CG1003", NewSeverity = Severity.Critical }] }));

        Assert.All(evaluation.Result.Findings, f => Assert.Equal(Severity.Critical, f.Severity));
    }

    [Fact]
    public void IgnorePaths_FilterFindingsByGlob()
    {
        var engine = CreateEngine();
        var changes = new[]
        {
            Change.Make(ChangeKinds.EndpointRemoved, "/internal/users", HttpMethodKind.Get),
            Change.Make(ChangeKinds.EndpointRemoved, "/orders"),
        };
        var evaluation = engine.Evaluate(changes, Options(o => o with { IgnorePathGlobs = ["/internal/**"] }));

        var finding = Assert.Single(evaluation.Result.Findings);
        Assert.Equal("/orders", finding.Path);
        Assert.Equal(1, evaluation.Statistics.IgnoredByPathCount);
    }

    [Fact]
    public void Suppression_HidesFindingAndListsItWithReason()
    {
        var engine = CreateEngine();
        var evaluation = engine.Evaluate([Change.Make(ChangeKinds.EndpointRemoved, "/legacy/orders")],
            Options(o => o with
            {
                Suppressions =
                [
                    new SuppressionEntry
                    {
                        RuleId = "CG1001",
                        PathGlob = "/legacy/**",
                        Reason = "Legacy mobile client migration",
                        Expires = new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero),
                    },
                ],
            }));

        Assert.Empty(evaluation.Result.Findings);
        var suppressed = Assert.Single(evaluation.Result.Suppressed);
        Assert.Equal("suppression", suppressed.Source);
        Assert.Equal("Legacy mobile client migration", suppressed.Reason);
    }

    [Fact]
    public void ExpiredSuppression_KeepsFindingAndAddsWarning()
    {
        var engine = CreateEngine(); // clock: 2026-06-15
        var evaluation = engine.Evaluate([Change.Make(ChangeKinds.EndpointRemoved, "/legacy/orders")],
            Options(o => o with
            {
                Suppressions =
                [
                    new SuppressionEntry
                    {
                        RuleId = "CG1001",
                        Reason = "Temporary acknowledgement",
                        Expires = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    },
                ],
            }));

        Assert.Contains(evaluation.Result.Findings, f => f.RuleId == "CG1001");
        var warning = Assert.Single(evaluation.Result.Findings, f => f.RuleId == "CG9001");
        Assert.Contains("expired", warning.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, evaluation.Statistics.ExpiredSuppressionCount);
    }

    [Fact]
    public void Baseline_SuppressesMatchingFingerprint_AndReportsStaleEntries()
    {
        var engine = CreateEngine();
        var probe = engine.Evaluate([Change.Make(ChangeKinds.EndpointRemoved, "/orders")], Options());
        var fingerprint = Assert.Single(probe.Result.Findings).Fingerprint;

        // Same change with the fingerprint baselined: hidden, no stale entries.
        var adopted = engine.Evaluate([Change.Make(ChangeKinds.EndpointRemoved, "/orders")],
            Options(o => o with { BaselineFingerprints = [fingerprint] }));
        Assert.Empty(adopted.Result.Findings);
        Assert.Equal(1, adopted.Statistics.SuppressedByBaselineCount);
        Assert.Equal("baseline", Assert.Single(adopted.Result.Suppressed).Source);
        Assert.Equal(0, adopted.Statistics.StaleBaselineCount);

        // The change is gone but the baseline entry remains: stale-baseline informational finding.
        var stale = engine.Evaluate([], Options(o => o with { BaselineFingerprints = [fingerprint] }));
        var staleFinding = Assert.Single(stale.Result.Findings);
        Assert.Equal("CG9002", staleFinding.RuleId);
        Assert.Equal(1, stale.Statistics.StaleBaselineCount);
    }

    [Fact]
    public void Findings_AreSortedDeterministically()
    {
        var engine = CreateEngine();
        var changes = new[]
        {
            Change.Make(ChangeKinds.OperationAdded, "/b", HttpMethodKind.Post),
            Change.Make(ChangeKinds.EndpointRemoved, "/a", HttpMethodKind.Delete),
            Change.Make(ChangeKinds.EndpointRemoved, "/a", HttpMethodKind.Get),
        };
        var evaluation = engine.Evaluate(changes, Options());

        var order = evaluation.Result.Findings.Select(f => (f.Path, f.HttpMethod)).ToList();
        Assert.Equal([("/a", HttpMethodKind.Get), ("/a", HttpMethodKind.Delete), ("/b", HttpMethodKind.Post)], order);
    }

    [Theory]
    [InlineData(VersionBump.Major, ChangeKinds.EndpointRemoved)]
    [InlineData(VersionBump.Minor, ChangeKinds.EndpointAdded)]
    [InlineData(VersionBump.Minor, ChangeKinds.ResponseContentTypeAdded)]
    public void Recommendation_MapsToExpectedBump(VersionBump expected, string kind)
    {
        var engine = CreateEngine();
        var evaluation = engine.Evaluate([Change.Make(kind, "/x")], Options());
        Assert.Equal(expected, evaluation.Result.Recommendation!.Bump);
    }

    [Fact]
    public void Recommendation_NoneWithoutFindings()
    {
        var engine = CreateEngine();
        var evaluation = engine.Evaluate([], Options());
        Assert.Equal(VersionBump.None, evaluation.Result.Recommendation!.Bump);
    }

    [Fact]
    public void PolicyEvaluation_IsAppliedToResult()
    {
        var engine = CreateEngine();

        var failing = engine.Evaluate([Change.Make(ChangeKinds.EndpointRemoved)], Options());
        Assert.True(failing.Result.PolicyViolated);
        Assert.Equal("failed", failing.Result.Result);

        var passing = engine.Evaluate([], Options());
        Assert.False(passing.Result.PolicyViolated);
        Assert.Equal("passed", passing.Result.Result);

        var relaxed = engine.Evaluate([Change.Make(ChangeKinds.EndpointRemoved)],
            Options(o => o with { Policy = new FailurePolicy { FailOnAnyFinding = false, FailOnBreaking = false } }));
        Assert.False(relaxed.Result.PolicyViolated);
    }

    [Fact]
    public void EndpointSummaries_ReflectTerminalStates()
    {
        var engine = CreateEngine();
        var changes = new[]
        {
            Change.Make(ChangeKinds.EndpointRemoved, "/gone"),
            Change.Make(ChangeKinds.EndpointAdded, "/fresh"),
            Change.Make(ChangeKinds.ParameterTypeChanged, "/changed", HttpMethodKind.Get),
        };
        var evaluation = engine.Evaluate(changes, Options());

        var endpoints = evaluation.Result.Endpoints!;
        Assert.Collection(endpoints,
            e => { Assert.Equal("/changed", e.Path); Assert.Equal(EndpointState.Changed, e.State); },
            e => { Assert.Equal("/fresh", e.Path); Assert.Equal(EndpointState.Added, e.State); },
            e => { Assert.Equal("/gone", e.Path); Assert.Equal(EndpointState.Removed, e.State); });
    }

    [Fact]
    public void Summary_CountsSeveritiesAndCompatibility()
    {
        var engine = CreateEngine();
        var changes = new[]
        {
            Change.Make(ChangeKinds.EndpointRemoved, "/a"),          // CG1001 Critical Breaking
            Change.Make(ChangeKinds.EndpointAdded, "/b"),            // CG1003 Info Informational
        };
        var evaluation = engine.Evaluate(changes, Options());

        var summary = evaluation.Result.Summary;
        Assert.Equal(2, summary.Total);
        Assert.Equal(1, summary.Critical);
        Assert.Equal(1, summary.Info);
        Assert.Equal(1, summary.Breaking);
        Assert.Equal(1, summary.NonBreaking);
    }
}
