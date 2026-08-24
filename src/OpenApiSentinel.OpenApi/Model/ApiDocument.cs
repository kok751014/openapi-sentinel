using OpenApiSentinel.Core;

namespace OpenApiSentinel.OpenApi.Model;

/// <summary>Supported specification major/minor families.</summary>
public enum SpecVersion
{
    Unknown,
    Swagger20,
    OpenApi30,
    OpenApi31,
    OpenApi32,
}

public static class SpecVersionExtensions
{
    public static bool Is31OrLater(this SpecVersion v) => v is SpecVersion.OpenApi31 or SpecVersion.OpenApi32;

    public static string Display(this SpecVersion v) => v switch
    {
        SpecVersion.Swagger20 => "OpenAPI 2.0 (Swagger)",
        SpecVersion.OpenApi30 => "OpenAPI 3.0",
        SpecVersion.OpenApi31 => "OpenAPI 3.1",
        SpecVersion.OpenApi32 => "OpenAPI 3.2",
        _ => "unknown",
    };
}

/// <summary>A fully normalized API contract used by the comparison engine.</summary>
public sealed class ApiDocument
{
    public required SpecVersion Version { get; init; }

    /// <summary>The raw version string from the document, e.g. "3.1.0".</summary>
    public string? RawVersion { get; init; }

    public string Title { get; init; } = "";

    public string InfoVersion { get; init; } = "";

    public IReadOnlyList<ServerInfo> Servers { get; init; } = [];

    /// <summary>Paths keyed by their templated form, e.g. "/orders/{id}".</summary>
    public required IReadOnlyDictionary<string, PathItem> Paths { get; init; }

    /// <summary>Webhooks (3.1+) keyed by name; compared like paths.</summary>
    public IReadOnlyDictionary<string, WebhookItem> Webhooks { get; init; } =
        new Dictionary<string, WebhookItem>();

    public IReadOnlyDictionary<string, SecuritySchemeInfo> SecuritySchemes { get; init; } =
        new Dictionary<string, SecuritySchemeInfo>();

    /// <summary>Document-level default security requirements; null means "not specified".</summary>
    public IReadOnlyList<SecurityRequirementInfo>? GlobalSecurity { get; init; }
}

public sealed record ServerInfo(string Url);

/// <summary>All operations sharing one path.</summary>
public sealed class PathItem
{
    public required string Path { get; init; }

    public bool Deprecated { get; init; }

    public required IReadOnlyDictionary<HttpMethodKind, Operation> Operations { get; init; }

    /// <summary>Path-level parameters shared by all operations (2.0/3.x).</summary>
    public IReadOnlyList<ParameterInfo> SharedParameters { get; init; } = [];
}

/// <summary>A webhook definition; behaves like a path item for comparison purposes.</summary>
public sealed class WebhookItem
{
    public required string Name { get; init; }
    public required IReadOnlyDictionary<HttpMethodKind, Operation> Operations { get; init; }
}

public sealed class Operation
{
    public required HttpMethodKind Method { get; init; }

    public required string Path { get; init; }

    public string? OperationId { get; init; }

    public string? Summary { get; init; }

    public bool Deprecated { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Merged view: operation parameters with path-item parameters applied underneath.</summary>
    public required IReadOnlyList<ParameterInfo> Parameters { get; init; }

    public RequestBodyInfo? RequestBody { get; init; }

    /// <summary>Responses keyed by status expression ("200", "4XX", "default").</summary>
    public required IReadOnlyDictionary<string, ResponseInfo> Responses { get; init; }

    /// <summary>Operation-level security; null inherits from document global security.</summary>
    public IReadOnlyList<SecurityRequirementInfo>? Security { get; init; }

    /// <summary>Callbacks declared by this operation: out-of-band requests the SERVER will make to CLIENT-provided URLs (3.0+).</summary>
    public IReadOnlyList<CallbackInfo> Callbacks { get; init; } = [];
}

/// <summary>
/// A named callback definition. Each runtime expression / URI template maps to a
/// path item describing the operations the server will invoke on the client.
/// </summary>
public sealed class CallbackInfo
{
    public required string Name { get; init; }

