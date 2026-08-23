using OpenApiSentinel.Core;
using OpenApiSentinel.OpenApi;

namespace OpenApiSentinel.Rules;

/// <summary>Rules for document metadata and operation identity changes (CG70xx).</summary>
internal static class MetadataRules
{
    private static string Where(SemanticChange c) =>
        c.HttpMethod == HttpMethodKind.None ? c.Path : $"{c.HttpMethod.ToHttpString()} {c.Path}";

    public static IEnumerable<RuleDefinition> All()
    {
        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG7001", Title = "Info version changed",
                Category = FindingCategory.Metadata,
                DefaultSeverity = Severity.Info,
                DefaultCompatibility = Compatibility.Informational,
                ChangeKinds = [ChangeKinds.InfoVersionChanged],
                Description = "The info.version field of the document changed.",
                Impact = "Documentation-only; runtime behaviour unchanged. Useful signal when auditing release discipline.",
                Remediation = "No action required; keep the version aligned with the actual API lifecycle.",
            },
            Build = c => FindingFactory.Create("CG7001", "API version changed",
                Severity.Info, Compatibility.Informational, c,
                $"Document version changed: {RuleText.Values(c)}.",
                "Metadata only — no wire-level impact.",
                "No action required."),
        };

        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG7002", Title = "Title changed",
                Category = FindingCategory.Metadata,
                DefaultSeverity = Severity.Info,
                DefaultCompatibility = Compatibility.Informational,
                ChangeKinds = [ChangeKinds.TitleChanged],
                Description = "The info.title of the document changed.",
                Impact = "Cosmetic.",
                Remediation = "No action required.",
            },
            Build = c => FindingFactory.Create("CG7002", "API title changed",
                Severity.Info, Compatibility.Informational, c,
                $"Document title changed: {RuleText.Values(c)}.",
                "Cosmetic change with no compatibility impact.",
                "No action required."),
        };

        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG7003", Title = "operationId changed",
                Category = FindingCategory.Metadata,
                DefaultSeverity = Severity.Medium,
                DefaultCompatibility = Compatibility.PotentiallyBreaking,
                ChangeKinds = [ChangeKinds.OperationIdChanged],
                Description = "The operationId of an operation changed.",
                Impact = "Generated client method names, SDK symbols, and tooling references change; code that pins the old identifier breaks at build time.",
                Remediation = "Treat operationId as a stable public identifier; keep the old id or ship generated-client major versions.",
                BreakingExample = "operationId: getOrder  →  operationId: fetchOrder",
                Limitations = "Purely a contract/tooling concern: the HTTP behaviour itself is unchanged.",
            },
            Build = c => FindingFactory.Create("CG7003", "operationId changed",
                Severity.Medium, Compatibility.PotentiallyBreaking, c,
                $"operationId on {Where(c)} changed: {RuleText.Values(c)}.",
                "Clients using generated SDKs may face renamed methods; automation referencing the old id breaks.",
                "Keep stable operationIds; if renaming is unavoidable, coordinate with consumers and regenerate SDKs."),
        };

        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG7004", Title = "operationId removed",
                Category = FindingCategory.Metadata,
                DefaultSeverity = Severity.Medium,
                DefaultCompatibility = Compatibility.PotentiallyBreaking,
                ChangeKinds = [ChangeKinds.OperationIdRemoved],
                Description = "An operationId was removed from an operation (now unnamed).",
                Impact = "Tooling that keys on operationId loses its anchor; generated code falls back to path-derived names.",
                Remediation = "Restore the operationId or assign an equivalent stable identifier.",
            },
            Build = c => FindingFactory.Create("CG7004", "operationId removed",
                Severity.Medium, Compatibility.PotentiallyBreaking, c,
                $"operationId '{c.OldValue}' was removed from {Where(c)}.",
                "Code generation and documentation links that referenced this id degrade.",
                "Keep every operation identified by a stable operationId."),
        };

        yield return new RuleDefinition
        {
            Descriptor = new RuleDescriptor
            {
                Id = "CG7005", Title = "operationId added",
                Category = FindingCategory.Metadata,
                DefaultSeverity = Severity.Info,
                DefaultCompatibility = Compatibility.NonBreaking,
                ChangeKinds = [ChangeKinds.OperationIdAdded],
                Description = "A previously unnamed operation received an operationId.",
                Impact = "Additive; improves naming for future consumers.",
                Remediation = "No action required.",
            },
            Build = c => FindingFactory.Create("CG7005", "operationId added",
                Severity.Info, Compatibility.NonBreaking, c,
                $"New operationId '{c.NewValue}' on {Where(c)}.",
                "Additive naming improvement; no existing references break.",
                "No action required."),
        };
    }
}
