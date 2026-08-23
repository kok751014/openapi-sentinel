using OpenApiSentinel.Core;

namespace OpenApiSentinel.Rules;

/// <summary>
/// A configured suppression: hides matching findings while active. A reason is mandatory so
/// reports can explain why a finding was acknowledged instead of fixed.
/// </summary>
public sealed record SuppressionEntry
{
    /// <summary>Rule id the suppression applies to, or empty/"*" for every rule.</summary>
    public string RuleId { get; init; } = "*";

    /// <summary>Glob pattern matched against the finding path; null/empty matches every path.</summary>
    public string? PathGlob { get; init; }

    public required string Reason { get; init; }

    /// <summary>UTC expiry; after this instant matching findings are reported again plus a warning.</summary>
    public DateTimeOffset? Expires { get; init; }

    public bool MatchesRule(string ruleId) =>
        string.IsNullOrEmpty(RuleId) || RuleId == "*" || string.Equals(RuleId, ruleId, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Everything the rules engine needs besides the changes themselves.</summary>
public sealed record EngineOptions
{
    public required string OldContractName { get; init; }

    public required string NewContractName { get; init; }

    public string? OldOpenApiVersion { get; init; }

    public string? NewOpenApiVersion { get; init; }

    public string? OldInfoVersion { get; init; }

    public string? NewInfoVersion { get; init; }

    public string ToolVersion { get; init; } = "0.0.0-dev";

    /// <summary>Per-rule severity overrides. Action "off" disables a rule entirely.</summary>
    public IReadOnlyList<RuleOverride> Overrides { get; init; } = [];

    /// <summary>Glob patterns; findings whose path matches any pattern are excluded from the result.</summary>
    public IReadOnlyList<string> IgnorePathGlobs { get; init; } = [];

    public IReadOnlyList<SuppressionEntry> Suppressions { get; init; } = [];

    /// <summary>Fingerprints from an adopted baseline; matching findings are acknowledged, not reported.</summary>
    public IReadOnlyCollection<string> BaselineFingerprints { get; init; } = [];

    public FailurePolicy Policy { get; init; } = FailurePolicy.Default();

    /// <summary>When true, change kinds no registered rule handles produce an aggregated VERIFY finding.</summary>
    public bool ReportUnknownChangeKinds { get; init; }
}

/// <summary>Counters describing what the engine did with the incoming change stream.</summary>
public sealed record EngineStatistics
{
    public int ChangeCount { get; init; }
    public int FindingsEmitted { get; init; }
    public int UnmatchedChangeCount { get; init; }
    public IReadOnlyList<string> UnknownChangeKinds { get; init; } = [];
    public int DisabledByOverrideCount { get; init; }
    public int IgnoredByPathCount { get; init; }
    public int SuppressedByConfigCount { get; init; }
    public int SuppressedByBaselineCount { get; init; }
    public int ExpiredSuppressionCount { get; init; }
    public int StaleBaselineCount { get; init; }
}

/// <summary>Engine output: the reportable result plus observability counters.</summary>
public sealed record EngineEvaluation
{
    public required ComparisonResult Result { get; init; }

    public required EngineStatistics Statistics { get; init; }
}
