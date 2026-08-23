using System.Text;
using OpenApiSentinel.Core;
using OpenApiSentinel.OpenApi;
using OpenApiSentinel.Rules;

namespace OpenApiSentinel.Cli;

/// <summary>validate / rules / explain / init / version commands.</summary>
public static class SimpleCommands
{
    private static readonly System.Text.Json.JsonSerializerOptions RulesJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase) },
    };

    public static async Task<int> ValidateAsync(
        string source, SentinelConfiguration config, bool allowRemoteOverride,
        IConsoleWriter console, CancellationToken ct)
    {
        var outcome = await AnalysisPipeline.LoadDocumentAsync(
            source, config.RemoteReferences, config.HeaderEnv, allowRemoteOverride, config.Limits, ct);
        var doc = outcome.Document;
        var paths = doc.Paths.Count;
        var operations = doc.Paths.Sum(p => p.Value.Operations.Count);
        console.Out($"OK — '{DisplayName(source)}' parsed as {doc.RawVersion}: {paths} path(s), {operations} operation(s), {doc.SecuritySchemes.Count} security scheme(s).");
        if (outcome.UnresolvedExternalRefs > 0)
        {
            console.Out($"Warning: {outcome.UnresolvedExternalRefs} external reference(s) could not be resolved " +
                        "(missing file, unreachable URL or policy refusal); affected subtrees were skipped.");
        }
        return ExitCodes.Ok;
    }

    public static int Rules(IConsoleWriter console, bool asJson)
    {
        var registry = RuleRegistry.CreateDefault();
        var rules = registry.All.OrderBy(r => r.Descriptor.Id, StringComparer.Ordinal).ToList();

        if (asJson)
        {
            var payload = rules.Select(r => new
            {
                id = r.Descriptor.Id,
                title = r.Descriptor.Title,
                category = r.Descriptor.Category.ToString(),
                defaultSeverity = SeverityNames.ToStringValue(r.Descriptor.DefaultSeverity),
                compatibility = r.Descriptor.DefaultCompatibility.ToString(),
                changeKinds = r.Descriptor.ChangeKinds,
                description = r.Descriptor.Description,
            });
            console.Out(System.Text.Json.JsonSerializer.Serialize(payload, RulesJsonOptions));
            return ExitCodes.Ok;
        }

        console.Out($"OpenAPI Sentinel built-in rules ({rules.Count}):");
        console.Out();
        foreach (var rule in rules.GroupBy(r => r.Descriptor.Category).OrderBy(g => g.Key))
        {
            console.Out(rule.Key.ToString().ToUpperInvariant());
            foreach (var r in rule)
            {
                console.Out($"  {r.Descriptor.Id}  [{SeverityNames.ToDisplayName(r.Descriptor.DefaultSeverity),8}]  {r.Descriptor.Title}");
            }
            console.Out();
        }
        console.Out("Run 'openapi-sentinel explain <RULE-ID>' for the full description of a rule.");
        return ExitCodes.Ok;
    }

    public static int Explain(string ruleId, IConsoleWriter console)
    {
        if (!RuleRegistry.CreateDefault().TryGet(ruleId.Trim(), out var rule))
        {
            console.WriteError($"Unknown rule id '{ruleId}'. Run 'openapi-sentinel rules' to list all ids.");
            return ExitCodes.InvalidInput;
        }

        var d = rule.Descriptor;
        var w = new StringBuilder();
        w.AppendLine($"{d.Id} — {d.Title}");
        w.AppendLine(new string('─', Math.Min(72, d.Id.Length + d.Title.Length + 3)));
        w.AppendLine($"Category: {d.Category}   Default severity: {SeverityNames.ToStringValue(d.DefaultSeverity)}   Classification: {d.DefaultCompatibility}");
        w.AppendLine($"Applies to: {string.Join(", ", d.ChangeKinds)}");
        w.AppendLine();
        w.AppendLine(d.Description);
        w.AppendLine();
        w.AppendLine("Impact");
        w.AppendLine($"  {d.Impact}");
        w.AppendLine();
        w.AppendLine("Remediation");
        w.AppendLine($"  {d.Remediation}");
        if (!string.IsNullOrEmpty(d.BreakingExample))
        {
            w.AppendLine();
            w.AppendLine("Breaking example");
            foreach (var line in d.BreakingExample.Split('\n')) w.AppendLine($"  {line.TrimEnd()}");
        }
        if (!string.IsNullOrEmpty(d.NonBreakingExample))
        {
            w.AppendLine();
            w.AppendLine("Non-breaking example");
            foreach (var line in d.NonBreakingExample.Split('\n')) w.AppendLine($"  {line.TrimEnd()}");
        }
        if (!string.IsNullOrEmpty(d.Limitations))
        {
            w.AppendLine();
            w.AppendLine("Limitations");
            w.AppendLine($"  {d.Limitations}");
        }
        console.Out(w.ToString());
        return ExitCodes.Ok;
    }

    public static int Init(bool force, IConsoleWriter console)
    {
        const string fileName = "openapi-sentinel.yml";
        var target = Path.Combine(Directory.GetCurrentDirectory(), fileName);
        if (File.Exists(target) && !force)
        {
            console.WriteError($"'{fileName}' already exists (use --force to overwrite).");
            return ExitCodes.InvalidConfiguration;
        }

        const string template = """
# OpenAPI Sentinel configuration
# Docs: https://github.com/openapi-sentinel/openapi-sentinel/blob/main/docs/configuration.md

# Minimum severity that fails CI: critical | high | medium | low | verify | info | all | none
failOn: high

rules:
  CG1003: info          # endpoint added
  # CG2001: off         # disable a rule entirely

ignore:
  paths:
    - /internal/**

remoteReferences:
  enabled: false        # allow fetching $ref targets over HTTP(S)
  allowHttp: false      # HTTPS only unless explicitly relaxed
  allowPrivateNetwork: false
  allowedHosts: []
  timeoutSeconds: 30

suppressions:
  - rule: CG1001
    path: "/legacy/**"
    reason: "Legacy mobile client migration until Q4"
    expires: 2026-12-31

# reportUnknownKinds: false   # flag changes no rule evaluates (CG9003)

# Processing safety budgets (rarely needed). When one trips during $ref
# resolution the run degrades to Verify findings (CG5022) instead of failing;
# raise these only for very large, reference-heavy specifications. Defaults:
# limits:
#   maxDocumentBytes: 20971520
#   maxDepth: 128
#   maxNodes: 2000000
#   maxResolvedRefs: 200000
#   maxRefChainDepth: 64

headerEnv: {}   # Authorization: SENTINEL_SWAGGER_TOKEN

output: {}
  # markdown: artifacts/api-diff.md
  # json: artifacts/api-diff.json
  # html: artifacts/api-diff.html
  # pr-summary: artifacts/pr-summary.md   # compact summary for PR comments
""";
        File.WriteAllText(target, template);
        console.Out($"Wrote starter configuration to '{target}'. Review failOn and ignore paths before committing.");
        return ExitCodes.Ok;
    }

    public static int Version(IConsoleWriter console)
    {
        console.Out($"openapi-sentinel {ToolVersion.Get()}");
        return ExitCodes.Ok;
    }

    private static string DisplayName(string source) => source == "-" ? "<stdin>" : source;
}
