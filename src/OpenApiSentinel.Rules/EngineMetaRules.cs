using OpenApiSentinel.Core;
using OpenApiSentinel.OpenApi;

namespace OpenApiSentinel.Rules;

/// <summary>
/// Engine meta-findings (CG9xxx). These are not produced from semantic change kinds —
/// they surface process conditions (expired suppressions, stale baselines, unevaluated
/// change kinds) — but they live in the registry so `rules` / `explain` can document them
/// like any other rule.
/// </summary>
internal static class EngineMetaRules
{
    public static IEnumerable<RuleDefinition> All()
    {
        yield return Meta(
            "CG9001", "Expired suppression",
            Severity.Low, Compatibility.Informational,
            "A configured suppression matched this finding, but its expiry date has passed, so the finding is reported again.",
            "Previously acknowledged findings reappear and may fail CI once their suppression lapses.",
            "Fix the underlying issue, or renew the suppression with a new reason and expiry date.",
            "Emitted alongside the original finding it un-suppresses.");

        yield return Meta(
            "CG9002", "Stale baseline entries",
            Severity.Info, Compatibility.Informational,
            "One or more baseline fingerprints no longer match any current finding.",
            "The baseline contains acknowledgements for issues that are gone; stale entries hide future regressions of the same shape.",
            "Refresh the baseline file to remove stale fingerprints.",
            "Aggregated into a single informational finding per run.");

        yield return Meta(
            "CG9003", "Changes without an assigned rule",
            Severity.Verify, Compatibility.Unknown,
            "Structural changes were detected whose change kinds no registered rule evaluates.",
            "These changes were found but not classified; their compatibility impact is unknown.",
            "Review the changes manually or extend the tool with custom rules for these kinds.",
            "Only emitted when the engine option ReportUnknownChangeKinds is enabled.");
    }

    private static RuleDefinition Meta(
        string id, string title, Severity severity, Compatibility compatibility,
        string description, string impact, string remediation, string limitations)
    {
        // Empty change-kind list: the engine's kind index never routes to meta rules;
        // they exist so reports and `explain` can describe engine-emitted findings.
        return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = id,
                Title = title,
                Category = FindingCategory.Metadata,
                DefaultSeverity = severity,
                DefaultCompatibility = compatibility,
                ChangeKinds = [],
                Description = description,
                Impact = impact,
                Remediation = remediation,
                Limitations = limitations,
            },
            Build = c => FindingFactory.Create(id, title, severity, compatibility, c,
                description, impact, remediation),
        };
    }
}
