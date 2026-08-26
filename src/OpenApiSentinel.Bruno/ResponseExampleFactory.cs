using System.Text.Json;
using System.Text.Json.Nodes;
using OpenApiSentinel.OpenApi.Model;

namespace OpenApiSentinel.Bruno;

/// <summary>An expected-response pair attached to a generated example.</summary>
public sealed record ScenarioResponse(
    int? Status,
    string? StatusText,
    string BodyType,
    string? BodyData,
    IReadOnlyList<(string Name, string Value)> Headers);

/// <summary>
/// Derives expected responses for generated examples EXCLUSIVELY from documented OpenAPI
/// behavior. Priority: explicit media-type example → schema example/examples/default →
/// deterministic sample from the response schema. Nothing undocumented is ever invented:
/// when the specification does not describe the outcome, no response is attached and the
/// caller surfaces a warning instead.
/// </summary>
public static class ResponseExampleFactory
{
    /// <summary>Deterministic documented-success selection policy: 200 → 201 → 202 → 204.</summary>
    private static readonly string[] SuccessPreference = ["200", "201", "202", "204"];

    /// <summary>Failure scenarios may only cite statuses the contract explicitly documents
    /// for bad requests — never an assumed convention.</summary>
    private static readonly string[] FailurePreference = ["400", "422"];

    public const string UnavailableNote =
        "預期回應無法從 OpenAPI 規格推導（規格未提供可對應的文件回應）。";

    public static (ScenarioResponse? Response, bool Documented) ForSuccess(Operation operation)
    {
        foreach (var code in SuccessPreference)
        {
            if (operation.Responses.TryGetValue(code, out var response))
            {
                return (Derive(int.Parse(code), response), true);
            }
        }

        return (null, false);
    }

    /// <summary>
    /// Attaches an expected failure response ONLY when the specification documents one that
    /// can be responsibly associated with invalid input: an exact 400/422 or the spec's own
    /// <c>default</c> response. Wildcards (4XX) and undocumented outcomes attach nothing.
    /// </summary>
    public static (ScenarioResponse? Response, bool Documented) ForFailure(Operation operation)
    {
        foreach (var code in FailurePreference)
        {
            if (operation.Responses.TryGetValue(code, out var response))
            {
                return (Derive(int.Parse(code), response), true);
            }
        }

        if (operation.Responses.TryGetValue("default", out var defaultResponse) ||
            operation.Responses.TryGetValue("DEFAULT", out defaultResponse))
        {
            return (Derive(null, defaultResponse), true);
        }

        return (null, false);
    }

    internal static ScenarioResponse? Derive(int? status, ResponseInfo response)
    {
        var headers = new List<(string Name, string Value)>();
        foreach (var header in response.Headers.OrderBy(h => h.Name, StringComparer.Ordinal))
        {
            var value = header.Schema is null ? "sample" : SampleValueFactory.ParameterSampleText(
                new ParameterInfo { Name = header.Name, In = ParameterLocation.Header, Required = header.Required, Schema = header.Schema });
            headers.Add((header.Name, value));
        }

        // 1. Explicit media-type example wins over everything derived from the schema.
        foreach (var pair in response.MediaExamples.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (pair.Value is not null)
            {
                return new ScenarioResponse(status, StatusTextFor(status), TypeOf(pair.Key), Render(pair.Value, pair.Key), headers);
            }
        }

        if (response.Content.Count == 0)
        {
            // No content declared: 204-style empty responses stay empty (accurate, no JSON invented).
            return new ScenarioResponse(status, StatusTextFor(status), "text", null, headers);
        }

        foreach (var pair in response.Content.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            var contentType = pair.Key;
            var type = TypeOf(contentType);

            // 2. Schema-level example → 3. examples[0] → default → 4. deterministic sample.
            JsonNode? value = null;
            if (pair.Value.TryGetKeyword("example", out var explicitExample) && explicitExample is not null)
            {
                value = explicitExample.DeepClone();
            }
            else if (pair.Value.TryGetKeyword("examples", out var many))
            {
                if (many is JsonArray arr && arr.Count > 0 && arr[0] is { } first)
                {
                    value = first.DeepClone();
                }
            }
            else if (pair.Value.TryGetKeyword("default", out var def) && def is not null)
            {
                value = def.DeepClone();
            }

            if (value is null && type != "binary")
            {
                value = SampleValueFactory.Sample(pair.Value, SampleValueFactory.Depth.Full, level: 2);
            }

            var data = type == "binary"
                ? null // never fabricate binary payloads
                : value is null ? null : Render(value, contentType);
            return new ScenarioResponse(status, StatusTextFor(status), type, data, headers);
        }

        return null;
    }

    private static string Render(JsonNode node, string contentType)
    {
        if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            return node.ToJsonString(Indented);
        }

        return node is JsonValue v && v.TryGetValue<string>(out var s) ? s : node.ToJsonString();
    }

    private static string TypeOf(string contentType) => contentType.ToLowerInvariant() switch
    {
        _ when contentType.Contains("json", StringComparison.OrdinalIgnoreCase) => "json",
        _ when contentType.Contains("xml", StringComparison.OrdinalIgnoreCase) => "xml",
        _ when contentType.Contains("html", StringComparison.OrdinalIgnoreCase) => "html",
        _ when contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) => "text",
        _ => "binary",
    };

    private static string? StatusTextFor(int? status) => status switch
    {
        200 => "OK",
        201 => "Created",
        202 => "Accepted",
        204 => "No Content",
        400 => "Bad Request",
        422 => "Unprocessable Entity",
        _ => null,
    };

    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
