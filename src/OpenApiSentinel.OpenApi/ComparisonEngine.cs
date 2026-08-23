using OpenApiSentinel.Core;
using OpenApiSentinel.OpenApi.Model;

namespace OpenApiSentinel.OpenApi;

/// <summary>
/// Compares two normalized API documents and produces the full stream of semantic changes.
/// Deterministic: iteration order is sorted, so identical inputs always yield identical outputs.
/// </summary>
public sealed class ComparisonEngine
{
    private static readonly HttpMethodKind[] MethodOrder =
    [
        HttpMethodKind.Get, HttpMethodKind.Post, HttpMethodKind.Put,
        HttpMethodKind.Patch, HttpMethodKind.Delete, HttpMethodKind.Head,
        HttpMethodKind.Options, HttpMethodKind.Trace,
    ];

    public IReadOnlyList<SemanticChange> Compare(ApiDocument oldDoc, ApiDocument newDoc)
    {
        var changes = new List<SemanticChange>();

        CompareServers(oldDoc, newDoc, changes);
        CompareMetadata(oldDoc, newDoc, changes);
        CompareSecuritySchemes(oldDoc.SecuritySchemes, newDoc.SecuritySchemes, changes);
        ComparePathCollection(oldDoc.Paths, newDoc.Paths, oldDoc.GlobalSecurity, newDoc.GlobalSecurity, changes);
        CompareWebhooks(oldDoc.Webhooks, newDoc.Webhooks, oldDoc.GlobalSecurity, newDoc.GlobalSecurity, changes);

        return changes;
    }

    // ---------------- metadata ----------------

    private static void CompareServers(ApiDocument oldDoc, ApiDocument newDoc, List<SemanticChange> changes)
    {
        var oldUrls = oldDoc.Servers.Select(s => s.Url).ToHashSet(StringComparer.Ordinal);
        var newUrls = newDoc.Servers.Select(s => s.Url).ToHashSet(StringComparer.Ordinal);
        if (oldUrls.Count > 0 || newUrls.Count > 0)
        {
            if (!oldUrls.SetEquals(newUrls))
            {
                changes.Add(new SemanticChange
                {
                    Kind = ChangeKinds.ServerChanged,
                    Category = FindingCategory.Metadata,
                    Path = "",
                    OldValue = string.Join(", ", oldUrls.OrderBy(x => x, StringComparer.Ordinal)),
                    NewValue = string.Join(", ", newUrls.OrderBy(x => x, StringComparer.Ordinal)),
                });
            }
        }
    }

    private static void CompareMetadata(ApiDocument oldDoc, ApiDocument newDoc, List<SemanticChange> changes)
    {
        if (!string.Equals(oldDoc.InfoVersion, newDoc.InfoVersion, StringComparison.Ordinal) &&
            (oldDoc.InfoVersion.Length > 0 || newDoc.InfoVersion.Length > 0))
        {
            changes.Add(new SemanticChange
            {
                Kind = ChangeKinds.InfoVersionChanged,
                Category = FindingCategory.Metadata,
                Path = "",
                OldValue = oldDoc.InfoVersion,
                NewValue = newDoc.InfoVersion,
            });
        }
    }

    // ---------------- paths ----------------

    private void ComparePathCollection(
        IReadOnlyDictionary<string, PathItem> oldPaths,
        IReadOnlyDictionary<string, PathItem> newPaths,
        IReadOnlyList<SecurityRequirementInfo>? oldGlobalSecurity,
        IReadOnlyList<SecurityRequirementInfo>? newGlobalSecurity,
        List<SemanticChange> changes)
    {
        foreach (var key in oldPaths.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (!newPaths.TryGetValue(key, out var newPathItem))
            {
                changes.Add(new SemanticChange
                {
                    Kind = ChangeKinds.EndpointRemoved,
                    Category = FindingCategory.Endpoint,
                    Path = key,
                });
                continue;
            }
            ComparePathItems(oldPaths[key], newPathItem, oldGlobalSecurity, newGlobalSecurity, changes);
        }

        foreach (var key in newPaths.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (!oldPaths.ContainsKey(key))
            {
                changes.Add(new SemanticChange
                {
                    Kind = ChangeKinds.EndpointAdded,
                    Category = FindingCategory.Endpoint,
                    Path = key,
                });
            }
        }
    }

