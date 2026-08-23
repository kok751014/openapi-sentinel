namespace OpenApiSentinel.Core;

/// <summary>Aggregated counts for a comparison run.</summary>
public sealed record ResultSummary
{
    public int Total { get; init; }
    public int Breaking { get; init; }
    public int PotentiallyBreaking { get; init; }
    public int NonBreaking { get; init; }
    public int Informational { get; init; }
    public int Unknown { get; init; }
    public int Critical { get; init; }
    public int High { get; init; }
    public int Medium { get; init; }
    public int Low { get; init; }
    public int Info { get; init; }
    public int Verify { get; init; }
}

/// <summary>Recommended semantic version bump derived from the findings.</summary>
public enum VersionBump
{
    None,
    Patch,
    Minor,
    Major,
}

public sealed record VersionRecommendation
{
    public required VersionBump Bump { get; init; }
    public required string Reason { get; init; }
    public string Display => Bump.ToString().ToUpperInvariant();
}

/// <summary>A finding that was suppressed (by config suppression or baseline) and therefore not counted.</summary>
public sealed record SuppressedFinding
{
    public required Finding Finding { get; init; }

    /// <summary>"suppression" or "baseline".</summary>
    public required string Source { get; init; }

    public required string Reason { get; init; }
}

/// <summary>
/// The structured result of a single comparison. Reporters consume only this type.
/// </summary>
public sealed record ComparisonResult
{
    /// <summary>Version of the machine-readable output contract ("1.0").</summary>
    public const string SchemaVersionValue = "1.0";

    public required string SchemaVersion { get; init; }

    public required string ToolVersion { get; init; }

    /// <summary>"passed" or "failed" from the policy perspective (before policy evaluation this reflects breaking presence).</summary>
    public string Result => PolicyViolated ? "failed" : "passed";

    public required string OldContractName { get; init; }

    public required string NewContractName { get; init; }

    public string? OldOpenApiVersion { get; init; }

    public string? NewOpenApiVersion { get; init; }

    /// <summary>The declared info.version of each contract, when known.</summary>
    public string? OldInfoVersion { get; init; }

    public string? NewInfoVersion { get; init; }

    public required DateTimeOffset GeneratedAtUtc { get; init; }

    public required IReadOnlyList<Finding> Findings { get; init; }

    public required IReadOnlyList<SuppressedFinding> Suppressed { get; init; }

    public required ResultSummary Summary { get; init; }

    public VersionRecommendation? Recommendation { get; init; }

    /// <summary>Set after policy evaluation: whether the configured failure threshold was violated.</summary>
    public bool PolicyViolated { get; init; }

    /// <summary>List of endpoints present in old and/or new contract with change flags, for report summaries.</summary>
    public IReadOnlyList<EndpointSummary>? Endpoints { get; init; }

    /// <summary>Markdown changelog body when requested; null otherwise.</summary>
    public string? ChangelogMarkdown { get; init; }

    /// <summary>How the engine processed the incoming change stream (additive, v1.0).</summary>
    public ProcessingSummary? Processing { get; init; }
}

/// <summary>Observability counters for the rules-engine stage.</summary>
public sealed record ProcessingSummary
{
    public int Changes { get; init; }
    public int FindingsEmitted { get; init; }
    public int UnmatchedChanges { get; init; }
    public int DisabledByOverride { get; init; }
    public int IgnoredByPath { get; init; }
    public int SuppressedByConfig { get; init; }
    public int SuppressedByBaseline { get; init; }
}

public sealed record EndpointSummary
{
    public required string Path { get; init; }
    public required HttpMethodKind HttpMethod { get; init; }
    public required EndpointState State { get; init; }
    public int FindingCount { get; init; }
}

public enum EndpointState
{
    Unchanged,
    Changed,
    Added,
    Removed,
}
