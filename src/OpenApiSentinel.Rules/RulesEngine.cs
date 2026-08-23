using OpenApiSentinel.Core;
using OpenApiSentinel.OpenApi;

namespace OpenApiSentinel.Rules;

/// <summary>
/// Maps a semantic change stream onto registered rules, applies configuration (overrides,
/// ignore paths, suppressions, baseline), and produces the deterministic ComparisonResult.
/// The engine is rule-agnostic: it only needs a RuleRegistry.
/// </summary>
public sealed class RulesEngine
{
    private const string ExpiredSuppressionRuleId = "CG9001";
    private const string StaleBaselineRuleId = "CG9002";
    private const string UnknownChangeKindRuleId = "CG9003";

    /// <summary>Additive change kinds that justify a MINOR version recommendation.</summary>
    private static readonly HashSet<string> AdditiveMinorKinds = new(StringComparer.Ordinal)
    {
        ChangeKinds.EndpointAdded,
        ChangeKinds.OperationAdded,
        ChangeKinds.RequestContentTypeAdded,
        ChangeKinds.ResponseContentTypeAdded,
        ChangeKinds.ResponseStatusCodeAdded,
    };

    private static readonly Dictionary<HttpMethodKind, int> MethodOrder = new()
    {
        [HttpMethodKind.None] = 0,
        [HttpMethodKind.Get] = 1,
        [HttpMethodKind.Put] = 2,
        [HttpMethodKind.Post] = 3,
        [HttpMethodKind.Delete] = 4,
        [HttpMethodKind.Options] = 5,
        [HttpMethodKind.Head] = 6,
        [HttpMethodKind.Patch] = 7,
        [HttpMethodKind.Trace] = 8,
    };

    private readonly RuleRegistry _registry;
    private readonly ISentinelClock _clock;
    private readonly Dictionary<string, List<RuleDefinition>> _rulesByKind;

    public RulesEngine(RuleRegistry registry, ISentinelClock? clock = null)
    {
        _registry = registry;
        _clock = clock ?? SystemClock.Instance;
        _rulesByKind = new Dictionary<string, List<RuleDefinition>>(StringComparer.Ordinal);
        foreach (var rule in registry.All.OrderBy(r => r.Descriptor.Id, StringComparer.Ordinal))
        {
            foreach (var kind in rule.Descriptor.ChangeKinds)
            {
                if (!_rulesByKind.TryGetValue(kind, out var list))
                    _rulesByKind[kind] = [rule];
                else
                    list.Add(rule);
            }
        }
    }

    public RuleRegistry Registry => _registry;

