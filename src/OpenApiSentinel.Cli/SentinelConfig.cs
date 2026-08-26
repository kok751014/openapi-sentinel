using System.Globalization;
using OpenApiSentinel.Bruno;
using OpenApiSentinel.Core;
using OpenApiSentinel.OpenApi;
using OpenApiSentinel.Rules;

namespace OpenApiSentinel.Cli;

/// <summary>Typed view of openapi-sentinel.yml. Produced only by <see cref="SentinelConfigLoader"/>.</summary>
public sealed record SentinelConfiguration
{
    /// <summary>Parsed fail-on policy; null = default (breaking or potentially-breaking at medium+).</summary>
    public FailurePolicy? Policy { get; init; }

    public IReadOnlyList<RuleOverride> Overrides { get; init; } = [];

    public IReadOnlyList<string> IgnorePaths { get; init; } = [];

    public RemoteFetchPolicy RemoteReferences { get; init; } = RemoteFetchPolicy.Disabled();

    public IReadOnlyList<EnvHeader> HeaderEnv { get; init; } = [];

    public IReadOnlyList<SuppressionEntry> Suppressions { get; init; } = [];

    /// <summary>Path to a baseline file (JSON array of finding fingerprints).</summary>
    public string? BaselinePath { get; init; }

    /// <summary>Emit an aggregated Verify finding for change kinds no rule evaluates.</summary>
    public bool ReportUnknownKinds { get; init; }

    /// <summary>Configured output artifact paths keyed by format.</summary>
    public IReadOnlyDictionary<string, string> Output { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Overrides for the built-in safety budgets (giant legitimate specs);
    /// null = defaults. See ProcessingLimits.</summary>
    public ProcessingLimits? Limits { get; init; }

    /// <summary>Permit relative external file references that climb above the main
    /// document's directory (e.g. Azure resource-manager specs sharing common-types).
    /// Absolute paths outside the document tree stay blocked in every mode.</summary>
    public bool AllowAboveDocumentTree { get; init; }

    /// <summary>Bruno sync / scenario-generation section; null when absent (feature off).</summary>
    public BrunoConfiguration? Bruno { get; init; }

    /// <summary>Directory of the configuration file itself; relative paths in the config
    /// resolve against this so MSBuild/CI invocations from any cwd behave identically.</summary>
    public string? ConfigBaseDirectory { get; init; }
}

public sealed class SentinelConfigException(string message) : Exception(message);

/// <summary>
/// Loads and validates openapi-sentinel.yml. Unknown keys produce actionable warnings
/// (never silently ignored); semantic errors (bad severity, missing reason, bad date)
/// raise <see cref="SentinelConfigException"/> → exit code 3.
/// </summary>
public static class SentinelConfigLoader
{
    private static readonly HashSet<string> TopLevelKeys = new(StringComparer.Ordinal)
    {
        "failOn", "rules", "ignore", "remoteReferences", "headerEnv",
        "suppressions", "baseline", "output", "reportUnknownKinds", "limits",
        "externalReferences", "bruno",
    };

    private static readonly Dictionary<string, HashSet<string>> NestedKeys = new(StringComparer.Ordinal)
    {
        ["ignore"] = new(StringComparer.Ordinal) { "paths" },
        ["remoteReferences"] = new(StringComparer.Ordinal)
            { "enabled", "allowHttp", "allowPrivateNetwork", "allowedHosts", "timeoutSeconds", "maxResponseBytes" },
        ["output"] = new(StringComparer.Ordinal)
            { "console", "json", "markdown", "html", "sarif", "junit", "changelog", "pr-summary" },
        ["limits"] = new(StringComparer.Ordinal)
            { "maxDocumentBytes", "maxDepth", "maxNodes", "maxResolvedRefs", "maxRefChainDepth" },
        ["externalReferences"] = new(StringComparer.Ordinal)
            { "allowAboveDocumentTree" },
        ["bruno"] = new(StringComparer.Ordinal)
            { "enabled", "source", "collectionPath", "workspacePath", "reportPath", "format", "sync", "examples" },
        ["bruno.sync"] = new(StringComparer.Ordinal)
            { "preserveManualValues", "preserveScripts", "preserveTests", "preserveDocs",
              "removeDeletedOperations", "statePath" },
        ["bruno.examples"] = new(StringComparer.Ordinal)
            { "enabled", "language", "ignoreAuthentication", "maxPerOperation", "scenariosPath" },
    };