    private void CompareWebhooks(
        IReadOnlyDictionary<string, WebhookItem> oldHooks,
        IReadOnlyDictionary<string, WebhookItem> newHooks,
        IReadOnlyList<SecurityRequirementInfo>? oldGlobalSecurity,
        IReadOnlyList<SecurityRequirementInfo>? newGlobalSecurity,
        List<SemanticChange> changes)
    {
        var allNames = oldHooks.Keys.Union(newHooks.Keys).OrderBy(k => k, StringComparer.Ordinal);
        foreach (var name in allNames)
        {
            var existsOld = oldHooks.TryGetValue(name, out var oldHook);
            var existsNew = newHooks.TryGetValue(name, out var newHook);

            if (!existsOld && existsNew && newHook!.Operations.Count > 0)
            {
                changes.Add(new SemanticChange
                {
                    Kind = ChangeKinds.OperationAdded,
                    Category = FindingCategory.Endpoint,
                    Path = $"<webhook:{name}>",
                });
            }
            else if (existsOld && !existsNew && oldHook!.Operations.Count > 0)
            {
                changes.Add(new SemanticChange
                {
                    Kind = ChangeKinds.OperationRemoved,
                    Category = FindingCategory.Endpoint,
                    Path = $"webhook:{name}",
                });
            }
            else if (existsOld && existsNew)
            {
                // Webhook content changes matter as much as endpoint changes:
                // diff every method pair inside the existing webhook.
                foreach (var method in MethodOrder)
                {
                    var hasOld = oldHook!.Operations.TryGetValue(method, out var oldOp);
                    var hasNew = newHook!.Operations.TryGetValue(method, out var newOp);
                    if (hasOld && hasNew)
                        CompareOperations(oldOp!, newOp!, oldGlobalSecurity, newGlobalSecurity, changes);
                    else if (hasOld && !hasNew)
                        changes.Add(new SemanticChange
                        {
                            Kind = ChangeKinds.OperationRemoved,
                            Category = FindingCategory.Endpoint,
                            Path = $"<webhook:{name}>",
                            HttpMethod = method,
                        });
                    else if (!hasOld && hasNew)
                        changes.Add(new SemanticChange
                        {
                            Kind = ChangeKinds.OperationAdded,
                            Category = FindingCategory.Endpoint,
                            Path = $"<webhook:{name}>",
                            HttpMethod = method,
                        });
                }
            }
        }
    }

    private void ComparePathItems(
        PathItem oldItem, PathItem newItem,
        IReadOnlyList<SecurityRequirementInfo>? oldGlobalSecurity,
        IReadOnlyList<SecurityRequirementInfo>? newGlobalSecurity,
        List<SemanticChange> changes)
    {
        var path = oldItem.Path;

        foreach (var method in MethodOrder)
        {
            var hasOld = oldItem.Operations.TryGetValue(method, out var oldOp);
            var hasNew = newItem.Operations.TryGetValue(method, out var newOp);

            if (hasOld && !hasNew)
            {
                changes.Add(new SemanticChange
                {
                    Kind = ChangeKinds.OperationRemoved,
                    Category = FindingCategory.Endpoint,
                    Path = path,
                    HttpMethod = method,
                });
            }
            else if (!hasOld && hasNew)
            {
                changes.Add(new SemanticChange
                {
                    Kind = ChangeKinds.OperationAdded,
                    Category = FindingCategory.Endpoint,
                    Path = path,
                    HttpMethod = method,
                });
            }
            else if (hasOld && hasNew)
            {
                CompareOperations(oldOp!, newOp!, oldGlobalSecurity, newGlobalSecurity, changes);
            }
        }
    }

    private void CompareOperations(
        Operation oldOp, Operation newOp,
        IReadOnlyList<SecurityRequirementInfo>? oldGlobalSecurity,
        IReadOnlyList<SecurityRequirementInfo>? newGlobalSecurity,
        List<SemanticChange> changes)
    {
        string path = oldOp.Path;
        var method = oldOp.Method;
        string locPrefix = $"{method.ToHttpString()} {path}";

        // operationId
        if (!string.Equals(oldOp.OperationId, newOp.OperationId, StringComparison.Ordinal))
        {
            changes.Add(new SemanticChange
            {
                Kind = oldOp.OperationId is null ? ChangeKinds.OperationIdAdded
                    : newOp.OperationId is null ? ChangeKinds.OperationIdRemoved
                    : ChangeKinds.OperationIdChanged,
                Category = FindingCategory.Metadata,
                Path = path,
                HttpMethod = method,
                Location = "operationId",
                OldValue = oldOp.OperationId,
                NewValue = newOp.OperationId,
            });
        }

        // deprecated
        if (oldOp.Deprecated != newOp.Deprecated)
        {
            changes.Add(new SemanticChange
            {
                Kind = ChangeKinds.OperationDeprecatedToggled,
                Category = FindingCategory.Endpoint,
                Path = path,
                HttpMethod = method,
                Location = "deprecated",
                OldValue = oldOp.Deprecated ? "true" : "false",
                NewValue = newOp.Deprecated ? "true" : "false",
            });
        }

        // parameters
        CompareParameters(oldOp.Parameters, newOp.Parameters, path, method, locPrefix, changes);

        // request body
        CompareRequestBody(oldOp.RequestBody, newOp.RequestBody, path, method, locPrefix, changes);

        // responses
        CompareResponses(oldOp.Responses, newOp.Responses, path, method, locPrefix, changes);

        // security
        CompareEffectiveSecurity(oldOp.Security ?? oldGlobalSecurity, newOp.Security ?? newGlobalSecurity, path, method, locPrefix, changes);
    }

