using System.Text.Json.Nodes;
using OpenApiSentinel.Core;
using OpenApiSentinel.OpenApi.Model;

namespace OpenApiSentinel.OpenApi;

/// <summary>
/// Converts a parsed, reference-resolved JSON document into the normalized
/// <see cref="ApiDocument"/> model. Handles Swagger 2.0, OpenAPI 3.0, 3.1 and 3.2,
/// deliberately normalizing their differing semantics.
/// </summary>
public static class OpenApiNormalizer
{
    private static readonly string[] HttpOperationKeys =
        ["get", "put", "post", "delete", "options", "head", "patch", "trace"];

    public static ApiDocument Normalize(JsonNode root)
    {
        if (root is not JsonObject doc)
            throw new SpecParseException("Specification root must be an object.");

        var spec = DetectVersion(doc);
        var info = doc["info"] as JsonObject;
        var paths = NormalizePaths(doc["paths"] as JsonObject, spec);
        var webhooks = NormalizeWebhooks(doc, spec);
        var securitySchemes = NormalizeSecuritySchemes(doc, spec);
        var globalSecurity = ParseSecurityRequirements(doc["security"], spec);

        return new ApiDocument
        {
            Version = spec,
            RawVersion = spec == SpecVersion.Swagger20 ? "2.0" : doc["openapi"]?.GetValue<string>(),
            Title = info?["title"]?.GetValue<string>() ?? "",
            InfoVersion = info?["version"]?.GetValue<string>() ?? "",
            Servers = NormalizeServers(doc, spec),
            Paths = paths,
            Webhooks = webhooks,
            SecuritySchemes = securitySchemes,
            GlobalSecurity = globalSecurity,
        };
    }

    public static SpecVersion DetectVersion(JsonObject doc)
    {
        if (doc.TryGetPropertyValue("swagger", out var swagger) &&
            swagger is JsonValue sv && sv.TryGetValue<string>(out var s) && s.StartsWith("2.", StringComparison.Ordinal))
            return SpecVersion.Swagger20;

        if (doc.TryGetPropertyValue("openapi", out var oa) && oa is JsonValue ov &&
            ov.TryGetValue<string>(out var v))
        {
            if (v.StartsWith("3.0", StringComparison.Ordinal)) return SpecVersion.OpenApi30;
            if (v.StartsWith("3.1", StringComparison.Ordinal)) return SpecVersion.OpenApi31;
            if (v.StartsWith("3.2", StringComparison.Ordinal)) return SpecVersion.OpenApi32;
            if (v.StartsWith("3.", StringComparison.Ordinal))
                return SpecVersion.OpenApi31; // future 3.x: closest known semantics
            throw new SpecParseException(
                $"Unsupported specification version '{v}'. Supported: Swagger 2.0, OpenAPI 3.0.x, 3.1.x, 3.2.x.");
        }

        throw new SpecParseException(
            "Missing 'openapi' or 'swagger' version field. This does not look like an OpenAPI document.");
    }

    // ---------------- servers ----------------

    private static List<ServerInfo> NormalizeServers(JsonObject doc, SpecVersion spec)
    {
        var result = new List<ServerInfo>();
        if (spec == SpecVersion.Swagger20)
        {
            var host = doc["host"]?.GetValue<string>() ?? "localhost";
            var basePath = doc["basePath"]?.GetValue<string>() ?? "/";
            var schemes = doc["schemes"] as JsonArray;
            if (schemes is { Count: > 0 })
                foreach (var s in schemes)
                    if (s?.GetValue<string>() is { } scheme)
                        result.Add(new ServerInfo($"{scheme}://{host}{basePath}"));
            if (result.Count == 0)
                result.Add(new ServerInfo($"https://{host}{basePath}"));
            return result;
        }

        if (doc["servers"] is JsonArray arr)
            foreach (var s in arr)
                if (s is JsonObject so && so["url"]?.GetValue<string>() is { } url)
                    result.Add(new ServerInfo(url));
        return result;
    }

    // ---------------- paths ----------------

    private static Dictionary<string, PathItem> NormalizePaths(JsonObject? paths, SpecVersion spec)
    {
        var result = new Dictionary<string, PathItem>(StringComparer.Ordinal);
        if (paths is null) return result;

        foreach (var (pathKey, pathValue) in paths)
        {
            if (pathKey.StartsWith("x-", StringComparison.Ordinal)) continue;
            if (pathValue is not JsonObject pathObj) continue;
            result[pathKey] = NormalizePathItem(pathKey, pathObj, spec);
        }
        return result;
    }

