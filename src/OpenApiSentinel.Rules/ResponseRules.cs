using OpenApiSentinel.Core;
using OpenApiSentinel.OpenApi;

namespace OpenApiSentinel.Rules;

/// <summary>Rules for response-container changes (CG40xx). Schema-level diffs inside responses use CG50xx rules.</summary>
internal static class ResponseRules
{
    private static string Where(SemanticChange c) =>
        c.HttpMethod == HttpMethodKind.None ? c.Path : $"{c.HttpMethod.ToHttpString()} {c.Path}";

    private static string Status(SemanticChange c) =>
        c.Details.TryGetValue("statusCode", out var s) ? s : "";

    public static IEnumerable<RuleDefinition> All()
    {
        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG4001", Title = "Response status code removed",
                Category = FindingCategory.Response,
                DefaultSeverity = Severity.High,
                DefaultCompatibility = Compatibility.Breaking,
                ChangeKinds = [ChangeKinds.ResponseStatusCodeRemoved],
                Description = "A documented response status code disappeared and is not covered by a remaining wildcard (default/2XX).",
                Impact = "Clients relying on this documented status will encounter undocumented responses and may fail handling them.",
                Remediation = "Keep returning the status code or document it under a wildcard; coordinate removal with consumers.",
                BreakingExample = "responses:\n  '404': {...}   # removed, no default/4XX documented",
                NonBreakingExample = "responses:\n  default: {...}   # still documents arbitrary errors",
            },
            Build = c => FindingFactory.Create("CG4001", "Response status code removed",
                Severity.High, Compatibility.Breaking, c,
                $"Status code {Status(c)} was removed from {Where(c)}.",
                "Consumers may receive undocumented status codes and fail to handle them.",
                "Restore the response or cover it via 'default'/range responses; inform consumers."),
        };

        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG4002", Title = "Response status code added",
                Category = FindingCategory.Response,
                DefaultSeverity = Severity.Low,
                DefaultCompatibility = Compatibility.NonBreaking,
                ChangeKinds = [ChangeKinds.ResponseStatusCodeAdded],
                Description = "A new response status code was documented.",
                Impact = "Documentation-only for existing behaviour; additive.",
                Remediation = "No action required.",
            },
            Build = c => FindingFactory.Create("CG4002", "Response status code added",
                Severity.Low, Compatibility.NonBreaking, c,
                $"New documented status code {Status(c)} on {Where(c)}.",
                "Additive documentation; existing consumers unaffected.",
                "No action required."),
        };

        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG4003", Title = "Response content type removed",
                Category = FindingCategory.Response,
                DefaultSeverity = Severity.Medium,
                DefaultCompatibility = Compatibility.PotentiallyBreaking,
                ChangeKinds = [ChangeKinds.ResponseContentTypeRemoved],
                Description = "A media type documented in the response was removed.",
                Impact = "Clients requesting this content type via Accept may receive 406 or fall back unexpectedly.",
                Remediation = "Keep producing the media type during migration; coordinate removal with consumers.",
                BreakingExample = "'200':\n  content:\n    application/xml: {...}   # removed",
            },
            Build = c => FindingFactory.Create("CG4003", "Response content type removed",
                Severity.Medium, Compatibility.PotentiallyBreaking, c,
                $"Response content type '{c.OldValue}' removed from {Where(c)} {(Status(c).Length > 0 ? $"({Status(c)})" : "")}.",
                "Clients preferring this media type may receive 406 responses or an unexpected format.",
                "Continue producing the media type during a deprecation window."),
        };

        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG4004", Title = "Response content type added",
                Category = FindingCategory.Response,
                DefaultSeverity = Severity.Low,
                DefaultCompatibility = Compatibility.NonBreaking,
                ChangeKinds = [ChangeKinds.ResponseContentTypeAdded],
                Description = "A new media type was added to the documented response.",
                Impact = "Additive capability.",
                Remediation = "No action required.",
            },
            Build = c => FindingFactory.Create("CG4004", "Response content type added",
                Severity.Low, Compatibility.NonBreaking, c,
                $"New response content type '{c.NewValue}' on {Where(c)}.",
                "Additive change.",
                "No action required."),
        };

        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG4005", Title = "Response header removed",
                Category = FindingCategory.Response,
                DefaultSeverity = Severity.Medium,
                DefaultCompatibility = Compatibility.PotentiallyBreaking,
                ChangeKinds = [ChangeKinds.ResponseHeaderRemoved],
                Description = "A documented response header disappeared.",
                Impact = "Clients reading this header (pagination, tracing, rate limits) lose the information.",
                Remediation = "Keep sending the header during a deprecation window.",
                BreakingExample = "headers:\n  X-Total-Count: {...}   # removed",
            },
            Build = c =>
            {
                c.Details.TryGetValue("required", out var required);
                var wasRequired = required == "true";
                return FindingFactory.Create("CG4005", "Response header removed",
                    wasRequired ? Severity.High : Severity.Medium,
                    wasRequired ? Compatibility.Breaking : Compatibility.PotentiallyBreaking, c,
                    $"Response header '{c.OldValue}' was removed from {Where(c)}.",
                    wasRequired
                        ? "Clients depending on this required header will fail when it disappears."
                        : "Clients reading this header may misbehave when it is absent.",
                    "Keep sending the header during migration or document its removal.");
            },
        };

        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG4006", Title = "Response header added",
                Category = FindingCategory.Response,
                DefaultSeverity = Severity.Low,
                DefaultCompatibility = Compatibility.NonBreaking,
                ChangeKinds = [ChangeKinds.ResponseHeaderAdded],
                Description = "A new response header was documented.",
                Impact = "Additive; clients ignoring unknown headers are unaffected.",
                Remediation = "No action required.",
            },
            Build = c => FindingFactory.Create("CG4006", "Response header added",
                Severity.Low, Compatibility.NonBreaking, c,
                $"New response header '{c.NewValue}' on {Where(c)}.",
                "Additive change.",
                "No action required."),
        };

        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG4007", Title = "Response header required-ness changed",
                Category = FindingCategory.Response,
                DefaultSeverity = Severity.Medium,
                DefaultCompatibility = Compatibility.PotentiallyBreaking,
                ChangeKinds = [ChangeKinds.ResponseHeaderRequiredChanged],
                Description = "The required flag of a response header changed.",
                Impact = "required → optional means the server may omit a header clients rely on; optional → required is a stronger server commitment.",
                Remediation = "Avoid downgrading a required header to optional while clients depend on its presence.",
            },
            Build = c => c.OldValue == "required"
                ? FindingFactory.Create("CG4007", "Response header became optional",
                    Severity.Medium, Compatibility.PotentiallyBreaking, c,
                    $"Header required-ness changed: {RuleText.Values(c)}.",
                    "The server may now omit a header that clients treat as always present.",
                    "Keep sending the header unconditionally or coordinate with consumers.")
                : FindingFactory.Create("CG4007", "Response header became required",
                    Severity.Low, Compatibility.NonBreaking, c,
                    $"Header required-ness changed: {RuleText.Values(c)}.",
                    "Stronger commitment: clients must be prepared for the header to always appear.",
                    "No action required."),
        };
    }
}