    public EngineEvaluation Evaluate(IEnumerable<SemanticChange> changes, EngineOptions options)
    {
        var overrideMap = options.Overrides.ToDictionary(o => o.RuleId, o => o, StringComparer.OrdinalIgnoreCase);
        var ignoreMatchers = options.IgnorePathGlobs.Select(g => new GlobMatcher(g)).ToList();
        var suppressionMatchers = options.Suppressions
            .Select(s => (Entry: s, PathMatcher: string.IsNullOrEmpty(s.PathGlob) ? null : new GlobMatcher(s.PathGlob!)))
            .ToList();
        var baseline = new HashSet<string>(options.BaselineFingerprints, StringComparer.Ordinal);
        var usedBaseline = new HashSet<string>(StringComparer.Ordinal);

        var findings = new List<Finding>();
        var suppressed = new List<SuppressedFinding>();
        var unknownKinds = new SortedSet<string>(StringComparer.Ordinal);
        var now = _clock.UtcNow;

        int disabledByOverride = 0, ignoredByPath = 0, suppressedByConfig = 0, suppressedByBaseline = 0;
        int unmatched = 0, expiredCount = 0;
        var changeList = changes as IReadOnlyList<SemanticChange> ?? changes.ToList();

        foreach (var change in changeList)
        {
            if (!_rulesByKind.TryGetValue(change.Kind, out var candidates))
            {
                unmatched++;
                unknownKinds.Add(change.Kind);
                continue;
            }

            // First candidate (by rule-id order) whose optional guard accepts the change wins,
            // letting families split a shared kind across directional rules.
            RuleDefinition? rule = null;
            foreach (var candidate in candidates)
            {
                if (candidate.AppliesTo?.Invoke(change) ?? true)
                {
                    rule = candidate;
                    break;
                }
            }

            if (rule is null)
            {
                unmatched++;
                unknownKinds.Add(change.Kind);
                continue;
            }

            var finding = rule.Build(change);

            if (overrideMap.TryGetValue(finding.RuleId, out var ov))
            {
                if (string.Equals(ov.Action, "off", StringComparison.OrdinalIgnoreCase))
                {
                    disabledByOverride++;
                    continue;
                }

                if (ov.NewSeverity is { } remapped)
                    finding = finding with { Severity = remapped };
            }

            if (finding.Path.Length > 0 && ignoreMatchers.Count > 0 && GlobMatcher.MatchesAny(ignoreMatchers, finding.Path))
            {
                ignoredByPath++;
                continue;
            }

            // Suppressions: an expired matching suppression keeps the finding and raises a warning.
            SuppressionEntry? expiredMatch = null;
            SuppressedFinding? matched = null;
            foreach (var (entry, pathMatcher) in suppressionMatchers)
            {
                if (!entry.MatchesRule(finding.RuleId)) continue;
                if (pathMatcher is not null && !pathMatcher.IsMatch(finding.Path)) continue;

                if (entry.Expires is { } expires && expires <= now)
                {
                    expiredMatch = entry;
                    continue;
                }

                matched = new SuppressedFinding
                {
                    Finding = finding,
                    Source = "suppression",
                    Reason = entry.Reason,
                };
                break;
            }

            if (matched is not null)
            {
                suppressedByConfig++;
                suppressed.Add(matched);
                continue;
            }

            if (baseline.Contains(finding.Fingerprint))
            {
                usedBaseline.Add(finding.Fingerprint);
                suppressedByBaseline++;
                suppressed.Add(new SuppressedFinding
                {
                    Finding = finding,
                    Source = "baseline",
                    Reason = "Present in the adopted baseline; fix or remove the baseline entry to resurface it.",
                });
                continue;
            }

            findings.Add(finding);

            if (expiredMatch is not null)
            {
                expiredCount++;
                if (!IsMetaRuleOff(overrideMap, ExpiredSuppressionRuleId))
                    findings.Add(CreateExpiredSuppressionWarning(expiredMatch, finding));
            }
        }

        if (options.ReportUnknownChangeKinds && unknownKinds.Count > 0 && !IsMetaRuleOff(overrideMap, UnknownChangeKindRuleId))
            findings.Add(CreateUnknownKindFinding(unknownKinds));

        var staleBaseline = baseline.Except(usedBaseline, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (staleBaseline.Count > 0 && !IsMetaRuleOff(overrideMap, StaleBaselineRuleId))
            findings.Add(CreateStaleBaselineFinding(staleBaseline));

        findings.Sort(FindingsComparer);
        suppressed.Sort((a, b) => FindingsComparer(a.Finding, b.Finding));

        var summary = BuildSummary(findings);
        var recommendation = RecommendVersion(findings, changeList);
        var decision = options.Policy.Evaluate(findings);

        var result = new ComparisonResult
        {
            SchemaVersion = ComparisonResult.SchemaVersionValue,
            ToolVersion = options.ToolVersion,
            OldContractName = options.OldContractName,
            NewContractName = options.NewContractName,
            OldOpenApiVersion = options.OldOpenApiVersion,
            NewOpenApiVersion = options.NewOpenApiVersion,
            OldInfoVersion = options.OldInfoVersion,
            NewInfoVersion = options.NewInfoVersion,
            GeneratedAtUtc = _clock.UtcNow,
            Findings = findings,
            Suppressed = suppressed,
            Summary = summary,
            Recommendation = recommendation,
            PolicyViolated = decision.Violated,
            Endpoints = BuildEndpointSummaries(findings, changeList),
        };

        var statistics = new EngineStatistics
        {
            ChangeCount = changeList.Count,
            FindingsEmitted = findings.Count,
            UnmatchedChangeCount = unmatched,
            UnknownChangeKinds = unknownKinds.ToList(),
            DisabledByOverrideCount = disabledByOverride,
            IgnoredByPathCount = ignoredByPath,
            SuppressedByConfigCount = suppressedByConfig,
            SuppressedByBaselineCount = suppressedByBaseline,
            ExpiredSuppressionCount = expiredCount,
            StaleBaselineCount = staleBaseline.Count,
        };

        return new EngineEvaluation { Result = result, Statistics = statistics };
    }

    private static bool IsMetaRuleOff(Dictionary<string, RuleOverride> overrides, string ruleId) =>
        overrides.TryGetValue(ruleId, out var ov) &&
        string.Equals(ov.Action, "off", StringComparison.OrdinalIgnoreCase);

    private static int FindingsComparer(Finding x, Finding y)
    {
        var byPath = string.CompareOrdinal(x.Path, y.Path);
        if (byPath != 0) return byPath;
        var byMethod = MethodOrder.GetValueOrDefault(x.HttpMethod).CompareTo(MethodOrder.GetValueOrDefault(y.HttpMethod));
        if (byMethod != 0) return byMethod;
        var byLocation = string.CompareOrdinal(x.Location, y.Location);
        if (byLocation != 0) return byLocation;
        var byRule = string.CompareOrdinal(x.RuleId, y.RuleId);
        if (byRule != 0) return byRule;
        var byOld = string.CompareOrdinal(x.OldValue, y.OldValue);
        if (byOld != 0) return byOld;
        return string.CompareOrdinal(x.NewValue, y.NewValue);
    }

    private static ResultSummary BuildSummary(IReadOnlyList<Finding> findings)
    {
        ResultSummary summary = new();
        foreach (var f in findings)
        {
            summary = summary with { Total = summary.Total + 1 };
            switch (f.Compatibility)
            {
                case Compatibility.Breaking: summary = summary with { Breaking = summary.Breaking + 1 }; break;
                case Compatibility.PotentiallyBreaking: summary = summary with { PotentiallyBreaking = summary.PotentiallyBreaking + 1 }; break;
                case Compatibility.NonBreaking: summary = summary with { NonBreaking = summary.NonBreaking + 1 }; break;
                case Compatibility.Informational: summary = summary with { Informational = summary.Informational + 1 }; break;
                default: summary = summary with { Unknown = summary.Unknown + 1 }; break;
            }

            switch (f.Severity)
            {
                case Severity.Critical: summary = summary with { Critical = summary.Critical + 1 }; break;
                case Severity.High: summary = summary with { High = summary.High + 1 }; break;
                case Severity.Medium: summary = summary with { Medium = summary.Medium + 1 }; break;
                case Severity.Low: summary = summary with { Low = summary.Low + 1 }; break;
                case Severity.Verify: summary = summary with { Verify = summary.Verify + 1 }; break;
                default: summary = summary with { Info = summary.Info + 1 }; break;
            }
        }
        return summary;
    }

    private static VersionRecommendation? RecommendVersion(List<Finding> findings, IReadOnlyList<SemanticChange> changes)
    {
        var breakingHigh = findings.FirstOrDefault(f =>
            f.Compatibility == Compatibility.Breaking && f.Severity >= Severity.High);
        if (breakingHigh is not null)
        {
            var count = findings.Count(f => f.Compatibility == Compatibility.Breaking && f.Severity >= Severity.High);
            return new VersionRecommendation
            {
                Bump = VersionBump.Major,
                Reason = $"{count} breaking finding(s) at high or critical severity (first: {breakingHigh.RuleId}) require a major version.",
            };
        }

        if (changes.Any(c => AdditiveMinorKinds.Contains(c.Kind)))
        {
            return new VersionRecommendation
            {
                Bump = VersionBump.Minor,
                Reason = "Additive changes only (new endpoints, operations, status codes or content types): minor version.",
            };
        }

        if (findings.Count > 0 && findings.All(f => f.Severity <= Severity.Low))
        {
            return new VersionRecommendation
            {
                Bump = VersionBump.Patch,
                Reason = "Only low-severity or informational findings: patch version.",
            };
        }

        return new VersionRecommendation
        {
            Bump = VersionBump.None,
            Reason = findings.Count == 0
                ? "No findings."
                : "Findings exist that do not map cleanly to a semver bump; review them manually.",
        };
    }

    private static List<EndpointSummary> BuildEndpointSummaries(IReadOnlyList<Finding> findings, IReadOnlyList<SemanticChange> changes)
    {
        var states = new Dictionary<(string Path, HttpMethodKind Method), EndpointState>();
        foreach (var change in changes)
        {
            if (change.Path.Length == 0) continue;
            var key = (change.Path, change.HttpMethod);
            var state = change.Kind switch
            {
                ChangeKinds.EndpointRemoved or ChangeKinds.OperationRemoved => EndpointState.Removed,
                ChangeKinds.EndpointAdded or ChangeKinds.OperationAdded => EndpointState.Added,
                _ => EndpointState.Changed,
            };

            if (!states.TryGetValue(key, out var existing))
            {
                states[key] = state;
                continue;
            }

            // Removed wins over Changed/Added so consumers see the terminal state first.
            if (existing != EndpointState.Removed && state == EndpointState.Removed)
                states[key] = EndpointState.Removed;
        }

        var counts = new Dictionary<(string Path, HttpMethodKind Method), int>();
        foreach (var finding in findings)
        {
            if (finding.Path.Length == 0) continue;
            var key = (finding.Path, finding.HttpMethod);
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        return states.Keys
            .OrderBy(k => k.Path, StringComparer.Ordinal)
            .ThenBy(k => MethodOrder.GetValueOrDefault(k.Method))
            .Select(k => new EndpointSummary
            {
                Path = k.Path,
                HttpMethod = k.Method,
                State = states[k],
                FindingCount = counts.GetValueOrDefault(k),
            })
            .ToList();
    }

    private static Finding CreateExpiredSuppressionWarning(SuppressionEntry entry, Finding forFinding) => new()
    {
        RuleId = ExpiredSuppressionRuleId,
        Title = "Expired suppression",
        Severity = Severity.Low,
        Category = FindingCategory.Metadata,
        ChangeType = ChangeType.Changed,
        Compatibility = Compatibility.Informational,
        Confidence = Confidence.Certain,
        Direction = ApiDirection.Neutral,
        Path = forFinding.Path,
        HttpMethod = forFinding.HttpMethod,
        Location = forFinding.Location,
        OldValue = entry.Expires?.ToString("yyyy-MM-dd"),
        NewValue = forFinding.RuleId,
        Explanation = $"The suppression covering {forFinding.RuleId} at '{forFinding.Path}' (reason: {entry.Reason}) expired on {entry.Expires:yyyy-MM-dd}; the finding is reported again.",
        Impact = "Previously acknowledged findings reappear and may fail CI once the suppression has lapsed.",
        SuggestedAction = "Fix the underlying issue, or renew the suppression with a new reason and expiry date.",
    };

    private static Finding CreateStaleBaselineFinding(List<string> staleFingerprints)
    {
        const int maxShown = 5;
        var shown = string.Join(", ", staleFingerprints.Take(maxShown));
        var more = staleFingerprints.Count > maxShown ? $" (+{staleFingerprints.Count - maxShown} more)" : "";
        return new Finding
        {
            RuleId = StaleBaselineRuleId,
            Title = "Stale baseline entries",
            Severity = Severity.Info,
            Category = FindingCategory.Metadata,
            ChangeType = ChangeType.Changed,
            Compatibility = Compatibility.Informational,
            Explanation = $"{staleFingerprints.Count} baseline entr(y|ies) no longer match any current finding: {shown}{more}.",
            Impact = "The baseline contains acknowledgements for issues that are gone; keeping them hides future regressions of the same shape.",
            SuggestedAction = "Refresh the baseline file (remove stale fingerprints) to keep it meaningful.",
        };
    }

    private static Finding CreateUnknownKindFinding(SortedSet<string> kinds)
    {
        var list = string.Join(", ", kinds);
        return new Finding
        {
            RuleId = UnknownChangeKindRuleId,
            Title = "Changes without an assigned rule",
            Severity = Severity.Verify,
            Category = FindingCategory.Metadata,
            ChangeType = ChangeType.Changed,
            Compatibility = Compatibility.Unknown,
            Confidence = Confidence.Uncertain,
            Explanation = $"No registered rule evaluates the following change kinds: {list}.",
            Impact = "These structural changes were detected but not classified; their compatibility impact is unknown.",
            SuggestedAction = "Review the changes manually or extend the tool with custom rules for these kinds.",
        };
    }
}