    private const string SuppressKeyRule = "rule";
    private const string SuppressKeyPath = "path";
    private const string SuppressKeyReason = "reason";
    private const string SuppressKeyExpires = "expires";

    public static SentinelConfiguration Load(string path, Action<string>? warn = null)
    {
        warn ??= _ => { };
        if (!File.Exists(path))
            throw new SentinelConfigException($"Configuration file not found: '{path}'.");

        string yaml;
        try
        {
            yaml = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            throw new SentinelConfigException($"Cannot read configuration file '{path}': {ex.Message}");
        }

        object? raw;
        var deserializer = new YamlDotNet.Serialization.DeserializerBuilder().Build();
        try
        {
            raw = deserializer.Deserialize<object>(yaml);
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            throw new SentinelConfigException($"Invalid YAML in '{path}' (line {ex.Start.Line}): {ex.Message}");
        }

        if (raw is not Dictionary<object, object> root)
            throw new SentinelConfigException($"Configuration root of '{path}' must be a mapping.");

        foreach (var key in root.Keys)
        {
            if (key is string k && !TopLevelKeys.Contains(k))
                warn($"Unknown configuration key '{k}' in '{path}'. Supported: {string.Join(", ", TopLevelKeys.Order())}.");
        }

        foreach (var (section, allowed) in NestedKeys)
        {
            if (!root.TryGetValue(section, out var value) || value is not Dictionary<object, object> map) continue;
            foreach (var key in map.Keys)
            {
                if (key is string k && !allowed.Contains(k))
                    warn($"Unknown key '{section}.{k}' in '{path}'. Supported: {string.Join(", ", allowed.Order())}.");
            }
        }

        // ---- failOn ----
        FailurePolicy? policy = null;
        if (root.TryGetValue("failOn", out var failOnRaw))
        {
            var failOn = RequireString(failOnRaw, "failOn");
            policy = ParseFailOn(failOn);
        }

        // ---- rules ----
        var overrides = new List<RuleOverride>();
        if (root.TryGetValue("rules", out var rulesRaw))
        {
            if (rulesRaw is not Dictionary<object, object> rulesMap)
                throw new SentinelConfigException("'rules' must be a mapping of rule id to 'off' or a severity name.");
            foreach (var (idRaw, actionRaw) in rulesMap)
            {
                var id = RequireString(idRaw, "rules key");
                ValidateRuleId(id);
                var action = RequireString(actionRaw, $"rules.{id}");
                if (string.Equals(action, "off", StringComparison.OrdinalIgnoreCase))
                {
                    overrides.Add(new RuleOverride { RuleId = id, Action = "off" });
                    continue;
                }
                if (!SeverityNames.TryParse(action, out var sev) ||
                    action.Equals("off", StringComparison.OrdinalIgnoreCase) ||
                    action.Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    throw new SentinelConfigException(
                        $"'rules.{id}: {action}' is invalid. Use 'off' or one of: critical, high, medium, low, info, verify.");
                }
                overrides.Add(new RuleOverride { RuleId = id, NewSeverity = sev });
            }
        }

        // ---- ignore.paths ----
        var ignorePaths = new List<string>();
        if (root.TryGetValue("ignore", out var ignoreRaw))
        {
            if (ignoreRaw is not Dictionary<object, object> ignoreMap || !ignoreMap.TryGetValue("paths", out var pathsRaw))
                throw new SentinelConfigException("'ignore' must contain a 'paths' list.");
            ignorePaths.AddRange(RequireStringList(pathsRaw, "ignore.paths"));
        }

        // ---- remoteReferences ----
        var remote = RemoteFetchPolicy.Disabled();
        if (root.TryGetValue("remoteReferences", out var remoteRaw))
        {
            if (remoteRaw is not Dictionary<object, object> remoteMap)
                throw new SentinelConfigException("'remoteReferences' must be a mapping.");
            remote = new RemoteFetchPolicy
            {
                Enabled = GetBool(remoteMap, "enabled") ?? false,
                AllowHttp = GetBool(remoteMap, "allowHttp") ?? false,
                AllowPrivateNetwork = GetBool(remoteMap, "allowPrivateNetwork") ?? false,
                AllowedHosts = remoteMap.TryGetValue("allowedHosts", out var hosts)
                    ? new HashSet<string>(RequireStringList(hosts, "remoteReferences.allowedHosts"), StringComparer.OrdinalIgnoreCase)
                    : null,
                TimeoutSeconds = GetInt(remoteMap, "timeoutSeconds") ?? 30,
                MaxResponseBytes = GetInt(remoteMap, "maxResponseBytes") ?? 20 * 1024 * 1024,
            };
            if (remote.Enabled && remote.TimeoutSeconds <= 0)
                throw new SentinelConfigException("'remoteReferences.timeoutSeconds' must be positive.");
        }

        // ---- headerEnv ----
        var headers = new List<EnvHeader>();
        if (root.TryGetValue("headerEnv", out var headerRaw))
        {
            if (headerRaw is not Dictionary<object, object> headerMap)
                throw new SentinelConfigException("'headerEnv' must be a mapping of header name to environment variable name.");
            foreach (var (nameRaw, envRaw) in headerMap)
            {
                var name = RequireString(nameRaw, "headerEnv key");
                var envVar = RequireString(envRaw, $"headerEnv.{name}");
                headers.Add(new EnvHeader(name, envVar));
            }
        }

        // ---- suppressions ----
        var suppressions = new List<SuppressionEntry>();
        if (root.TryGetValue("suppressions", out var supprRaw))
        {
            if (supprRaw is not List<object> entries)
                throw new SentinelConfigException("'suppressions' must be a list of mappings.");
            foreach (var entry in entries)
            {
                if (entry is not Dictionary<object, object> map)
                    throw new SentinelConfigException("Each suppression must be a mapping with 'rule', 'reason'.");
                foreach (var key in map.Keys)
                {
                    if (key is string k && k is not (SuppressKeyRule or SuppressKeyPath or SuppressKeyReason or SuppressKeyExpires))
                        warn($"Unknown suppression key '{k}' (supported: {SuppressKeyRule}, {SuppressKeyPath}, {SuppressKeyReason}, {SuppressKeyExpires}).");
                }

                var rule = map.TryGetValue(SuppressKeyRule, out var r) ? RequireString(r, "suppressions.rule") : "*";
                if (!rule.Equals("*", StringComparison.Ordinal)) ValidateRuleId(rule);

                var pathGlob = map.TryGetValue(SuppressKeyPath, out var p) ? RequireString(p, "suppressions.path") : null;
                var reason = map.TryGetValue(SuppressKeyReason, out var re) ? RequireString(re, "suppressions.reason") : null;
                if (string.IsNullOrWhiteSpace(reason))
                    throw new SentinelConfigException(
                        $"Each suppression requires a non-empty 'reason' (rule={rule}{(pathGlob is null ? "" : $", path={pathGlob}")}).");

                DateTimeOffset? expires = null;
                if (map.TryGetValue(SuppressKeyExpires, out var expRaw))
                {
                    var expText = RequireString(expRaw, "suppressions.expires");
                    if (!DateTimeOffset.TryParse(expText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
                        throw new SentinelConfigException($"'suppressions.expires: {expText}' is not a valid date (YYYY-MM-DD).");
                    expires = parsed;
                }

                suppressions.Add(new SuppressionEntry { RuleId = rule, PathGlob = pathGlob, Reason = reason!, Expires = expires });
            }
        }

        // ---- reportUnknownKinds ----
        var reportUnknownKinds = false;
        if (root.TryGetValue("reportUnknownKinds", out var unknownRaw))
            reportUnknownKinds = GetBool(root, "reportUnknownKinds")
                ?? throw new SentinelConfigException("'reportUnknownKinds' must be true or false.");

        // ---- externalReferences ----
        var allowAboveDocumentTree = false;
        if (root.TryGetValue("externalReferences", out var extRefRaw) &&
            extRefRaw is Dictionary<object, object> extRefMap)
        {
            if (extRefMap.TryGetValue("allowAboveDocumentTree", out var aboveRaw))
                allowAboveDocumentTree = GetBool(extRefMap, "allowAboveDocumentTree")
                    ?? throw new SentinelConfigException("'externalReferences.allowAboveDocumentTree' must be true or false.");
        }

        // ---- baseline ----
        string? baselinePath = null;
        if (root.TryGetValue("baseline", out var baselineRaw))
            baselinePath = RequireString(baselineRaw, "baseline");

        // ---- output ----
        var output = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root.TryGetValue("output", out var outputRaw) && outputRaw is Dictionary<object, object> outputMap)
        {
            foreach (var (keyRaw, valueRaw) in outputMap)
            {
                var key = RequireString(keyRaw, "output key");
                output[key] = RequireString(valueRaw, $"output.{key}");
            }
        }

        // ---- limits (safety-budget overrides for giant legitimate specs) ----
        ProcessingLimits? limits = null;
        if (root.TryGetValue("limits", out var limitsRaw))
        {
            if (limitsRaw is not Dictionary<object, object> limitsMap)
                throw new SentinelConfigException("'limits' must be a mapping.");

            static int RequirePositive(Dictionary<object, object> map, string key, int? fallback)
            {
                var v = GetInt(map, key) ?? fallback;
                if (v is null || v <= 0)
                    throw new SentinelConfigException($"'limits.{key}' must be a positive integer.");
                return v.Value;
            }

            limits = new ProcessingLimits
            {
                MaxDocumentBytes = RequirePositive(limitsMap, "maxDocumentBytes", 20 * 1024 * 1024),
                MaxDepth = RequirePositive(limitsMap, "maxDepth", 128),
                MaxNodes = RequirePositive(limitsMap, "maxNodes", 2_000_000),
                MaxResolvedRefs = RequirePositive(limitsMap, "maxResolvedRefs", 200_000),
                MaxRefChainDepth = RequirePositive(limitsMap, "maxRefChainDepth", 64),
            };
        }

        // ---- bruno (sync + scenario generation) ----
        BrunoConfiguration? bruno = null;
        if (root.TryGetValue("bruno", out var brunoRaw))
        {
            if (brunoRaw is not Dictionary<object, object> brunoMap)
                throw new SentinelConfigException("'bruno' must be a mapping.");

            var enabled = GetBool(brunoMap, "enabled") ?? true;
            string? source = null, collectionPath = null, reportPath = null, workspacePath = null;
            if (brunoMap.TryGetValue("source", out var srcRaw))
                source = RequireString(srcRaw, "bruno.source");
            if (brunoMap.TryGetValue("collectionPath", out var colRaw))
                collectionPath = RequireString(colRaw, "bruno.collectionPath");
            if (brunoMap.TryGetValue("workspacePath", out var wsRaw))
                workspacePath = RequireString(wsRaw, "bruno.workspacePath");
            if (brunoMap.TryGetValue("reportPath", out var repRaw))
                reportPath = RequireString(repRaw, "bruno.reportPath");

            var format = "auto";
            if (brunoMap.TryGetValue("format", out var fmtRaw))
            {
                format = RequireString(fmtRaw, "bruno.format");
                if (!string.Equals(format, "auto", StringComparison.OrdinalIgnoreCase))
                    throw new SentinelConfigException(
                        $"'bruno.format: {format}' is not supported. Only 'auto' (= OpenCollection YAML) exists in this version.");
            }

            var syncSettings = new BrunoSyncSettings();
            if (brunoMap.TryGetValue("sync", out var syncRaw))
            {
                if (syncRaw is not Dictionary<object, object> syncMap)
                    throw new SentinelConfigException("'bruno.sync' must be a mapping.");
                foreach (var flag in new[] { "preserveScripts", "preserveTests" })
                {
                    if (syncMap.TryGetValue(flag, out var flagRaw) && GetBool(syncMap, flag) == false)
                        warn($"'bruno.sync.{flag}: false' is accepted but has no effect: Sentinel never deletes or rewrites user scripts/tests. The key exists for forward compatibility.");
                }

                syncSettings = new BrunoSyncSettings
                {
                    PreserveManualValues = GetBool(syncMap, "preserveManualValues") ?? true,
                    PreserveScripts = GetBool(syncMap, "preserveScripts") ?? true,
                    PreserveTests = GetBool(syncMap, "preserveTests") ?? true,
                    PreserveDocs = GetBool(syncMap, "preserveDocs") ?? true,
                    RemoveDeletedOperations = GetBool(syncMap, "removeDeletedOperations") ?? false,
                    StatePath = syncMap.TryGetValue("statePath", out var spRaw) ? RequireString(spRaw, "bruno.sync.statePath") : null,
                };
            }

            var exampleSettings = new BrunoExampleSettings();
            if (brunoMap.TryGetValue("examples", out var exRaw))
            {
                if (exRaw is not Dictionary<object, object> exMap)
                    throw new SentinelConfigException("'bruno.examples' must be a mapping.");

                var language = ScenarioTexts.LanguageTag;
                if (exMap.TryGetValue("language", out var langRaw))
                {
                    language = RequireString(langRaw, "bruno.examples.language");
                    if (!string.Equals(language, ScenarioTexts.LanguageTag, StringComparison.OrdinalIgnoreCase))
                        throw new SentinelConfigException(
                            $"'bruno.examples.language: {language}' is not supported yet. Supported: {ScenarioTexts.LanguageTag}.");
                }

                var maxPerOperation = GetInt(exMap, "maxPerOperation") ?? 8;
                if (maxPerOperation is < 1 or > 64)
                    throw new SentinelConfigException("'bruno.examples.maxPerOperation' must be between 1 and 64.");

                exampleSettings = new BrunoExampleSettings
                {
                    Enabled = GetBool(exMap, "enabled") ?? true,
                    Language = language,
                    IgnoreAuthentication = GetBool(exMap, "ignoreAuthentication") ?? true,
                    MaxPerOperation = maxPerOperation,
                    ScenariosPath = exMap.TryGetValue("scenariosPath", out var scRaw) ? RequireString(scRaw, "bruno.examples.scenariosPath") : null,
                };
            }

            bruno = new BrunoConfiguration
            {
                Enabled = enabled,
                Source = source,
                CollectionPath = collectionPath,
                WorkspacePath = workspacePath,
                ReportPath = reportPath,
                Format = format,
                Sync = syncSettings,
                Examples = exampleSettings,
            };
        }

        return new SentinelConfiguration
        {
            Policy = policy,
            Overrides = overrides,
            IgnorePaths = ignorePaths,
            RemoteReferences = remote,
            HeaderEnv = headers,
            Suppressions = suppressions,
            BaselinePath = baselinePath,
            ReportUnknownKinds = reportUnknownKinds,
            Output = output,
            Limits = limits,
            AllowAboveDocumentTree = allowAboveDocumentTree,
            Bruno = bruno,
            ConfigBaseDirectory = string.IsNullOrEmpty(path) ? null : Path.GetDirectoryName(Path.GetFullPath(path)),
        };
    }

    internal static FailurePolicy ParseFailOn(string failOn)
    {
        var normalized = failOn.Trim().ToLowerInvariant();
        return normalized switch
        {
            "all" or "any" => new FailurePolicy { FailOnAnyFinding = true },
            "none" => new FailurePolicy { FailOnAnyFinding = false, FailOnBreaking = false },
            _ when SeverityNames.TryParse(normalized, out var sev) && normalized is not ("all" or "off" or "none")
                => new FailurePolicy { FailOnSeverity = sev },
            _ => throw new SentinelConfigException(
                $"'failOn: {failOn}' is invalid. Use all, none, critical, high, medium, low, verify, or info."),
        };
    }

    private static void ValidateRuleId(string id)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(id, @"^CG\d{4}$"))
            throw new SentinelConfigException($"'{id}' is not a valid rule id (expected form CG1001). See 'openapi-sentinel rules'.");
    }

    private static string RequireString(object? value, string what) =>
        value?.ToString() is { Length: > 0 } s ? s : throw new SentinelConfigException($"'{what}' must be a non-empty string.");

    private static List<string> RequireStringList(object? value, string what)
    {
        if (value is not List<object> list)
            throw new SentinelConfigException($"'{what}' must be a list of strings.");
        return list.Select(v => RequireString(v, what)).ToList();
    }

    private static bool? GetBool(Dictionary<object, object> map, string key) =>
        map.TryGetValue(key, out var v) ? v.ToString() switch
        {
            "true" or "True" or "yes" or "on" => true,
            "false" or "False" or "no" or "off" => false,
            _ => throw new SentinelConfigException($"'{key}' must be true or false."),
        } : null;

    private static int? GetInt(Dictionary<object, object> map, string key)
    {
        if (!map.TryGetValue(key, out var v)) return null;
        return int.TryParse(v.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n
            : throw new SentinelConfigException($"'{key}' must be an integer.");
    }
}