    private static Dictionary<string, WebhookItem> NormalizeWebhooks(JsonObject doc, SpecVersion spec)
    {
        var result = new Dictionary<string, WebhookItem>(StringComparer.Ordinal);
        if (!spec.Is31OrLater()) return result;
        if (doc["webhooks"] is not JsonObject webhooks) return result;

        foreach (var (name, wh) in webhooks)
        {
            if (wh is not JsonObject obj) continue;
            var ops = new Dictionary<HttpMethodKind, Operation>();
            foreach (var key in HttpOperationKeys)
                if (obj[key] is JsonObject opObj)
                {
                    var method = HttpMethodKindExtensions.ParseHttpMethod(key);
                    ops[method] = NormalizeOperation(opObj, $"<webhook:{name}>", method, [], spec);
                }
            result[name] = new WebhookItem { Name = name, Operations = ops };
        }
        return result;
    }

    private static PathItem NormalizePathItem(string path, JsonObject pathObj, SpecVersion spec)
    {
        var sharedParams = ParseParameterList(pathObj["parameters"], spec);
        var operations = new Dictionary<HttpMethodKind, Operation>();

        foreach (var key in HttpOperationKeys)
        {
            if (pathObj[key] is not JsonObject opObj) continue;
            var method = HttpMethodKindExtensions.ParseHttpMethod(key);
            operations[method] = NormalizeOperation(opObj, path, method, sharedParams, spec);
        }

        var deprecated = pathObj["deprecated"] is JsonValue dv && dv.TryGetValue<bool>(out var db) && db;

        return new PathItem { Path = path, Operations = operations, SharedParameters = sharedParams, Deprecated = deprecated };
    }

    private static Operation NormalizeOperation(
        JsonObject op, string path, HttpMethodKind method, IReadOnlyList<ParameterInfo> sharedParams, SpecVersion spec)
    {
        var parameters = MergeParameters(sharedParams, ParseParameterList(op["parameters"], spec));

        RequestBodyInfo? requestBody = null;
        if (spec == SpecVersion.Swagger20)
            requestBody = ExtractSwagger20RequestBody(parameters, op);

        requestBody ??= NormalizeRequestBody(op["requestBody"], spec);

        var responses = NormalizeResponses(op["responses"] as JsonObject, spec);

        var tags = new List<string>();
        if (op["tags"] is JsonArray tagArr)
            foreach (var t in tagArr)
                if (t?.GetValue<string>() is { } tag) tags.Add(tag);

        return new Operation
        {
            Method = method,
            Path = path,
            OperationId = op["operationId"]?.GetValue<string>(),
            Summary = op["summary"]?.GetValue<string>(),
            Deprecated = op["deprecated"] is JsonValue dv && dv.TryGetValue<bool>(out var d) && d,
            Tags = tags,
            Parameters = parameters.Where(p => p.BodySchema is null).ToList(),
            RequestBody = requestBody,
            Responses = responses,
            Security = ParseSecurityRequirements(op["security"], spec),
            Callbacks = ParseCallbacks(op["callbacks"] as JsonObject, path, method, spec),
        };
    }

