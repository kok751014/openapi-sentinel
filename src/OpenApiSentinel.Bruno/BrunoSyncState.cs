using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenApiSentinel.Bruno;

/// <summary>
/// Schema-versioned incremental sync state. Stored next to the collection file by default;
/// contains only hashes and timestamps — never secrets or spec content.
/// </summary>
public sealed record BrunoSyncState
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("generatorVersion")]
    public string GeneratorVersion { get; init; } = "";

    /// <summary>Fingerprint of the OpenAPI contract surface the sync derives from.</summary>
    [JsonPropertyName("sourceHash")]
    public string SourceHash { get; init; } = "";

    /// <summary>Hash of the bruno: config section (affects behavior).</summary>
    [JsonPropertyName("brunoConfigHash")]
    public string BrunoConfigHash { get; init; } = "";

    /// <summary>Hash of bruno.examples: settings (scenario output changes when it differs).</summary>
    [JsonPropertyName("exampleConfigHash")]
    public string ExampleConfigHash { get; init; } = "";

    /// <summary>Hash of the business-scenario file content (null = no file configured).</summary>
    [JsonPropertyName("scenariosFileHash")]
    public string? ScenariosFileHash { get; init; }

    [JsonPropertyName("lastSyncUtc")]
    public string? LastSyncUtc { get; init; }
}

public static class BrunoSyncStateStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static BrunoSyncState? Read(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var state = JsonSerializer.Deserialize<BrunoSyncState>(File.ReadAllText(path));
            // Corruption or unknown schema → treat as absent so a full safe rebuild happens.
            return state is { SchemaVersion: 1 } ? state : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    public static void Write(string path, BrunoSyncState state)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(state, Options));
    }

    public static bool Matches(BrunoSyncState? state, BrunoSyncState expected) =>
        state is not null &&
        state.SchemaVersion == expected.SchemaVersion &&
        string.Equals(state.GeneratorVersion, expected.GeneratorVersion, StringComparison.Ordinal) &&
        string.Equals(state.SourceHash, expected.SourceHash, StringComparison.Ordinal) &&
        string.Equals(state.BrunoConfigHash, expected.BrunoConfigHash, StringComparison.Ordinal) &&
        string.Equals(state.ExampleConfigHash, expected.ExampleConfigHash, StringComparison.Ordinal) &&
        string.Equals(state.ScenariosFileHash ?? "", expected.ScenariosFileHash ?? "", StringComparison.Ordinal);
}