    /// <summary>Runtime expression (e.g. '{$request.body#/callbackUrl}') keyed to its operations.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyDictionary<HttpMethodKind, Operation>> Expressions { get; init; }
}

public enum ParameterLocation
{
    Query,
    Header,
    Path,
    Cookie,
}

public sealed class ParameterInfo
{
    public required string Name { get; init; }

    public required ParameterLocation In { get; init; }

    public required bool Required { get; init; }

    public bool Deprecated { get; init; }

    public bool IsPathParameter => In == ParameterLocation.Path;

    /// <summary>Normalized schema of the parameter value (for 2.0 body parameters this is null; see BodySchema).</summary>
    public SchemaInfo? Schema { get; init; }

    /// <summary>Only for Swagger 2.0 body/formData parameters.</summary>
    public SchemaInfo? BodySchema { get; init; }

    public string? Style { get; init; }

    public bool? Explode { get; init; }

    /// <summary>Content type for 2.0 body params / 3.x content-based params.</summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// All declared media types for 3.x content-based parameters (media type → schema).
    /// Empty unless the parameter used <c>content:</c>. The first media type also populates
    /// <see cref="Schema"/> (and <see cref="PrimaryContentType"/>) for legacy rule routing;
    /// the engine diffs every additional media type separately.
    /// </summary>
    public IReadOnlyDictionary<string, SchemaInfo> Content { get; init; } =
        new Dictionary<string, SchemaInfo>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The media type whose schema backs <see cref="Schema"/> (content params only).</summary>
    public string? PrimaryContentType { get; init; }
}

public sealed class RequestBodyInfo
{
    public required bool Required { get; init; }

    /// <summary>Content types mapped to their schema, e.g. "application/json" -> schema.</summary>
    public required IReadOnlyDictionary<string, SchemaInfo> Content { get; init; }
}

public sealed class ResponseInfo
{
    /// <summary>"200", "default", "2XX" ...</summary>
    public required string StatusCodeKey { get; init; }

    public string? Description { get; init; }

    public required IReadOnlyDictionary<string, SchemaInfo> Content { get; init; }

    public required IReadOnlyList<HeaderInfo> Headers { get; init; }
}

public sealed class HeaderInfo
{
    public required string Name { get; init; }
    public required bool Required { get; init; }
    public SchemaInfo? Schema { get; init; }
}

public sealed class SchemaInfo
{
    /// <summary>Component schema name when this schema was resolved from components; otherwise null.</summary>
    public string? ComponentName { get; init; }

    /// <summary>The ref-resolved JSON node for this schema.</summary>
    public required System.Text.Json.Nodes.JsonNode Node { get; init; }

    public required SpecVersion Spec { get; init; }

    // ---- Semantic accessors (version aware) ----

    private const string NullType = "null";

    /// <summary>
    /// Effective type set. Handles: 2.0/3.0 single "type" + sibling nullable;
    /// 3.1+ "type" as array possibly containing "null".
    /// </summary>
    public IReadOnlySet<string> Types()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (Node is not System.Text.Json.Nodes.JsonObject obj) return (IReadOnlySet<string>)set;

        if (obj.TryGetPropertyValue("type", out var t) && t is not null)
        {
            if (t is System.Text.Json.Nodes.JsonArray arr)
            {
                foreach (var item in arr)
                    if (item?.GetValue<string>() is { } s) set.Add(s);
            }
            else
            {
                set.Add(t.GetValue<string>());
            }
        }

        if (!Spec.Is31OrLater() && obj.TryGetPropertyValue("nullable", out var n))
        {
            var nullable = n is System.Text.Json.Nodes.JsonValue v && v.TryGetValue<bool>(out var b) && b;
            if (nullable && set.Count > 0) set.Add(NullType);
        }

