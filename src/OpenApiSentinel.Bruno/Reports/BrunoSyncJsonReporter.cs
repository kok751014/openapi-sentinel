using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenApiSentinel.Bruno.Reports;

/// <summary>Machine-readable bruno-sync-report.json contract.</summary>
public sealed record BrunoSyncReport
{
    public const string SchemaVersionValue = "bruno-sync/1.0";

    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; init; } = SchemaVersionValue;

    [JsonPropertyName("toolVersion")]
    public string ToolVersion { get; init; } = "";

    [JsonPropertyName("generatedAtUtc")]
    public string GeneratedAtUtc { get; init; } = "";

    [JsonPropertyName("dryRun")]
    public bool DryRun { get; init; }

    /// <summary>True when the incremental fast-path skipped all work.</summary>
    [JsonPropertyName("skipped")]
    public bool Skipped { get; init; }

    [JsonPropertyName("source")]
    public string Source { get; init; } = "";

    [JsonPropertyName("collectionPath")]
    public string CollectionPath { get; init; } = "";

    /// <summary>Version of the official OpenCollection schema the output was validated against.</summary>
    [JsonPropertyName("openCollectionSchemaVersion")]
    public string OpenCollectionSchemaVersion { get; init; } = OpenApiSentinel.Bruno.Validation.OpenCollectionSchemaValidator.SchemaVersion;

    /// <summary>Result of the mandatory pre-write official-schema validation gate.</summary>
    [JsonPropertyName("schemaValidation")]
    public BrunoSyncReportSchemaValidation SchemaValidation { get; init; } = new();

    [JsonPropertyName("summary")]
    public BrunoSyncReportSummary Summary { get; init; } = new();

    [JsonPropertyName("operations")]
    public IReadOnlyList<BrunoSyncReportOperation> Operations { get; init; } = [];

    [JsonPropertyName("conflicts")]
    public IReadOnlyList<BrunoSyncReportConflict> Conflicts { get; init; } = [];

    [JsonPropertyName("warnings")]
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record BrunoSyncReportSchemaValidation
{
    /// <summary>"passed" — the gate refuses any other value to reach a report.</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = "passed";
}

public sealed record BrunoSyncReportSummary
{
    [JsonPropertyName("operationsAdded")]
    public int OperationsAdded { get; init; }

    [JsonPropertyName("operationsUpdated")]
    public int OperationsUpdated { get; init; }

    [JsonPropertyName("operationsStale")]
    public int OperationsStale { get; init; }

    [JsonPropertyName("operationsRemoved")]
    public int OperationsRemoved { get; init; }

    [JsonPropertyName("operationsUnchanged")]
    public int OperationsUnchanged { get; init; }

    [JsonPropertyName("examplesAdded")]
    public int ExamplesAdded { get; init; }

    [JsonPropertyName("examplesUpdated")]
    public int ExamplesUpdated { get; init; }

    [JsonPropertyName("operationsWithManualPreserved")]
    public int OperationsWithManualPreserved { get; init; }

    [JsonPropertyName("conflicts")]
    public int Conflicts { get; init; }

    [JsonPropertyName("warnings")]
    public int Warnings { get; init; }
}

public sealed record BrunoSyncReportOperation
{
    /// <summary>"added" | "updated" | "unchanged" | "stale" | "removed"</summary>
    [JsonPropertyName("action")]
    public string Action { get; init; } = "";

    [JsonPropertyName("method")]
    public string Method { get; init; } = "";

    [JsonPropertyName("path")]
    public string Path { get; init; } = "";

    [JsonPropertyName("operationId")]
    public string? OperationId { get; init; }

    [JsonPropertyName("requestName")]
    public string? RequestName { get; init; }

    [JsonPropertyName("changes")]
    public IReadOnlyList<string> Changes { get; init; } = [];

    [JsonPropertyName("preserved")]
    public IReadOnlyList<string> Preserved { get; init; } = [];

    [JsonPropertyName("examples")]
    public IReadOnlyList<BrunoSyncReportExample> Examples { get; init; } = [];

    [JsonPropertyName("warnings")]
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record BrunoSyncReportExample(string Name, string Action)
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = Name;

    /// <summary>"added" | "updated" | "kept"</summary>
    [JsonPropertyName("action")]
    public string Action { get; init; } = Action;
}

public sealed record BrunoSyncReportConflict(string Kind, string Detail)
{
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = Kind;

    [JsonPropertyName("detail")]
    public string Detail { get; init; } = Detail;
}

public static class BrunoSyncJsonReporter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Deterministic serialization: callers must supply a fixed timestamp for tests.</summary>
    public static string Render(BrunoSyncReport report) =>
        JsonSerializer.Serialize(report, Options);
}
