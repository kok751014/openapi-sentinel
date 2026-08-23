using System.Text.Json;
using System.Text.Json.Serialization;
using OpenApiSentinel.Core;

namespace OpenApiSentinel.Reporting;

/// <summary>
/// Machine-readable JSON report. The structure is a documented, versioned contract:
/// schemaVersion "1.0" fields are additive-only; removals or renames bump the schema version.
/// </summary>
public sealed class JsonReporter : IResultReporter
{
    /// <summary>Serializer settings for the report contract (stable field order is insertion order).</summary>
    public static readonly JsonSerializerOptions ReportOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string FormatName => "json";

    public string Render(ComparisonResult result, ReporterOptions options)
    {
        var payload = options.IncludeSuppressed
            ? result
            : result with { Suppressed = [] };
        return JsonSerializer.Serialize(payload, ReportOptions);
    }
}
