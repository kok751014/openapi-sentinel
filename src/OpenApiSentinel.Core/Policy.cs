namespace OpenApiSentinel.Core;

/// <summary>Abstraction over the system clock so reports and tests are deterministic.</summary>
public interface ISentinelClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : ISentinelClock
{
    public static readonly SystemClock Instance = new();
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>Parses and compares severities used by --fail-on and configuration files.</summary>
public static class SeverityNames
{
    private static readonly Dictionary<string, Severity> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["info"] = Severity.Info,
        ["information"] = Severity.Info,
        ["low"] = Severity.Low,
        ["medium"] = Severity.Medium,
        ["high"] = Severity.High,
        ["critical"] = Severity.Critical,
        ["verify"] = Severity.Verify,
        ["error"] = Severity.High, // config-friendly alias: rules set to "error" behave as high
        ["warning"] = Severity.Medium, // alias: "warning" behaves as medium
        ["off"] = Severity.Info, // handled before this map for rule overrides
        ["none"] = Severity.Info,
        ["all"] = Severity.Info, // fail-on: any finding at all fails; special-cased by policy evaluator
    };

    /// <summary>Parses a severity name. Returns false for unknown values.</summary>
    public static bool TryParse(string value, out Severity severity)
    {
        severity = Severity.Info;
        return Map.TryGetValue(value.Trim(), out severity);
    }

    public static string ToStringValue(Severity severity) => severity switch
    {
        Severity.Critical => "critical",
        Severity.High => "high",
        Severity.Medium => "medium",
        Severity.Low => "low",
        Severity.Verify => "verify",
        _ => "info",
    };

    public static string ToDisplayName(Severity severity) => severity switch
    {
        Severity.Critical => "CRITICAL",
        Severity.High => "HIGH",
        Severity.Medium => "MEDIUM",
        Severity.Low => "LOW",
        Severity.Verify => "VERIFY",
        _ => "INFO",
    };
}

/// <summary>
/// Policy evaluation: decides whether a comparison violates the configured failure threshold.
/// </summary>
public sealed record FailurePolicy
{
    /// <summary>Minimum severity that causes failure. Null means "fail on any breaking change" default.</summary>
    public Severity? FailOnSeverity { get; init; }

    /// <summary>When true (fail-on=all), any finding including informational ones fails.</summary>
    public bool FailOnAnyFinding { get; init; }

    /// <summary>Default mode: fail when any finding classified Breaking or PotentiallyBreaking exists.</summary>
    public bool FailOnBreaking { get; init; } = true;

    public static FailurePolicy Default() => new();

    public PolicyDecision Evaluate(IReadOnlyList<Finding> findings)
    {
        if (FailOnAnyFinding && findings.Count > 0)
        {
            return new PolicyDecision(true, $"Found {findings.Count} finding(s); policy is configured to fail on any finding.");
        }

        if (FailOnSeverity is { } threshold)
        {
            var violating = findings.Where(f => f.Severity >= threshold).ToList();
            if (violating.Count > 0)
            {
                var worst = violating.Max(f => f.Severity);
                return new PolicyDecision(true,
                    $"Found {violating.Count} finding(s) at or above '{SeverityNames.ToStringValue(threshold)}' severity (worst: '{SeverityNames.ToStringValue(worst)}').");
            }
            return new PolicyDecision(false, "");
        }

        if (FailOnBreaking)
        {
            var violating = findings.Where(f =>
                    f.Compatibility is Compatibility.Breaking or Compatibility.PotentiallyBreaking &&
                    f.Severity >= Severity.Medium)
                .ToList();
            if (violating.Count > 0)
            {
                return new PolicyDecision(true,
                    $"Found {violating.Count} breaking or potentially-breaking finding(s) (severity medium or higher).");
            }
        }

        return new PolicyDecision(false, "");
    }
}

public sealed record PolicyDecision(bool Violated, string Reason);

/// <summary>Per-rule override applied from configuration.</summary>
public sealed record RuleOverride
{
    public required string RuleId { get; init; }

    /// <summary>"off" disables the rule; otherwise a replacement severity.</summary>
    public string? Action { get; init; }

    public Severity? NewSeverity { get; init; }
}
