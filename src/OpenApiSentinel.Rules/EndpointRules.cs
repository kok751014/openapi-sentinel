using OpenApiSentinel.OpenApi;
using OpenApiSentinel.Core;

namespace OpenApiSentinel.Rules;

/// <summary>Rules for endpoint- and operation-level changes (CG10xx).</summary>
internal static class EndpointRules
{
    public static IEnumerable<RuleDefinition> All()
    {
        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG1001", Title = "Endpoint removed",
                Category = FindingCategory.Endpoint,
                DefaultSeverity = Severity.Critical,
                DefaultCompatibility = Compatibility.Breaking,
                ChangeKinds = [ChangeKinds.EndpointRemoved],
                Description = "A previously published path no longer exists in the new contract. Clients calling it will receive 404 responses.",
                Impact = "Every consumer of this endpoint stops working immediately: 404 Not Found on all requests.",
                Remediation = "Keep the endpoint operational during a deprecation window, return 410 Gone with guidance if it must disappear, and migrate clients before removal.",
                BreakingExample = "- path: /orders/{id}\n  get: { ... }   # present in v1, absent in v2",
            },
            Build = c => FindingFactory.Create("CG1001", "Endpoint removed", Severity.Critical,
                Compatibility.Breaking, c,
                $"The endpoint '{c.Path}' was removed from the API.",
                "All existing consumers of this endpoint will receive 404 responses and fail.",
                "Restore the endpoint or run a deprecation period; notify active consumers before removal."),
        };

        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG1002", Title = "Operation removed",
                Category = FindingCategory.Endpoint,
                DefaultSeverity = Severity.Critical,
                DefaultCompatibility = Compatibility.Breaking,
                ChangeKinds = [ChangeKinds.OperationRemoved],
                Description = "An HTTP method on an existing path disappeared while the path itself remains.",
                Impact = "Clients using this method against the path will receive 404/405 errors.",
                Remediation = "Re-add the operation or deprecate it explicitly and give consumers time to migrate.",
                BreakingExample = "paths:\n  /orders:\n    get: {...}   # DELETE /orders existed in v1 but not in v2",
                NonBreakingExample = "# adding a method is non-breaking (see CG1004)",
            },
            Build = c => FindingFactory.Create("CG1002", "Operation removed", Severity.Critical,
                Compatibility.Breaking, c,
                $"Operation {c.HttpMethod.ToHttpString()} {c.Path} was removed.",
                "Existing calls to this operation fail with 404/405 responses.",
                "Restore the operation or formally deprecate it and coordinate a migration window."),
        };

        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG1003", Title = "Endpoint added",
                Category = FindingCategory.Endpoint,
                DefaultSeverity = Severity.Info,
                DefaultCompatibility = Compatibility.NonBreaking,
                ChangeKinds = [ChangeKinds.EndpointAdded],
                Description = "A new path was added to the API.",
                Impact = "No existing consumer is affected; new capability is available.",
                Remediation = "No action required for compatibility. Consider documenting the new endpoint and releasing a minor version.",
            },
            Build = c => FindingFactory.Create("CG1003", "Endpoint added", Severity.Info,
                Compatibility.NonBreaking, c,
                $"New endpoint '{c.Path}' was added.",
                "Additive change: existing consumers are unaffected.",
                "Document the new endpoint; ship as a minor version bump."),
        };

        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG1004", Title = "Operation added",
                Category = FindingCategory.Endpoint,
                DefaultSeverity = Severity.Info,
                DefaultCompatibility = Compatibility.NonBreaking,
                ChangeKinds = [ChangeKinds.OperationAdded],
                Description = "A new HTTP method was added to an existing path.",
                Impact = "Purely additive; existing operations are untouched.",
                Remediation = "No action required; consider announcing the new capability.",
                NonBreakingExample = "paths:\n  /orders:\n    delete: {...}   # newly supported",
            },
            Build = c => FindingFactory.Create("CG1004", "Operation added", Severity.Info,
                Compatibility.NonBreaking, c,
                $"New operation {c.HttpMethod.ToHttpString()} {c.Path} was added.",
                "Additive change: no existing call pattern is affected.",
                "No action required; announce the capability if relevant."),
        };

        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG1005", Title = "Operation deprecated",
                Category = FindingCategory.Endpoint,
                DefaultSeverity = Severity.Medium,
                DefaultCompatibility = Compatibility.PotentiallyBreaking,
                ChangeKinds = [ChangeKinds.OperationDeprecatedToggled],
                Description = "An operation was marked deprecated, signalling planned future removal.",
                Impact = "Not immediately breaking, but signals that removal is planned. Consumers should start migrating.",
                Remediation = "Publish a removal timeline and migration guide; monitor usage of the deprecated operation.",
                BreakingExample = "get:\n  deprecated: true   # was false or absent",
            },
            Build = c =>
            {
                var nowDeprecated = c.NewValue == "true";
                var sev = nowDeprecated ? Severity.Medium : Severity.Low;
                var compat = nowDeprecated ? Compatibility.PotentiallyBreaking : Compatibility.NonBreaking;
                return FindingFactory.Create("CG1005", nowDeprecated ? "Operation deprecated" : "Deprecation removed",
                    sev, compat, c,
                    nowDeprecated
                        ? $"{RuleText.DescribeChange(c)} was marked deprecated."
                        : $"{RuleText.DescribeChange(c)} is no longer marked deprecated.",
                    nowDeprecated
                        ? "Consumers should migrate away; the operation is expected to be removed in a future release."
                        : "Consumers relying on the deprecation status may resume usage; no immediate impact.",
                    nowDeprecated
                        ? "Communicate the deprecation and provide a migration path with a sunset date."
                        : "Confirm the undeprecation is intentional and documented.");
            },
        };

        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG1010", Title = "Servers changed",
                Category = FindingCategory.Metadata,
                DefaultSeverity = Severity.Medium,
                DefaultCompatibility = Compatibility.PotentiallyBreaking,
                ChangeKinds = [ChangeKinds.ServerChanged],
                Description = "The set of advertised server URLs changed between contracts.",
                Impact = "If a base URL disappears, SDKs generated from the old document may target hosts that are no longer valid.",
                Remediation = "Ensure old server URLs keep working during a transition period, or version the base path.",
                BreakingExample = "servers:\n  - url: https://api.example.com/v1   # replaced by /v2",
                NonBreakingExample = "servers:\n  - url: https://api.example.com/v1\n  - url: https://api.example.com/v2   # additive",
            },
            Build = c =>
            {
                static HashSet<string> ParseUrls(string? joined) =>
                    string.IsNullOrEmpty(joined)
                        ? []
                        : joined.Split(", ").Select(u => u.Trim()).ToHashSet(StringComparer.Ordinal);

                var oldUrls = ParseUrls(c.OldValue);
                var newUrls = ParseUrls(c.NewValue);

                // All documented servers disappeared: generated clients lose their base URL.
                if (oldUrls.Count > 0 && newUrls.Count == 0)
                    return FindingFactory.Create("CG1010", "All advertised servers removed",
                        Severity.High, Compatibility.Breaking, c,
                        $"Every advertised server was removed (was: {c.OldValue}).",
                        "Clients and SDKs pinned to these base URLs stop resolving.",
                        "Keep at least one server entry; document deprecations instead of deleting.");

                // Purely additive: every previous URL remains advertised.
                if (oldUrls.Count > 0 && newUrls.IsSupersetOf(oldUrls))
                    return FindingFactory.Create("CG1010", "Servers added",
                        Severity.Low, Compatibility.NonBreaking, c,
                        $"New server URL(s) advertised: {string.Join(", ", newUrls.Except(oldUrls).Order())}.",
                        "Additive: existing base URLs remain valid.",
                        "No action required for existing consumers.",
                        Confidence.Certain);

                return FindingFactory.Create("CG1010", "Servers changed",
                    Severity.Medium, Compatibility.PotentiallyBreaking, c,
                    $"Advertised servers changed: {RuleText.Values(c)}.",
                    "Generated clients pinned to a removed server URL may stop resolving.",
                    "Keep old base URLs functional during transition or communicate the new endpoints clearly.");
            },
        };
    }
}