    // ---------------- parameters ----------------

    private void CompareParameters(
        IReadOnlyList<ParameterInfo> oldParams, IReadOnlyList<ParameterInfo> newParams,
        string path, HttpMethodKind method, string locPrefix, List<SemanticChange> changes)
    {
        static string Key(ParameterInfo p) => $"{p.In}:{p.Name}";
        var oldByKey = oldParams.GroupBy(Key).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var newByKey = newParams.GroupBy(Key).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var key in oldByKey.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (!newByKey.ContainsKey(key))
            {
                changes.Add(new SemanticChange
                {
                    Kind = ChangeKinds.ParameterRemoved,
                    Category = FindingCategory.Parameter,
                    Path = path,
                    HttpMethod = method,
                    Location = $"{locPrefix} parameters.{oldByKey[key].Name}",
                    ParameterName = oldByKey[key].Name,
                    Direction = ApiDirection.Request,
                    Details = new FrozenMap(new Dictionary<string, string>
                    {
                        ["in"] = oldByKey[key].In.ToString().ToLowerInvariant(),
                        ["required"] = oldByKey[key].Required ? "true" : "false",
                    }),
                });
            }
        }

        foreach (var key in newByKey.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (!oldByKey.TryGetValue(key, out var oldP))
            {
                var newP = newByKey[key];
                changes.Add(new SemanticChange
                {
                    Kind = ChangeKinds.ParameterAdded,
                    Category = FindingCategory.Parameter,
                    Path = path,
                    HttpMethod = method,
                    Location = $"{locPrefix} parameters.{newP.Name}",
                    ParameterName = newP.Name,
                    Direction = ApiDirection.Request,
                    Details = new FrozenMap(new Dictionary<string, string>
                    {
                        ["in"] = newP.In.ToString().ToLowerInvariant(),
                        ["required"] = newP.Required ? "true" : "false",
                    }),
                });
                continue;
            }

            CompareParameterPair(oldP, newByKey[key], path, method, locPrefix, changes);
        }
    }

    private readonly SchemaComparer _schemaComparer = new();

    private void CompareParameterPair(
        ParameterInfo oldP, ParameterInfo newP,
        string path, HttpMethodKind method, string locPrefix, List<SemanticChange> changes)
    {
        string location = $"{locPrefix} parameters.{newP.Name}";

        void Add(string kind, string? oldV, string? newV, Dictionary<string, string>? details = null)
        {
            changes.Add(new SemanticChange
            {
                Kind = kind,
                Category = FindingCategory.Parameter,
                Path = path,
                HttpMethod = method,
                Location = location,
                ParameterName = newP.Name,
                Direction = ApiDirection.Request,
                OldValue = oldV,
                NewValue = newV,
                Details = details is { Count: > 0 } ? new FrozenMap(details) : FrozenMap.Empty,
            });
        }

        if (oldP.In != newP.In)
        {
            Add(ChangeKinds.ParameterLocationChanged, oldP.In.ToString(), newP.In.ToString());
            return; // a moved parameter is effectively a different parameter
        }

        if (oldP.Required != newP.Required)
        {
            Add(ChangeKinds.ParameterRequiredChanged,
                oldP.Required ? "required" : "optional",
                newP.Required ? "required" : "optional");
        }

        if (!string.Equals(oldP.Style, newP.Style, StringComparison.Ordinal))
            Add(ChangeKinds.ParameterStyleChanged, oldP.Style ?? "default", newP.Style ?? "default");

        if (oldP.Explode != newP.Explode &&
            (oldP.Explode is not null || newP.Explode is not null))
            Add(ChangeKinds.ParameterExplodeChanged, oldP.Explode?.ToString() ?? "default", newP.Explode?.ToString() ?? "default");

        if (oldP.Deprecated != newP.Deprecated)
            Add(ChangeKinds.ParameterDeprecatedChanged, oldP.Deprecated ? "true" : "false", newP.Deprecated ? "true" : "false");

        // schema-level comparison
        var spec = oldP.Schema?.Spec ?? newP.Schema?.Spec;
        if (spec is not SpecVersion.Unknown && spec != SpecVersion.Swagger20 || oldP.BodySchema is null)
        {
            var oldTypes = oldP.Schema?.PrimaryType();
            var newTypes = newP.Schema?.PrimaryType();
            if (!string.Equals(oldTypes, newTypes, StringComparison.Ordinal) &&
                (oldTypes is not null || newTypes is not null))
                Add(ChangeKinds.ParameterTypeChanged, oldTypes ?? "(none)", newTypes ?? "(none)");

            var oldFormat = oldP.Schema?.Format;
            var newFormat = newP.Schema?.Format;
            if (!string.Equals(oldFormat, newFormat, StringComparison.Ordinal) &&
                (oldFormat is not null || newFormat is not null))
                Add(ChangeKinds.ParameterFormatChanged, oldFormat ?? "(none)", newFormat ?? "(none)");

            CompareEnumAndDefaults(oldP.Schema, newP.Schema, (k, o, n) => Add(k, o, n));

            var subChanges = _schemaComparer.Compare(
                oldP.Schema, newP.Schema, path, method, ApiDirection.Request, location);
            // Skip schema comparer's own type/format/enum emissions for parameters (already emitted above);
            // remap the remaining schema-level kinds onto their parameter-level counterparts.
            foreach (var c in FilterParamSubChanges(subChanges))
            {
                changes.Add(c with
                {
                    Kind = c.Kind switch
                    {
                        ChangeKinds.ConstraintChanged => ChangeKinds.ParameterConstraintChanged,
                        ChangeKinds.ItemsChanged => ChangeKinds.ParameterItemsChanged,
                        _ => c.Kind,
                    },
                    Category = FindingCategory.Parameter,
                    // Schema-level sub-changes do not carry the parameter identity; without
                    // this, rules render "Parameter '(unknown)'".
                    ParameterName = newP.Name,
                });
            }
        }
    }

    private static IEnumerable<SemanticChange> FilterParamSubChanges(IEnumerable<SemanticChange> changes) =>
        changes.Where(c => c.Kind is not (ChangeKinds.SchemaTypeChanged or ChangeKinds.SchemaFormatChanged
            or ChangeKinds.EnumValueAdded or ChangeKinds.EnumValueRemoved or ChangeKinds.NullableChanged
            or ChangeKinds.DefaultChanged));

    /// <summary>Compares enum values / defaults / nullable at parameter level with precise change kinds.</summary>
    private static void CompareEnumAndDefaults(SchemaInfo? oldS, SchemaInfo? newS, Action<string, string?, string?> add)
    {
        if (oldS is null || newS is null) return;

        var wasNullable = oldS.IsNullable();
        var nowNullable = newS.IsNullable();
        if (wasNullable != nowNullable)
            add(ChangeKinds.ParameterNullableChanged, wasNullable ? "nullable" : "not-nullable", nowNullable ? "nullable" : "not-nullable");

        if ((oldS.Node as System.Text.Json.Nodes.JsonObject)?.TryGetPropertyValue("enum", out var oe) == true &&
            (newS.Node as System.Text.Json.Nodes.JsonObject)?.TryGetPropertyValue("enum", out var ne) == true &&
            oe is System.Text.Json.Nodes.JsonArray oa && ne is System.Text.Json.Nodes.JsonArray na)
        {
            var oldVals = oa.Where(x => x is not null).Select(x => x!).ToList();
            var newVals = na.Where(x => x is not null).Select(x => x!).ToList();
            foreach (var v in oldVals)
                if (!newVals.Any(nv => SchemaComparer.JsonDeepEquals(nv, v)))
                    add(ChangeKinds.ParameterEnumValueRemoved, SemanticChange.ValueToString(v), null);
            foreach (var v in newVals)
                if (!oldVals.Any(ov => SchemaComparer.JsonDeepEquals(ov, v)))
                    add(ChangeKinds.ParameterEnumValueAdded, null, SemanticChange.ValueToString(v));
        }

        var oldObj = oldS.Node as System.Text.Json.Nodes.JsonObject;
        var newObj = newS.Node as System.Text.Json.Nodes.JsonObject;
        System.Text.Json.Nodes.JsonNode? od = null, nd = null;
        var hasOld = oldObj?.TryGetPropertyValue("default", out od) == true && od is not null;
        var hasNew = newObj?.TryGetPropertyValue("default", out nd) == true && nd is not null;
        if (hasOld || hasNew)
        {
            if (!SchemaComparer.JsonDeepEquals(od, nd))
                add(ChangeKinds.ParameterDefaultChanged,
                    SemanticChange.ValueToString(od?.DeepClone()),
                    SemanticChange.ValueToString(nd?.DeepClone()));
        }
    }

    // ---------------- request body ----------------

    private void CompareRequestBody(
        RequestBodyInfo? oldBody, RequestBodyInfo? newBody,
        string path, HttpMethodKind method, string locPrefix, List<SemanticChange> changes)
    {
        void Add(string kind, string? oldV, string? newV, Dictionary<string, string>? details = null)
        {
            changes.Add(new SemanticChange
            {
                Kind = kind,
                Category = FindingCategory.RequestBody,
                Path = path,
                HttpMethod = method,
                Location = $"{locPrefix} requestBody",
                Direction = ApiDirection.Request,
                OldValue = oldV,
                NewValue = newV,
                Details = details is { Count: > 0 } ? new FrozenMap(details) : FrozenMap.Empty,
            });
        }

        if (oldBody is null && newBody is not null)
        {
            Add(ChangeKinds.RequestBodyAdded, null, string.Join(", ", SortedKeys(newBody.Content)),
                new Dictionary<string, string> { ["required"] = newBody.Required ? "true" : "false" });
            return;
        }
        if (oldBody is not null && newBody is null)
        {
            Add(ChangeKinds.RequestBodyRemoved, string.Join(", ", SortedKeys(oldBody.Content)), null,
                new Dictionary<string, string> { ["required"] = oldBody.Required ? "true" : "false" });
            return;
        }
        if (oldBody is null || newBody is null) return;

        if (oldBody.Required != newBody.Required)
        {
            Add(ChangeKinds.RequestBodyRequiredChanged,
                oldBody.Required ? "required" : "optional",
                newBody.Required ? "required" : "optional");
        }

        foreach (var ct in SortedKeys(oldBody.Content))
            if (!newBody.Content.ContainsKey(ct))
                Add(ChangeKinds.RequestContentTypeRemoved, ct, null);

        foreach (var ct in SortedKeys(newBody.Content))
        {
            if (!oldBody.Content.ContainsKey(ct))
            {
                Add(ChangeKinds.RequestContentTypeAdded, null, ct);
                continue;
            }

            var subChanges = _schemaComparer.Compare(
                oldBody.Content[ct], newBody.Content[ct],
                path, method, ApiDirection.Request, "body");
            foreach (var c in subChanges)
            {
                changes.Add(c with { Category = FindingCategory.RequestBody });
            }
        }
    }

    // ---------------- responses ----------------

    private void CompareResponses(
        IReadOnlyDictionary<string, ResponseInfo> oldRes, IReadOnlyDictionary<string, ResponseInfo> newRes,
        string path, HttpMethodKind method, string locPrefix, List<SemanticChange> changes)
    {
        foreach (var code in SortedKeys(oldRes))
        {
            if (newRes.ContainsKey(code)) continue;
            if (IsCoveredByOtherKey(code, newRes.Keys)) continue;
            changes.Add(new SemanticChange
            {
                Kind = ChangeKinds.ResponseStatusCodeRemoved,
                Category = FindingCategory.Response,
                Path = path,
                HttpMethod = method,
                Location = $"{locPrefix} responses.{code}",
                Direction = ApiDirection.Response,
                OldValue = code,
                Details = new FrozenMap(new Dictionary<string, string>
                {
                    ["statusCode"] = code,
                    ["hadContent"] = oldRes[code].Content.Count > 0 ? "true" : "false",
                }),
            });
        }

        foreach (var code in SortedKeys(newRes))
        {
            if (oldRes.TryGetValue(code, out var oldResp))
            {
                var newResp = newRes[code];

                foreach (var ct in SortedKeys(oldResp.Content))
                    if (!newResp.Content.ContainsKey(ct) && !IsCoveredByOtherContentType(ct, newResp.Content.Keys))
                        changes.Add(new SemanticChange
                        {
                            Kind = ChangeKinds.ResponseContentTypeRemoved,
                            Category = FindingCategory.Response,
                            Path = path,
                            HttpMethod = method,
                            Location = $"{locPrefix} responses.{code}",
                            Direction = ApiDirection.Response,
                            OldValue = ct,
                            Details = new FrozenMap(new Dictionary<string, string> { ["statusCode"] = code }),
                        });

                foreach (var ct in SortedKeys(newResp.Content))
                {
                    if (!oldResp.Content.ContainsKey(ct))
                    {
                        changes.Add(new SemanticChange
                        {
                            Kind = ChangeKinds.ResponseContentTypeAdded,
                            Category = FindingCategory.Response,
                            Path = path,
                            HttpMethod = method,
                            Location = $"{locPrefix} responses.{code}",
                            Direction = ApiDirection.Response,
                            NewValue = ct,
                            Details = new FrozenMap(new Dictionary<string, string> { ["statusCode"] = code }),
                        });
                        continue;
                    }

                    var subChanges = _schemaComparer.Compare(
                        oldResp.Content[ct], newResp.Content[ct],
                        path, method, ApiDirection.Response, $"{code}");
                    foreach (var c in subChanges)
                    {
                        changes.Add(c with
                        {
                            Category = FindingCategory.Response,
                            Location = $"{locPrefix} responses.{c.Location}",
                            Details = WithStatusCode(c.Details, code),
                        });
                    }
                }

                // headers
                var oldHeaders = oldResp.Headers.ToDictionary(h => h.Name, StringComparer.Ordinal);
                var newHeaders = newResp.Headers.ToDictionary(h => h.Name, StringComparer.Ordinal);
                foreach (var hName in oldHeaders.Keys.OrderBy(k => k, StringComparer.Ordinal))
                {
                    if (!newHeaders.TryGetValue(hName, out var newHeader))
                    {
                        changes.Add(new SemanticChange
                        {
                            Kind = ChangeKinds.ResponseHeaderRemoved,
                            Category = FindingCategory.Response,
                            Path = path,
                            HttpMethod = method,
                            Location = $"{locPrefix} responses.{code}.headers.{hName}",
                            Direction = ApiDirection.Response,
                            OldValue = hName,
                            Details = WithStatusCode(new Dictionary<string, string>
                            {
                                ["required"] = oldHeaders[hName].Required ? "true" : "false",
                            }, code),
                        });
                    }
                    else if (oldHeaders[hName].Required != newHeader.Required)
                    {
                        changes.Add(new SemanticChange
                        {
                            Kind = ChangeKinds.ResponseHeaderRequiredChanged,
                            Category = FindingCategory.Response,
                            Path = path,
                            HttpMethod = method,
                            Location = $"{locPrefix} responses.{code}.headers.{hName}",
                            Direction = ApiDirection.Response,
                            OldValue = oldHeaders[hName].Required ? "required" : "optional",
                            NewValue = newHeader.Required ? "required" : "optional",
                            Details = WithStatusCode(new Dictionary<string, string>(), code),
                        });
                    }
                }
                foreach (var hName in newHeaders.Keys.OrderBy(k => k, StringComparer.Ordinal))
                    if (!oldHeaders.ContainsKey(hName))
                        changes.Add(new SemanticChange
                        {
                            Kind = ChangeKinds.ResponseHeaderAdded,
                            Category = FindingCategory.Response,
                            Path = path,
                            HttpMethod = method,
                            Location = $"{locPrefix} responses.{code}.headers.{hName}",
                            Direction = ApiDirection.Response,
                            NewValue = hName,
                            Details = WithStatusCode(new Dictionary<string, string>(), code),
                        });
            }
            else
            {
                if (IsCoveredByOtherKey(code, oldRes.Keys)) continue;
                changes.Add(new SemanticChange
                {
                    Kind = ChangeKinds.ResponseStatusCodeAdded,
                    Category = FindingCategory.Response,
                    Path = path,
                    HttpMethod = method,
                    Location = $"{locPrefix} responses.{code}",
                    Direction = ApiDirection.Response,
                    NewValue = code,
                    Details = new FrozenMap(new Dictionary<string, string> { ["statusCode"] = code }),
                });
            }
        }
    }

    private static FrozenMap WithStatusCode(IReadOnlyDictionary<string, string> existing, string code)
    {
        var d = new Dictionary<string, string>(existing);
        d["statusCode"] = code;
        return new FrozenMap(d);
    }

    private static bool IsCoveredByOtherContentType(string contentType, IEnumerable<string> available)
    {
        // Exact media types are not covered by wildcards except the obvious */* case.
        return available.Contains("*/*", StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when removing status <paramref name="code"/> is masked by a wildcard response
    /// ("default", "2XX"...) that still exists on the other side.
    /// </summary>
    private static bool IsCoveredByOtherKey(string code, IEnumerable<string> coveringKeys)
    {
        if (!TryParseStatusCodeRange(code, out var lo, out var hi)) return false;
        foreach (var key in coveringKeys)
        {
            if (!TryParseStatusCodeRange(key, out var klo, out var khi)) continue;
            if (klo <= lo && hi <= khi && !(lo == klo && hi == khi)) return true;
        }
        return false;
    }

    internal static bool TryParseStatusCodeRange(string key, out int lo, out int hi)
    {
        lo = hi = 0;
        if (string.Equals(key, "default", StringComparison.OrdinalIgnoreCase))
        {
            lo = 100; hi = 599;
            return true;
        }
        if (key.Length == 3 && char.IsDigit(key[0]) && key.EndsWith("XX", StringComparison.OrdinalIgnoreCase))
        {
            var hundreds = key[0] - '0';
            lo = hundreds * 100;
            hi = lo + 99;
            return true;
        }
        if (key.Length == 3 && key.All(char.IsAsciiDigit) && int.TryParse(key, out var sc))
        {
            lo = hi = sc;
            return true;
        }
        return false;
    }

    // ---------------- security ----------------

    private static void CompareEffectiveSecurity(
        IReadOnlyList<SecurityRequirementInfo>? oldEffective,
        IReadOnlyList<SecurityRequirementInfo>? newEffective,
        string path, HttpMethodKind method, string locPrefix,
        List<SemanticChange> changes)
    {
        // Callers have already applied operation-level override / global inheritance.
        var effectiveOld = oldEffective ?? [];
        var effectiveNew = newEffective ?? [];

        var oldReqs = ToRequirementMap(effectiveOld);
        var newReqs = ToRequirementMap(effectiveNew);

        foreach (var scheme in oldReqs.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (!newReqs.TryGetValue(scheme, out var newScopes))
            {
                changes.Add(new SemanticChange
                {
                    Kind = ChangeKinds.SecurityRequirementRemoved,
                    Category = FindingCategory.Security,
                    Path = path,
                    HttpMethod = method,
                    Location = $"{locPrefix} security",
                    OldValue = scheme,
                    Details = new FrozenMap(new Dictionary<string, string> { ["scheme"] = scheme }),
                });
            }
            else
            {
                foreach (var scope in oldReqs[scheme].OrderBy(s => s, StringComparer.Ordinal))
                    if (!newScopes.Contains(scope))
                        changes.Add(new SemanticChange
                        {
                            Kind = ChangeKinds.OAuthScopeRemoved,
                            Category = FindingCategory.Security,
                            Path = path,
                            HttpMethod = method,
                            Location = $"{locPrefix} security.{scheme}",
                            OldValue = scope,
                            Details = new FrozenMap(new Dictionary<string, string>
                            {
                                ["scheme"] = scheme, ["scope"] = scope,
                            }),
                        });
            }
        }

        foreach (var scheme in newReqs.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (!oldReqs.TryGetValue(scheme, out var oldScopes))
            {
                changes.Add(new SemanticChange
                {
                    Kind = ChangeKinds.SecurityRequirementAdded,
                    Category = FindingCategory.Security,
                    Path = path,
                    HttpMethod = method,
                    Location = $"{locPrefix} security",
                    NewValue = scheme,
                    Details = new FrozenMap(new Dictionary<string, string>
                    {
                        ["scheme"] = scheme,
                        ["scopes"] = string.Join(",", newReqs[scheme].OrderBy(s => s, StringComparer.Ordinal)),
                    }),
                });
            }
            else
            {
                foreach (var scope in newReqs[scheme].OrderBy(s => s, StringComparer.Ordinal))
                    if (!oldScopes.Contains(scope))
                        changes.Add(new SemanticChange
                        {
                            Kind = ChangeKinds.OAuthScopeAdded,
                            Category = FindingCategory.Security,
                            Path = path,
                            HttpMethod = method,
                            Location = $"{locPrefix} security.{scheme}",
                            NewValue = scope,
                            Details = new FrozenMap(new Dictionary<string, string>
                            {
                                ["scheme"] = scheme, ["scope"] = scope,
                            }),
                        });
            }
        }
    }

    private static Dictionary<string, HashSet<string>> ToRequirementMap(
        IReadOnlyList<SecurityRequirementInfo> requirements)
    {
        // Multiple requirement objects are alternatives (OR); within an object scopes AND.
        // For comparison purposes we union alternatives per scheme — conservative for detection.
        var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var req in requirements)
        {
            if (!map.TryGetValue(req.Scheme, out var scopes))
            {
                scopes = [];
                map[req.Scheme] = scopes;
            }
            foreach (var s in req.Scopes) scopes.Add(s);
        }
        return map;
    }

    private static void CompareSecuritySchemes(
        IReadOnlyDictionary<string, SecuritySchemeInfo> oldSchemes,
        IReadOnlyDictionary<string, SecuritySchemeInfo> newSchemes,
        List<SemanticChange> changes)
    {
        foreach (var name in oldSchemes.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (!newSchemes.TryGetValue(name, out var newScheme))
            {
                changes.Add(new SemanticChange
                {
                    Kind = ChangeKinds.SecuritySchemeRemoved,
                    Category = FindingCategory.Security,
                    Path = "",
                    Location = $"securitySchemes.{name}",
                    OldValue = name,
                    Details = new FrozenMap(new Dictionary<string, string> { ["scheme"] = name }),
                });
                continue;
            }
            var oldScheme = oldSchemes[name];

            void Add(string kind, string? oldV, string? newV, Dictionary<string, string>? extra = null)
            {
                var d = new Dictionary<string, string>(extra ?? []);
                d["scheme"] = name;
                changes.Add(new SemanticChange
                {
                    Kind = kind,
                    Category = FindingCategory.Security,
                    Path = "",
                    Location = $"securitySchemes.{name}",
                    OldValue = oldV,
                    NewValue = newV,
                    Details = new FrozenMap(d),
                });
            }

            if (oldScheme.Type != newScheme.Type || !string.Equals(oldScheme.RawType, newScheme.RawType, StringComparison.Ordinal))
                Add(ChangeKinds.SecuritySchemeTypeChanged, oldScheme.RawType, newScheme.RawType);

            if (!string.Equals(oldScheme.In, newScheme.In, StringComparison.Ordinal) &&
                (oldScheme.In is not null || newScheme.In is not null))
                Add(ChangeKinds.ApiKeyLocationChanged, oldScheme.In, newScheme.In);

            if (!string.Equals(oldScheme.KeyName, newScheme.KeyName, StringComparison.Ordinal) &&
                (oldScheme.KeyName is not null || newScheme.KeyName is not null))
                Add(ChangeKinds.ApiKeyNameChanged, oldScheme.KeyName, newScheme.KeyName);

            if (!string.Equals(oldScheme.Scheme, newScheme.Scheme, StringComparison.Ordinal) &&
                (oldScheme.Scheme is not null || newScheme.Scheme is not null))
                Add(ChangeKinds.HttpSchemeChanged, oldScheme.Scheme, newScheme.Scheme);

            if (!string.Equals(oldScheme.BearerFormat, newScheme.BearerFormat, StringComparison.Ordinal) &&
                (oldScheme.BearerFormat is not null || newScheme.BearerFormat is not null))
                Add(ChangeKinds.BearerFormatChanged, oldScheme.BearerFormat, newScheme.BearerFormat);

            if (!string.Equals(oldScheme.OpenIdConnectUrl, newScheme.OpenIdConnectUrl, StringComparison.Ordinal) &&
                (oldScheme.OpenIdConnectUrl is not null || newScheme.OpenIdConnectUrl is not null))
                Add(ChangeKinds.OpenIdConnectUrlChanged, oldScheme.OpenIdConnectUrl, newScheme.OpenIdConnectUrl);

            foreach (var flowName in oldScheme.Flows.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                if (!newScheme.Flows.TryGetValue(flowName, out var newFlow))
                {
                    Add(ChangeKinds.OAuthFlowUrlChanged, flowName, null,
                        new Dictionary<string, string> { ["flow"] = flowName, ["urlKind"] = "flow.removed" });
                    continue;
                }
                var oldFlow = oldScheme.Flows[flowName];
                if (!string.Equals(oldFlow.AuthorizationUrl, newFlow.AuthorizationUrl, StringComparison.Ordinal) &&
                    (oldFlow.AuthorizationUrl is not null || newFlow.AuthorizationUrl is not null))
                    Add(ChangeKinds.OAuthFlowUrlChanged, oldFlow.AuthorizationUrl, newFlow.AuthorizationUrl,
                        new Dictionary<string, string> { ["flow"] = flowName, ["urlKind"] = "authorizationUrl" });
                if (!string.Equals(oldFlow.TokenUrl, newFlow.TokenUrl, StringComparison.Ordinal) &&
                    (oldFlow.TokenUrl is not null || newFlow.TokenUrl is not null))
                    Add(ChangeKinds.OAuthFlowUrlChanged, oldFlow.TokenUrl, newFlow.TokenUrl,
                        new Dictionary<string, string> { ["flow"] = flowName, ["urlKind"] = "tokenUrl" });

                foreach (var scope in oldFlow.Scopes.OrderBy(s => s, StringComparer.Ordinal))
                    if (!newFlow.Scopes.Contains(scope))
                        Add(ChangeKinds.OAuthFlowScopeDefRemoved, scope, null,
                            new Dictionary<string, string> { ["flow"] = flowName, ["scope"] = scope });
                foreach (var scope in newFlow.Scopes.OrderBy(s => s, StringComparer.Ordinal))
                    if (!oldFlow.Scopes.Contains(scope))
                        Add(ChangeKinds.OAuthFlowScopeDefAdded, null, scope,
                            new Dictionary<string, string> { ["flow"] = flowName, ["scope"] = scope });
            }
        }

        foreach (var name in newSchemes.Keys.OrderBy(k => k, StringComparer.Ordinal))
            if (!oldSchemes.ContainsKey(name))
                changes.Add(new SemanticChange
                {
                    Kind = ChangeKinds.SecuritySchemeAdded,
                    Category = FindingCategory.Security,
                    Path = "",
                    Location = $"securitySchemes.{name}",
                    NewValue = name,
                    Details = new FrozenMap(new Dictionary<string, string> { ["scheme"] = name }),
                });
    }

    private static List<string> SortedKeys(IReadOnlyDictionary<string, ResponseInfo> dict) =>
        dict.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

    private static List<string> SortedKeys(IReadOnlyDictionary<string, SchemaInfo> dict) =>
        dict.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
}