    /// <summary>
    /// Parses operation-level callbacks (3.0+). Each named callback maps runtime
    /// expressions to path items; their operations are normalized like real endpoints
    /// under a synthetic display path so every downstream diff (schemas, parameters,
    /// security, nested callbacks) applies unchanged. Nested callbacks recurse naturally.
    /// </summary>
    private static List<CallbackInfo> ParseCallbacks(
        JsonObject? callbacks, string ownerPath, HttpMethodKind ownerMethod, SpecVersion spec)
    {
        var result = new List<CallbackInfo>();
        if (callbacks is null) return result;

        foreach (var (name, cbNode) in callbacks)
        {
            if (name.StartsWith("x-", StringComparison.Ordinal)) continue;
            if (cbNode is not JsonObject expressions) continue;

            var exprMap = new Dictionary<string, IReadOnlyDictionary<HttpMethodKind, Operation>>(StringComparer.Ordinal);
            foreach (var (expression, itemNode) in expressions)
            {
                if (itemNode is not JsonObject itemObj) continue;
                var ops = new Dictionary<HttpMethodKind, Operation>();
                var displayPath = $"<callback:{ownerMethod.ToHttpString()} {ownerPath}:{name}>";
                foreach (var key in HttpOperationKeys)
                    if (itemObj[key] is JsonObject opObj)
                    {
                        var m = HttpMethodKindExtensions.ParseHttpMethod(key);
                        ops[m] = NormalizeOperation(opObj, displayPath, m, [], spec);
                    }
                exprMap[expression] = ops;
            }

            result.Add(new CallbackInfo { Name = name, Expressions = exprMap });
        }

        return result.OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// In Swagger 2.0 a single `body` parameter IS the request body; formData parameters form it too.
    /// They are removed from the parameter list and represented as RequestBodyInfo.
    /// </summary>
    private static RequestBodyInfo? ExtractSwagger20RequestBody(List<ParameterInfo> parameters, JsonObject op)
    {
        var bodyParam = parameters.FirstOrDefault(p => p.BodySchema is not null && p.ContentType == "__body__");
        List<ParameterInfo>? formData = null;

        if (bodyParam is null)
        {
            formData = parameters.Where(p => p.ContentType == "__formData__").ToList();
            if (formData.Count == 0) return null;
        }

        var consumes = new List<string>();
        if (op["consumes"] is JsonArray ca)
            foreach (var c in ca)
                if (c?.GetValue<string>() is { } ct) consumes.Add(ct);
        if (consumes.Count == 0 && formData != null)
            consumes.Add("application/x-www-form-urlencoded");
        if (consumes.Count == 0)
            consumes.Add("application/json");

        SchemaInfo bodySchema;
        if (bodyParam is not null)
        {
            parameters.Remove(bodyParam);
            bodySchema = bodyParam.BodySchema!;
        }
        else
        {
            foreach (var fp in formData!) parameters.Remove(fp);
            var props = new JsonObject();
            var required = new JsonArray();
            foreach (var fp in formData!)
            {
                var propSchema = fp.Schema?.Node.DeepClone() ?? new JsonObject();
                props[fp.Name] = propSchema;
                if (fp.Required) required.Add(fp.Name);
            }
            bodySchema = new SchemaInfo
            {
                Node = new JsonObject { ["type"] = "object", ["properties"] = props, ["required"] = required },
                Spec = SpecVersion.Swagger20,
            };
        }

        var content = new Dictionary<string, SchemaInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var ct in consumes.Distinct(StringComparer.OrdinalIgnoreCase))
            content[ct] = bodySchema;

        return new RequestBodyInfo
        {
            Required = bodyParam?.Required ?? false,
            Content = content,
        };
    }

    // ---------------- parameters ----------------

    private static List<ParameterInfo> ParseParameterList(JsonNode? node, SpecVersion spec)
    {
        var list = new List<ParameterInfo>();
        if (node is not JsonArray arr) return list;

        foreach (var p in arr)
        {
            if (p is not JsonObject po) continue;

            // A 3.x parameter may be a reference already resolved by RefResolver; resolved objects are inline now.

            string name = po["name"]?.GetValue<string>() ?? "";
            string locationRaw = po["in"]?.GetValue<string>() ?? "query";
            if (!Enum.TryParse<ParameterLocation>(locationRaw, ignoreCase: true, out var location))
                location = ParameterLocation.Query;

            bool required = po["required"] is JsonValue rv && rv.TryGetValue<bool>(out var r) && r;
            if (location == ParameterLocation.Path) required = true;

            bool deprecated = po["deprecated"] is JsonValue dv && dv.TryGetValue<bool>(out var d) && d;

            SchemaInfo? schema;
            string? marker = null;

            if (spec == SpecVersion.Swagger20)
            {
                var paramKind = locationRaw;
                if (paramKind == "body")
                {
                    schema = MakeSchema(po["schema"], spec);
                    marker = "__body__";
                    location = ParameterLocation.Query; // placeholder, removed later
                    list.Add(new ParameterInfo
                    {
                        Name = name,
                        In = location,
                        Required = required,
                        Deprecated = deprecated,
                        BodySchema = schema,
                        ContentType = marker,
                    });
                    continue;
                }
                if (paramKind == "formData")
                {
                    marker = "__formData__";
                    schema = MakeSchema(po, spec); // 2.0 formData params carry type/items inline
                    list.Add(new ParameterInfo
                    {
                        Name = name,
                        In = ParameterLocation.Query,
                        Required = required,
                        Deprecated = deprecated,
                        Schema = schema,
                        ContentType = marker,
                    });
                    continue;
                }
                schema = MakeSchema(po, spec); // non-body 2.0 params carry type/format inline
            }
            else
            {
                schema = MakeSchema(po["schema"], spec);
                if (schema is null && po["content"] is JsonObject contentObj)
                {
                    // 3.x content-parameter: take first media type's schema.
                    foreach (var (_, media) in contentObj)
                    {
                        if (media is JsonObject mediaObj)
                            schema = MakeSchema(mediaObj["schema"], spec);
                        break;
                    }
                }
            }

            list.Add(new ParameterInfo
            {
                Name = name,
                In = location,
                Required = required,
                Deprecated = deprecated,
                Schema = schema,
                Style = spec == SpecVersion.Swagger20
                    ? CollectionFormatStyle(po, schema)
                    : po["style"]?.GetValue<string>(),
                Explode = po["explode"] is JsonValue ev && ev.TryGetValue<bool>(out var ex) ? ex : null,
            });
        }

        return list;
    }

