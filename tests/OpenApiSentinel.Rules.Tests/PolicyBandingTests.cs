using OpenApiSentinel.Core;

namespace OpenApiSentinel.Rules.Tests;

/// <summary>
/// Pins the documented fail-on threshold banding: severities rank
/// info &lt; low &lt; medium &lt; high &lt; critical, with verify ranked ABOVE critical
/// as its own class — so a verify threshold matches only Verify-class findings,
/// while info matches everything. See docs/configuration.md.
/// </summary>
public class PolicyBandingTests
{
    private static Finding F(Severity severity, string ruleId = "CG9000") => new()
    {
        RuleId = ruleId,
        Title = ruleId,
        Severity = severity,
        Category = FindingCategory.Metadata,
        ChangeType = ChangeType.Changed,
        Compatibility = Compatibility.Unknown,
        Explanation = "test",
        Impact = "test",
        SuggestedAction = "test",
    };

    [Fact]
    public void FailOn_Verify_MatchesOnlyVerifyFindings_NotCritical()
    {
        var policy = new FailurePolicy { FailOnSeverity = Severity.Verify };
        Assert.True(policy.Evaluate([F(Severity.Verify)]).Violated);
        Assert.False(policy.Evaluate([F(Severity.Critical)]).Violated);
        Assert.False(policy.Evaluate([F(Severity.Info)]).Violated);
    }

    [Fact]
    public void FailOn_Info_MatchesEverything_IncludingVerifyAndCritical()
    {
        var policy = new FailurePolicy { FailOnSeverity = Severity.Info };
        Assert.True(policy.Evaluate([F(Severity.Info)]).Violated);
        Assert.True(policy.Evaluate([F(Severity.Verify)]).Violated);
        Assert.True(policy.Evaluate([F(Severity.Critical)]).Violated);
    }

    [Fact]
    public void FailOn_High_MatchesHighCritical_AndVerify()
    {
        var policy = new FailurePolicy { FailOnSeverity = Severity.High };
        Assert.True(policy.Evaluate([F(Severity.High)]).Violated);
        Assert.True(policy.Evaluate([F(Severity.Critical)]).Violated);
        // Verify ranks above critical, so higher thresholds include it too.
        Assert.True(policy.Evaluate([F(Severity.Verify)]).Violated);
        Assert.False(policy.Evaluate([F(Severity.Medium)]).Violated);
        Assert.False(policy.Evaluate([F(Severity.Low)]).Violated);
    }

    [Fact]
    public void FailOn_All_MatchesAnySingleFinding()
    {
        var policy = new FailurePolicy { FailOnAnyFinding = true };
        Assert.True(policy.Evaluate([F(Severity.Info)]).Violated);
        Assert.True(policy.Evaluate([F(Severity.Verify)]).Violated);
        Assert.False(policy.Evaluate([]).Violated);
    }
}
