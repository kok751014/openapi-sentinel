using System.Text.Json;
using System.Text.Json.Nodes;
using OpenApiSentinel.Core;
using OpenApiSentinel.OpenApi;
using OpenApiSentinel.OpenApi.Model;

namespace OpenApiSentinel.Cli;

/// <summary>Loads one specification through the full pipeline: load → parse → resolve → normalize.</summary>
public static class AnalysisPipeline
{
    public static async Task<ApiDocument> LoadDocumentAsync(
        string source,
        RemoteFetchPolicy remotePolicy,
        IReadOnlyList<EnvHeader> envHeaders,
        bool allowRemoteOverride = false,
        ProcessingLimits? limits = null,
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

        var resolver = new RefResolver(effectiveLimits, policy);
        resolver.Resolve(root, loaded.BaseDirectoryOrNull);

        return OpenApiNormalizer.Normalize(root);
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
