using OpenApiSentinel.Core;
using OpenApiSentinel.OpenApi;

namespace OpenApiSentinel.Rules;

/// <summary>Rules for request-body container changes (CG30xx). Property-level diffs use CG50xx schema rules.</summary>
internal static class RequestBodyRules
{
    private static string Where(SemanticChange c) =>
        c.HttpMethod == HttpMethodKind.None ? c.Path : $"{c.HttpMethod.ToHttpString()} {c.Path}";

    public static IEnumerable<RuleDefinition> All()
    {
        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG3001", Title = "Request body removed",
                Category = FindingCategory.RequestBody,
                DefaultSeverity = Severity.Critical,
                DefaultCompatibility = Compatibility.Breaking,
                ChangeKinds = [ChangeKinds.RequestBodyRemoved],
                Description = "The operation no longer accepts a request body.",
                Impact = "Clients sending a body will receive 415/400 responses.",
                Remediation = "Keep accepting (and ignoring) the body during a deprecation window.",
                BreakingExample = "post:\n  requestBody: {...}   # removed in v2",
            },
            Build = c =>
            {
                var wasRequired = c.Details.TryGetValue("required", out var r) && r == "true";
                return FindingFactory.Create("CG3001", "Request body removed",
                    wasRequired ? Severity.High : Severity.Low,
                    wasRequired ? Compatibility.Breaking : Compatibility.NonBreaking, c,
                    $"The request body of {Where(c)} was removed.",
                    wasRequired
                        ? "Clients that still send payloads will be rejected by strict servers."
                        : "Relaxation: callers without a body keep working; verify servers ignore stray payloads.",
                    wasRequired
                        ? "Keep tolerating (and ignoring) the body during a deprecation window."
                        : "No action required.",
                    wasRequired ? Confidence.Certain : Confidence.Likely);
            },
        };

        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG3002", Title = "Request body added",
                Category = FindingCategory.RequestBody,
                DefaultSeverity = Severity.Info,
                DefaultCompatibility = Compatibility.NonBreaking,
                ChangeKinds = [ChangeKinds.RequestBodyAdded],
                Description = "The operation now documents an optional request body.",
                Impact = "Additive: clients without a body keep working as long as it stays optional.",
                Remediation = "No action required; document the new payload shape.",
            },
            Build = c =>
            {
                var nowRequired = c.Details.TryGetValue("required", out var r) && r == "true";
                return FindingFactory.Create("CG3002", nowRequired ? "Required request body added" : "Request body added",
                    nowRequired ? Severity.High : Severity.Info,
                    nowRequired ? Compatibility.Breaking : Compatibility.NonBreaking, c,
                    nowRequired
                        ? $"A required request body was added to {Where(c)}."
                        : $"An optional request body was added to {Where(c)}.",
                    nowRequired
                        ? "Existing calls without a body will be rejected with 400 responses."
                        : "Additive change for existing callers.",
                    nowRequired
                        ? "Ship as optional first; enforce only after consumers are verified to send it."
                        : "No action required.",
                    Confidence.Certain);
            },
        };

        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG3003", Title = "Optional request body became required",
                Category = FindingCategory.RequestBody,
                DefaultSeverity = Severity.High,
                DefaultCompatibility = Compatibility.Breaking,
                ChangeKinds = [ChangeKinds.RequestBodyRequiredChanged],
                Description = "The operation now requires a body that was previously optional.",
                Impact = "Existing calls without a body will be rejected with 400 responses.",
                Remediation = "Keep the body optional, or enforce only in a new major version.",
                BreakingExample = "requestBody:\n  required: false   # -> true",
            },
            AppliesTo = c => c.NewValue == "required",
            Build = c => FindingFactory.Create("CG3003", "Optional request body became required",
                Severity.High, Compatibility.Breaking, c,
                $"The request body of {Where(c)} changed from optional to required.",
                "Clients calling without a body will now fail validation.",
                "Keep the body optional until consumers are verified to send it."),
        };

        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG3004", Title = "Required request body became optional",
                Category = FindingCategory.RequestBody,
                DefaultSeverity = Severity.Low,
                DefaultCompatibility = Compatibility.NonBreaking,
                ChangeKinds = [ChangeKinds.RequestBodyRequiredChanged],
                Description = "The previously mandatory request body is now optional.",
                Impact = "Non-breaking: all previously valid requests remain valid.",
                Remediation = "Confirm server-side behaviour when the body is omitted.",
            },
            AppliesTo = c => c.NewValue != "required",
            Build = c => FindingFactory.Create("CG3004", "Required request body became optional",
                Severity.Low, Compatibility.NonBreaking, c,
                $"The request body of {Where(c)} changed from required to optional.",
                "Non-breaking relaxation.",
                "Verify behaviour when the body is omitted is well-defined."),
        };

        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG3005", Title = "Supported request content type removed",
                Category = FindingCategory.RequestBody,
                DefaultSeverity = Severity.High,
                DefaultCompatibility = Compatibility.Breaking,
                ChangeKinds = [ChangeKinds.RequestContentTypeRemoved],
                Description = "A media type the server accepted for this operation was removed.",
                Impact = "Clients posting this content type will receive 415 Unsupported Media Type.",
                Remediation = "Keep accepting legacy content types during migration; negotiate removal with consumers.",
                BreakingExample = "requestBody:\n  content:\n    application/xml: {...}   # removed",
            },
            Build = c => FindingFactory.Create("CG3005", "Supported request content type removed",
                Severity.High, Compatibility.Breaking, c,
                $"Content type '{c.OldValue}' is no longer accepted by {Where(c)}.",
                "Requests using this media type will be rejected with 415 responses.",
                "Continue accepting the media type during a transition or migrate clients first."),
        };

        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG3006", Title = "Request content type added",
                Category = FindingCategory.RequestBody,
                DefaultSeverity = Severity.Info,
                DefaultCompatibility = Compatibility.NonBreaking,
                ChangeKinds = [ChangeKinds.RequestContentTypeAdded],
                Description = "A new media type is accepted for this operation's request body.",
                Impact = "Purely additive input capability.",
                Remediation = "No action required.",
            },
            Build = c => FindingFactory.Create("CG3006", "Request content type added",
                Severity.Info, Compatibility.NonBreaking, c,
                $"New content type '{c.NewValue}' accepted by {Where(c)}.",
                "Additive change; existing clients unaffected.",
                "No action required."),
        };
    }
}