    /// <summary>
    /// Swagger 2.0 serializes array parameters per <c>collectionFormat</c> (csv/multi/ssv/pipes);
    /// the spec default is csv. Normalizing absent→csv for arrays only means explicit-vs-default
    /// restyling never fires, while real wire-format changes (multi→csv) route to CG2013.
    /// Non-array parameters ignore collectionFormat entirely — it has no effect on them.
    /// </summary>
    private static string? CollectionFormatStyle(JsonObject po, SchemaInfo? schema)
    {
        if (schema?.PrimaryType() != "array") return null;
        return po["collectionFormat"] is JsonValue cf && cf.TryGetValue<string>(out var s) ? s : "csv";
    }

    /// <summary>Merges path-level and operation-level parameters; operation wins on (name, in).</summary>
    private static List<ParameterInfo> MergeParameters(
        IReadOnlyList<ParameterInfo> shared, List<ParameterInfo> operation)
    {
        var merged = new List<ParameterInfo>(operation);
        foreach (var sp in shared)
        {
            var overridden = operation.Any(op =>
                op.Name.Equals(sp.Name, StringComparison.Ordinal) && op.In == sp.In);
            if (!overridden) merged.Add(sp);
        }
        return merged;
    }

    // ---------------- request bodies ----------------

    private static RequestBodyInfo? NormalizeRequestBody(JsonNode? node, SpecVersion spec)
    {
        if (node is not JsonObject obj) return null;

        var content = new Dictionary<string, SchemaInfo>(StringComparer.OrdinalIgnoreCase);
        if (obj["content"] is JsonObject co)
        {
            foreach (var (ct, media) in co)
            {
                if (ct.StartsWith("x-", StringComparison.Ordinal)) continue;
                if (media is JsonObject mo)
                {
                    var schema = MakeSchema(mo["schema"], spec);
                    if (schema is not null) content[ct] = schema;
                }
            }
        }

        return new RequestBodyInfo
        {
            Required = obj["required"] is JsonValue rv && rv.TryGetValue<bool>(out var r) && r,
            Content = content,
        };
    }

    // ---------------- responses ----------------

    private static Dictionary<string, ResponseInfo> NormalizeResponses(JsonObject? responses, SpecVersion spec)
    {
        var result = new Dictionary<string, ResponseInfo>(StringComparer.Ordinal);
        if (responses is null) return result;

        foreach (var (statusKey, resp) in responses)
        {
            if (statusKey.StartsWith("x-", StringComparison.Ordinal)) continue;
            if (resp is not JsonObject ro) continue;

            var content = new Dictionary<string, SchemaInfo>(StringComparer.OrdinalIgnoreCase);
            if (spec == SpecVersion.Swagger20)
            {
                var produces = new List<string> { "application/json" };
                if (ro["produces"] is JsonArray pa)
                {
                    produces.Clear();
                    foreach (var p in pa)
                        if (p?.GetValue<string>() is { } ct) produces.Add(ct);
                }
                var schema = MakeSchema(ro["schema"], spec);
                if (schema is not null)
                    foreach (var ct in produces)
                        content[ct] = schema;
            }
            else if (ro["content"] is JsonObject co)
            {
                foreach (var (ct, media) in co)
                {
                    if (ct.StartsWith("x-", StringComparison.Ordinal)) continue;
                    if (media is JsonObject mo)
                    {
                        var schema = MakeSchema(mo["schema"], spec);
                        if (schema is not null) content[ct] = schema;
                    }
                }
            }

            var headers = new List<HeaderInfo>();
            if (ro["headers"] is JsonObject ho)
            {
                foreach (var (hName, hVal) in ho)
                {
                    if (hVal is not JsonObject hObj) continue;
                    SchemaInfo? hSchema;
                    bool hRequired;
                    if (spec == SpecVersion.Swagger20)
                    {
                        hSchema = MakeSchema(hObj, spec);
                        hRequired = false; // 2.0 headers have no required flag
                    }
                    else
                    {
                        hSchema = MakeSchema(hObj["schema"], spec);
                        hRequired = hObj["required"] is JsonValue hv && hv.TryGetValue<bool>(out var hr) && hr;
                    }
                    headers.Add(new HeaderInfo { Name = hName, Required = hRequired, Schema = hSchema });
                }
            }

            result[statusKey] = new ResponseInfo
            {
                StatusCodeKey = statusKey.ToUpperInvariant(),
                Description = ro["description"]?.GetValue<string>(),
                Content = content,
                Headers = headers,
            };
        }
        return result;
    }

