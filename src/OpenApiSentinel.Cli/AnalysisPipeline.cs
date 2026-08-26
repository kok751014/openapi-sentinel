using System.Text.Json;
using System.Text.Json.Nodes;
using OpenApiSentinel.Core;
using OpenApiSentinel.OpenApi;
using OpenApiSentinel.OpenApi.Model;

namespace OpenApiSentinel.Cli;

/// <summary>Loaded specification plus diagnostics from reference resolution.</summary>
/// <param name="Document">The normalized API document.</param>
/// <param name="UnresolvedExternalRefs">Count of external refs that could not be resolved.</param>
/// <param name="Loaded">The raw entry document as loaded (file bytes/HTTP body); used by
/// the Bruno bootstrap snapshot. Null for sources without a single raw entry (stdin).</param>
public sealed record AnalysisOutcome(ApiDocument Document, int UnresolvedExternalRefs, LoadedSpec? Loaded = null);

/// <summary>Loads one specification through the full pipeline: load → parse → resolve → normalize.</summary>
public static class AnalysisPipeline
{
    public static async Task<AnalysisOutcome> LoadDocumentAsync(
        string source,
        RemoteFetchPolicy remotePolicy,
        IReadOnlyList<EnvHeader> envHeaders,
        bool allowRemoteOverride = false,
        ProcessingLimits? limits = null,
        bool allowAboveDocumentTree = false,
        CancellationToken ct = default)
    {
        var policy = allowRemoteOverride && !remotePolicy.Enabled
            ? remotePolicy with { Enabled = true }
            : remotePolicy;

        var loader = new SpecLoader(policy, envHeaders);
        LoadedSpec loaded;
        try
        {
            loaded = await loader.LoadAsync(source, ct);
        }
        catch (SpecFetchException)
        {
            throw; // mapped to exit code 4 by the runner
        }

        var effectiveLimits = limits ?? ProcessingLimits.Default();
        JsonNode root;
        try
        {
            root = DocumentParser.Parse(loaded.SourceName, loaded.Content, effectiveLimits);
        }
        catch (SpecParseException)
        {
            throw; // mapped to exit code 2
        }

        var resolver = new RefResolver(effectiveLimits, policy, allowAboveDocumentTree);
        resolver.Resolve(root, loaded.BaseDirectoryOrNull, loaded.BaseUriOrNull);

        return new AnalysisOutcome(
            OpenApiNormalizer.Normalize(root),
            RefResolver.CountUnresolvedMarkers(root),
            loaded);
    }
}

/// <summary>
/// Baseline files are JSON arrays of finding fingerprints. They acknowledge known issues so
/// only NEW findings surface; stale entries are reported by the engine.
/// </summary>
public static class BaselineStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static IReadOnlyList<string> Read(string path)
    {
        if (!File.Exists(path))
            throw new SentinelConfigException($"Baseline file not found: '{path}'. Create one with --update-baseline.");
        List<string> fingerprints;
        try
        {
            fingerprints = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path)) ?? [];
        }
        catch (JsonException ex)
        {
            throw new SentinelConfigException($"Baseline file '{path}' is not a valid JSON fingerprint array: {ex.Message}");
        }
        return fingerprints;
    }

    public static void Write(string path, IEnumerable<string> fingerprints)
    {
        var ordered = fingerprints.Order(StringComparer.Ordinal).ToList();
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(ordered, Options));
    }
}
