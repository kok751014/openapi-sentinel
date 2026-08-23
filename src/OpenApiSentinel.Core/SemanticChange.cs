using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace OpenApiSentinel.Core;

/// <summary>HTTP methods understood by the comparison engine.</summary>
public enum HttpMethodKind
{
    None,
    Get,
    Put,
    Post,
    Delete,
    Options,
    Head,
    Patch,
    Trace,
}

public static class HttpMethodKindExtensions
{
    public static string ToHttpString(this HttpMethodKind method) => method switch
    {
        HttpMethodKind.Get => "GET",
        HttpMethodKind.Put => "PUT",
        HttpMethodKind.Post => "POST",
        HttpMethodKind.Delete => "DELETE",
        HttpMethodKind.Options => "OPTIONS",
        HttpMethodKind.Head => "HEAD",
        HttpMethodKind.Patch => "PATCH",
        HttpMethodKind.Trace => "TRACE",
        _ => "",
    };

    /// <summary>Parses an HTTP method name; returns <see cref="HttpMethodKind.None"/> for unknown values.</summary>
    public static HttpMethodKind ParseHttpMethod(string value) =>
        Enum.TryParse<HttpMethodKind>(value.Trim(), ignoreCase: true, out var m) && m != HttpMethodKind.None
            ? m
            : HttpMethodKind.None;
}

/// <summary>Direction of a change with respect to the consumer/provider relationship.</summary>
public enum ApiDirection
{
    /// <summary>Change affects what clients send to the server (parameters, request bodies).</summary>
    Request,
    /// <summary>Change affects what the server returns to clients (responses, headers).</summary>
    Response,
    /// <summary>Change is not direction-specific (endpoints, metadata, security schemes).</summary>
    Neutral,
}

/// <summary>
/// A single semantic change detected between two API contracts. The rules engine maps
/// changes of a given kind to user-facing findings.
/// </summary>
public sealed record SemanticChange
{
    /// <summary>Machine-stable identifier of the structural change, e.g. "endpoint.removed".</summary>
    public required string Kind { get; init; }

    public required FindingCategory Category { get; init; }

    /// <summary>API path the change belongs to (e.g. "/orders/{id}"), or "" for document-level changes.</summary>
    public required string Path { get; init; }

    public HttpMethodKind HttpMethod { get; init; } = HttpMethodKind.None;

    /// <summary>Fine-grained location inside the operation, e.g. "responses.200.schema.properties.status".</summary>
    public string Location { get; init; } = "";

    /// <summary>Direction used for compatibility reasoning.</summary>
    public ApiDirection Direction { get; init; } = ApiDirection.Neutral;

    public string? PropertyName { get; init; }

    public string? ParameterName { get; init; }

    public string? OldValue { get; init; }

    public string? NewValue { get; init; }

    /// <summary>Optional extra detail consumed by specific rules.</summary>
    public IReadOnlyDictionary<string, string> Details { get; init; } = FrozenMap.Empty;

    public static string ValueToString(JsonNode? node) => node is null ? "" : node.ToJsonString(SerializerOptions);

    public static readonly System.Text.Json.JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}

/// <summary>A tiny read-only dictionary optimized for the empty case.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix",
    Justification = "Intentional domain name")]
public sealed class FrozenMap : IReadOnlyDictionary<string, string>
{
    public static readonly FrozenMap Empty = new(new Dictionary<string, string>());

    private readonly Dictionary<string, string> _inner;

    public FrozenMap(Dictionary<string, string> inner) => _inner = inner;

    public string this[string key] => _inner[key];
    public IEnumerable<string> Keys => _inner.Keys;
    public IEnumerable<string> Values => _inner.Values;
    public int Count => _inner.Count;
    public bool ContainsKey(string key) => _inner.ContainsKey(key);
    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _inner.GetEnumerator();
    public bool TryGetValue(string key, out string value) => _inner.TryGetValue(key, out value!);
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _inner.GetEnumerator();
}