    // ---------------- security ----------------

    public static IReadOnlyList<SecurityRequirementInfo>? ParseSecurityRequirements(JsonNode? node, SpecVersion spec)
    {
        if (node is null) return null; // absent: inherit / none depending on level
        if (node is not JsonArray arr) return [];

        var requirements = new List<SecurityRequirementInfo>();
        foreach (var req in arr)
        {
            if (req is not JsonObject ro) continue;
            foreach (var (schemeName, scopesNode) in ro)
            {
                var scopes = new HashSet<string>(StringComparer.Ordinal);
                if (scopesNode is JsonArray scopeArr)
                    foreach (var s in scopeArr)
                        if (s?.GetValue<string>() is { } scope) scopes.Add(scope);
                requirements.Add(new SecurityRequirementInfo { Scheme = schemeName, Scopes = scopes });
            }
        }
        return requirements;
    }

    private static Dictionary<string, SecuritySchemeInfo> NormalizeSecuritySchemes(JsonObject doc, SpecVersion spec)
    {
        var schemesNode = spec == SpecVersion.Swagger20 ? doc["securityDefinitions"] : (doc["components"] as JsonObject)?["securitySchemes"];
        var result = new Dictionary<string, SecuritySchemeInfo>(StringComparer.Ordinal);
        if (schemesNode is not JsonObject obj) return result;

        foreach (var (name, node) in obj)
        {
            if (node is not JsonObject so) continue;
            var rawType = so["type"]?.GetValue<string>() ?? "";
            var type = rawType.ToLowerInvariant() switch
            {
                "apikey" => SecuritySchemeType.ApiKey,
                "http" => SecuritySchemeType.Http,
                "oauth2" => SecuritySchemeType.OAuth2,
                "openIdConnect" => SecuritySchemeType.OpenIdConnect,
                "basic" => SecuritySchemeType.Basic,
                _ => SecuritySchemeType.Http,
            };

            var flows = new Dictionary<string, OAuthFlow>(StringComparer.Ordinal);
            if (so["flows"] is JsonObject fo)
            {
                foreach (var (flowName, flowNode) in fo)
                {
                    if (flowNode is not JsonObject flo) continue;
                    var flowScopes = new HashSet<string>(StringComparer.Ordinal);
                    if (flo["scopes"] is JsonObject sco)
                        foreach (var s in sco)
                            flowScopes.Add(s.Key);
                    flows[flowName] = new OAuthFlow(
                        flo["authorizationUrl"]?.GetValue<string>(),
                        flo["tokenUrl"]?.GetValue<string>(),
                        flo["refreshUrl"]?.GetValue<string>(),
                        flowScopes);
                }
            }

            result[name] = new SecuritySchemeInfo
            {
                Name = name,
                KeyName = so["name"]?.GetValue<string>(),
                Type = type,
                RawType = rawType,
                In = so["in"]?.GetValue<string>(),
                Scheme = so["scheme"]?.GetValue<string>(),
                BearerFormat = so["bearerFormat"]?.GetValue<string>(),
                OpenIdConnectUrl = so["openIdConnectUrl"]?.GetValue<string>(),
                Flows = flows,
                Deprecated = so["deprecated"] is JsonValue dv && dv.TryGetValue<bool>(out var d) && d,
            };
        }
        return result;
    }

    // ---------------- schemas ----------------

    private static SchemaInfo? MakeSchema(JsonNode? node, SpecVersion spec)
    {
        if (node is null) return null;
        return new SchemaInfo { Node = node, Spec = spec };
    }
}