        return (IReadOnlySet<string>)set;
    }

    /// <summary>
    /// Declared types, falling back to JSON-Schema composition semantics when the schema
    /// itself declares no <c>type</c>: allOf intersects the types declared by its branches
    /// (constraint-only mixins do not narrow), anyOf/oneOf union them. Restructuring
    /// <c>{"type": "X"}</c> into an equivalent composition must therefore not register as a
    /// type change, while genuine type differences under compositions stay detectable.
    /// </summary>
    public IReadOnlySet<string> EffectiveTypes()
    {
        var direct = Types();
        if (direct.Count > 0 || Node is not System.Text.Json.Nodes.JsonObject obj) return direct;

        if (obj.TryGetPropertyValue("allOf", out var allNode) &&
            allNode is System.Text.Json.Nodes.JsonArray { Count: > 0 } allBranches)
            return ComposeBranchTypes(allBranches, intersect: true);

        foreach (var keyword in (ReadOnlySpan<string>)["anyOf", "oneOf"])
        {
            if (!obj.TryGetPropertyValue(keyword, out var unionNode) ||
                unionNode is not System.Text.Json.Nodes.JsonArray { Count: > 0 } unionBranches)
                continue;
            return ComposeBranchTypes(unionBranches, intersect: false);
        }

        return direct;
    }

    private HashSet<string> ComposeBranchTypes(System.Text.Json.Nodes.JsonArray branches, bool intersect)
    {
        HashSet<string>? composed = null;
        foreach (var branch in branches)
        {
            if (branch is not System.Text.Json.Nodes.JsonObject branchObj) continue;
            var branchTypes = new SchemaInfo { Node = branchObj, Spec = Spec }.Types();
            if (branchTypes.Count == 0) continue; // a branch without a declared type neither narrows nor widens

            if (composed is null) composed = new HashSet<string>(branchTypes, StringComparer.Ordinal);
            else if (intersect) composed.IntersectWith(branchTypes);
            else composed.UnionWith(branchTypes);
        }

        return composed ?? [];
    }

    public bool IsNullable() => Types().Contains(NullType);

    public string? PrimaryType()
    {
        var types = Types();
        foreach (var t in types) if (t != NullType) return t;
        return types.Count > 0 ? NullType : null;
    }

    public string? Format =>
        (Node as System.Text.Json.Nodes.JsonObject)?.TryGetPropertyValue("format", out var f) == true
            ? f?.GetValue<string>()
            : null;

    public bool TryGetKeyword(string keyword, out System.Text.Json.Nodes.JsonNode? value)
    {
        value = null;
        if (Node is not System.Text.Json.Nodes.JsonObject obj) return false;
        if (!obj.TryGetPropertyValue(keyword, out var v)) return false;
        value = v;
        return true;
    }

    public IReadOnlyList<System.Text.Json.Nodes.JsonNode>? CompositionOf(string keyword)
    {
        if (!TryGetKeyword(keyword, out var v) || v is not System.Text.Json.Nodes.JsonArray arr) return null;
        var list = new List<System.Text.Json.Nodes.JsonNode>(arr.Count);
        foreach (var item in arr)
            if (item is not null) list.Add(item);
        return list;
    }

    public override string ToString() => SemanticChange.ValueToString(Node);

    /// <summary>Creates a child accessor for a named property without materializing unless needed.</summary>
    public SchemaInfo? PropertyOrNull(string name)
    {
        if (Node is not System.Text.Json.Nodes.JsonObject obj) return null;
        if (!obj.TryGetPropertyValue("properties", out var props) || props is not System.Text.Json.Nodes.JsonObject po) return null;
        if (!po.TryGetPropertyValue(name, out var p) || p is null) return null;
        return new SchemaInfo { Node = p, Spec = Spec };
    }
}
