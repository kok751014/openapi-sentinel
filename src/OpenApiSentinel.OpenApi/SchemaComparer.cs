using System.Text.Json.Nodes;
using OpenApiSentinel.Core;
using OpenApiSentinel.OpenApi.Model;

namespace OpenApiSentinel.OpenApi;

/// <summary>
/// Recursively compares two normalized schemas and emits directional semantic changes.
/// Understands JSON Schema semantics for OpenAPI 3.1+ while applying OpenAPI 2.0/3.0
/// rules (nullable keyword, single type) where appropriate.
/// </summary>
public sealed class SchemaComparer
{
    private readonly List<SemanticChange> _changes = [];

    public IReadOnlyList<SemanticChange> Compare(
        SchemaInfo? oldSchema, SchemaInfo? newSchema,
        string path, HttpMethodKind method, ApiDirection direction, string locationPrefix)
    {
        // One comparison result per call. A snapshot is returned so callers holding earlier
        // results are unaffected when the comparer instance is reused.
        _changes.Clear();
        CompareCore(oldSchema, newSchema, path, method, direction, locationPrefix, depth: 0);
        return _changes.ToArray();
    }

    private void CompareCore(
        SchemaInfo? oldS, SchemaInfo? newS,
        string path, HttpMethodKind method, ApiDirection direction,
        string location, int depth)
    {
        if (depth > 48) return; // structural guard; RefResolver already bounds cycles

        if (oldS is null && newS is null) return;

        if (oldS is null || newS is null)
        {
            // Whole schema presence change handled by callers; here it means one side lost its schema.
            Emit(ChangeKinds.ItemsChanged, direction, path, method, location,
                oldS?.ToString(), newS?.ToString());
            return;
        }

        var oldNode = StripCyclicMarker(oldS.Node);
        var newNode = StripCyclicMarker(newS.Node);

        // Unresolved-reference markers (safety budgets tripped): identical targets are
        // silently equal; anything else surfaces as exactly ONE Verify event instead of
        // cascading noise or a hard failure.
        var oldMarker = RefResolver.IsUnresolvedMarker(oldNode, out var oldRef);
        var newMarker = RefResolver.IsUnresolvedMarker(newNode, out var newRef);
        if (oldMarker || newMarker)
        {
            if (oldMarker && newMarker &&
                string.Equals(oldRef, newRef, StringComparison.Ordinal))
                return;
            Emit(ChangeKinds.SchemaUnresolvedChanged, direction, path, method, location,
                oldMarker ? oldRef : SemanticChange.ValueToString(oldS.Node),
                newMarker ? newRef : SemanticChange.ValueToString(newS.Node));
            return;
        }

        // Cyclic markers only match when pointing at the same target.
        if (oldNode is not JsonObject && newNode is not JsonObject) return;

        var oldObj = oldNode as JsonObject ?? [];
        var newObj = newNode as JsonObject ?? [];

        CompareType(oldS, newS, oldObj, newObj, path, method, direction, location);
        CompareEnumAndConst(oldS, newS, path, method, direction, location);
        CompareConstraints(oldObj, newObj, path, method, direction, location);
        CompareObjectStructure(oldS, newS, path, method, direction, location, depth);
        CompareComposition(oldS, newS, path, method, direction, location);
        CompareMiscFlags(oldObj, newObj, path, method, direction, location);
    }

    private static JsonNode StripCyclicMarker(JsonNode node)
    {
        if (node is JsonObject o &&
            o.Count == 1 &&
            o.ContainsKey("x-sentinel-cyclic-ref"))
            return new JsonObject { ["type"] = "cyclic" };
        return node;
    }

    // ---------------- type & nullability ----------------

    private void CompareType(SchemaInfo oldS, SchemaInfo newS, JsonObject o, JsonObject n,
        string path, HttpMethodKind method, ApiDirection direction, string location)
    {
        var oldTypes = oldS.Types();
        var newTypes = newS.Types();

        var oldCore = oldTypes.Where(t => t != "null").ToHashSet(StringComparer.Ordinal);
        var newCore = newTypes.Where(t => t != "null").ToHashSet(StringComparer.Ordinal);
        var wasNullable = oldTypes.Contains("null");
        var nowNullable = newTypes.Contains("null");

        if (!oldCore.SetEquals(newCore))
        {
            Emit(ChangeKinds.SchemaTypeChanged, direction, path, method, location,
                JsonValue.Create(string.Join("|", Ordered(oldCore)))?.ToJsonString(),
                JsonValue.Create(string.Join("|", Ordered(newCore)))?.ToJsonString());
        }

        if (wasNullable != nowNullable)
        {
            Emit(ChangeKinds.NullableChanged, direction, path, method, location,
                wasNullable ? "true" : "false",
                nowNullable ? "true" : "false");
        }

        var oldFormat = oldS.Format;
        var newFormat = newS.Format;
        if (!string.Equals(oldFormat, newFormat, StringComparison.Ordinal))
        {
            Emit(ChangeKinds.SchemaFormatChanged, direction, path, method, location,
                StringOrNull(oldFormat), StringOrNull(newFormat));
        }
    }

    // ---------------- enum / const ----------------

    private void CompareEnumAndConst(SchemaInfo oldS, SchemaInfo newS,
        string path, HttpMethodKind method, ApiDirection direction, string location)
    {
        var oldEnum = GetArrayValues(oldS.Node, "enum");
        var newEnum = GetArrayValues(newS.Node, "enum");

        if (oldEnum is not null || newEnum is not null)
        {
            var removed = oldEnum?.Except(newEnum ?? [], ValueEqualityComparer.Instance).ToList() ?? [];
            var added = newEnum?.Except(oldEnum ?? [], ValueEqualityComparer.Instance).ToList() ?? [];

            foreach (var v in removed)
                Emit(ChangeKinds.EnumValueRemoved, direction, path, method, location,
                    SemanticChange.ValueToString(v), null);
            foreach (var v in added)
                Emit(ChangeKinds.EnumValueAdded, direction, path, method, location,
                    null, SemanticChange.ValueToString(v));
        }

        System.Text.Json.Nodes.JsonNode? oldConst = null, newConst = null;
        var oldHas = oldS.TryGetKeyword("const", out oldConst);
        var newHas = newS.TryGetKeyword("const", out newConst);
        if (oldHas || newHas)
        {
            var oc = oldConst?.DeepClone();
            var nc = newConst?.DeepClone();
            if (!ValueEqualityComparer.Instance.Equals(oc, nc))
            {
                Emit(ChangeKinds.ConstChanged, direction, path, method, location,
                    SemanticChange.ValueToString(oc), SemanticChange.ValueToString(nc));
            }
        }
    }

    // ---------------- scalar constraints ----------------

    private static readonly string[] NumericConstraints =
        ["minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum", "multipleOf"];

    private static readonly string[] StringConstraints = ["minLength", "maxLength", "pattern"];

    private static readonly string[] ArrayConstraints = ["minItems", "maxItems", "uniqueItems"];

    private static readonly string[] ObjectConstraints = ["minProperties", "maxProperties"];

    private void CompareConstraints(JsonObject o, JsonObject n,
        string path, HttpMethodKind method, ApiDirection direction, string location)
    {
        foreach (var kw in NumericConstraints.Concat(StringConstraints).Concat(ArrayConstraints).Concat(ObjectConstraints))
        {
            o.TryGetPropertyValue(kw, out var ov);
            n.TryGetPropertyValue(kw, out var nv);

            var equal = ValueEqualityComparer.Instance.Equals(ov?.DeepClone(), nv?.DeepClone());
            if (!equal)
            {
                EmitWithDetail(ChangeKinds.ConstraintChanged, direction, path, method, location,
                    SemanticChange.ValueToString(ov?.DeepClone()),
                    SemanticChange.ValueToString(nv?.DeepClone()),
                    new Dictionary<string, string> { ["constraint"] = kw });
            }
        }
    }

    // ---------------- object structure ----------------

    private void CompareObjectStructure(SchemaInfo oldS, SchemaInfo newS,
        string path, HttpMethodKind method, ApiDirection direction, string location, int depth)
    {
        var oldProps = GetProperties(oldS.Node);
        var newProps = GetProperties(newS.Node);

        if (oldProps is not null || newProps is not null)
        {
            var oldNames = oldProps?.Select(p => p.Key).ToHashSet(StringComparer.Ordinal) ?? [];
            var newNames = newProps?.Select(p => p.Key).ToHashSet(StringComparer.Ordinal) ?? [];

            // High-confidence rename detection: same primary type + >=80% child property overlap.
            foreach (var removedName in oldNames.Except(newNames, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
            {
                var renameMatch = FindRenameMatch(removedName, oldProps![removedName], newNames, newProps!);
                if (renameMatch is { } match)
                {
                    EmitWithDetail(ChangeKinds.PropertyRenamed, direction, path, method, location,
                        match.OldKey, match.NewKey,
                        new Dictionary<string, string> { ["property"] = removedName });
                    newNames.Remove(match.NewKey);
                }
                else
                {
                    EmitWithDetail(ChangeKinds.PropertyRemoved, direction, path, method, location,
                        null, null, new Dictionary<string, string> { ["property"] = removedName });

                    if (direction == ApiDirection.Request && IsRequired(oldS, removedName))
                    {
                        // request-side removal of a required property also means the server no longer requires it
                        EmitRequiredChange(direction, path, method, ChildLocation(location, removedName),
                            removedName, becameRequired: false);
                    }
                }
            }

            foreach (var addedName in newNames.Except(oldNames).OrderBy(x => x, StringComparer.Ordinal))
            {
                var addedIsRequired = direction == ApiDirection.Request && IsRequired(newS, addedName);

                // On the request side a newly-added required property is reported once, as a
                // required-property change (more actionable); optional additions are reported here.
                if (!addedIsRequired)
                {
                    EmitWithDetail(ChangeKinds.PropertyAdded, direction, path, method, location,
                        null, null, new Dictionary<string, string> { ["property"] = addedName });
                }
                else
                {
                    EmitRequiredChange(direction, path, method, ChildLocation(location, addedName),
                        addedName, becameRequired: true);
                }
            }

            // Common properties: recurse.
            if (oldProps is not null && newProps is not null)
            {
                foreach (var common in oldProps.Select(p => p.Key)
                             .Intersect(newProps.Select(p => p.Key), StringComparer.Ordinal)
                             .OrderBy(x => x, StringComparer.Ordinal))
                {
                    var oldChild = Wrap(oldProps[common]);
                    var newChild = Wrap(newProps[common]);
                    CompareCore(oldChild, newChild, path, method, direction,
                        ChildLocation(location, common), depth + 1);
                }
            }
        }

        // required list changes; add/remove cases already covered above where they matter most
        var oldRequired = GetStringSet(oldS.Node, "required");
        var newRequired = GetStringSet(newS.Node, "required");
        if (oldRequired is not null || newRequired is not null)
        {
            var oldPropKeys = GetProperties(oldS.Node)?.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
            var newPropKeys = GetProperties(newS.Node)?.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
            // Include required-only entries (required referencing an undefined property is invalid
            // but must still be diffed rather than silently dropped).
            var propsUnion = (oldPropKeys ?? []).Union(newPropKeys ?? [])
                .Union(oldRequired ?? []).Union(newRequired ?? []);

            foreach (var prop in propsUnion.OrderBy(x => x, StringComparer.Ordinal))
            {
                var was = oldRequired?.Contains(prop) ?? false;
                var now = newRequired?.Contains(prop) ?? false;
                if (was == now) continue;

                var existsInBoth = (oldPropKeys?.Contains(prop) ?? false) && (newPropKeys?.Contains(prop) ?? false);
                if (!existsInBoth && direction == ApiDirection.Request)
                {
                    // Added/removed properties already carry the required-ness story for requests —
                    // but only when the property actually exists on one side. A required entry that
                    // references an undefined property on both sides must still be reported.
                    var inOldProps = oldPropKeys?.Contains(prop) ?? false;
                    var inNewProps = newPropKeys?.Contains(prop) ?? false;
                    if (inOldProps != inNewProps)
                        continue;
                }

                EmitRequiredChange(direction, path, method, ChildLocation(location, prop), prop, now);
            }
        }

        // additionalProperties
        {
            // Absent and explicit `true` are semantically identical (extras allowed unvalidated):
            // normalize before comparing so pure restyling does not produce findings.
            static JsonNode? Normalized(SchemaInfo s)
            {
                if (!s.TryGetKeyword("additionalProperties", out var v)) return System.Text.Json.Nodes.JsonValue.Create(true);
                return v;
            }

            var oa = Normalized(oldS);
            var na = Normalized(newS);
            if (!ValueEqualityComparer.Instance.Equals(oa?.DeepClone(), na?.DeepClone()))
            {
                EmitWithDetail(ChangeKinds.AdditionalPropertiesChanged, direction, path, method, location,
                    DescribeAdditionalProperties(oa), DescribeAdditionalProperties(na),
                    new Dictionary<string, string>());
            }
        }

        // items
        {
            var oldItems = oldS.TryGetKeyword("items", out var oi) ? Wrap(oi) : null;
            var newItems = newS.TryGetKeyword("items", out var ni) ? Wrap(ni) : null;

            bool oldHas = HasItems(oldS.Node), newHas = HasItems(newS.Node);
            if (oldHas != newHas)
            {
                // Presence change: the single-sided branch below emits exactly one event.
                CompareCore(oldItems, newItems, path, method, direction,
                    ChildLocation(location, "[]"), depth + 1);
            }
            else if (oldHas)
            {
                // Both present: recurse first and emit the CG5008 summary marker only when
                // the element diff produced at least one substantive change. Description/
                // title/example drift inside an element schema must not create findings
                // (false-positive control); previously any byte difference opened the gate.
                var before = _changes.Count;
                CompareCore(oldItems, newItems, path, method, direction,
                    ChildLocation(location, "[]"), depth + 1);
                if (_changes.Count > before)
                {
                    Emit(ChangeKinds.ItemsChanged, direction, path, method, location,
                        oldItems?.ToString(), newItems?.ToString());
                }
            }
        }
    }

    private static (string OldKey, string NewKey)? FindRenameMatch(
        string removedName, JsonNode? removedSchema, HashSet<string> newOnly, JsonObject newProps)
    {
        if (newOnly.Count == 0) return null;

        var oldPrimaryType = PrimaryTypeName(removedSchema);

        foreach (var candidate in newOnly.OrderBy(x => x, StringComparer.Ordinal))
        {
            var candPrimaryType = PrimaryTypeName(newProps[candidate]);
            if (!string.Equals(oldPrimaryType, candPrimaryType, StringComparison.Ordinal)) continue;

            // Structural similarity: compare nested property-name sets for objects.
            var oldNested = NestedPropertyNames(removedSchema);
            var newNested = NestedPropertyNames(newProps[candidate]);

            if (oldNested.Count == 0 && newNested.Count == 0)
            {
                // Primitive types with identical shape: treat as high-confidence rename only when
                // both have no children at all AND names differ by case/separator variants.
                if (IsLikelyTypographicVariant(removedName, candidate))
                    return (removedName, candidate);
                continue;
            }

            var union = oldNested.Union(newNested).ToList();
            if (union.Count > 0)
            {
                var inter = oldNested.Intersect(newNested).Count();
                var jaccard = (double)inter / union.Count;
                if (jaccard >= 0.8)
                    return (removedName, candidate);
            }
        }
        return null;
    }

    private static string? PrimaryTypeName(JsonNode? schema)
    {
        if (schema is not JsonObject o) return null;
        if (o["type"] is JsonArray ta)
        {
            foreach (var t in ta)
                if (t?.GetValue<string>() is { } v && v != "null") return v;
            return null;
        }
        return o["type"]?.GetValue<string>();
    }

    private static HashSet<string> NestedPropertyNames(JsonNode? schema)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        Collect(schema, 0);
        return result;

        void Collect(JsonNode? n, int d)
        {
            if (n is not JsonObject o || d > 3) return;
            if (o["properties"] is JsonObject po)
                foreach (var k in po)
                {
                    result.Add(k.Key);
                    Collect(k.Value, d + 1);
                }
        }
    }

    private static bool IsLikelyTypographicVariant(string a, string b)
    {
        var normA = a.Replace("_", "").Replace("-", "").ToLowerInvariant();
        var normB = b.Replace("_", "").Replace("-", "").ToLowerInvariant();
        return normA.Equals(normB, StringComparison.Ordinal);
    }

    private static string? DescribeAdditionalProperties(JsonNode? ap)
    {
        if (ap is null) return null;
        if (ap is JsonValue v && v.TryGetValue<bool>(out var b)) return b ? "true" : "false";
        return "<schema>";
    }

    // ---------------- composition ----------------

    private void CompareComposition(SchemaInfo oldS, SchemaInfo newS,
        string path, HttpMethodKind method, ApiDirection direction, string location)
    {
        foreach (var keyword in (string[])["allOf", "oneOf", "anyOf", "not"])
        {
            var hasOld = oldS.TryGetKeyword(keyword, out var oldVal);
            var hasNew = newS.TryGetKeyword(keyword, out var newVal);
            if (!hasOld && !hasNew) continue;

            var oldStr = oldVal is null ? null : SemanticChange.ValueToString(oldVal.DeepClone());
            var newStr = newVal is null ? null : SemanticChange.ValueToString(newVal.DeepClone());

            if (!string.Equals(oldStr, newStr, StringComparison.Ordinal))
            {
                // Deep equality to avoid false positives from key ordering, with prose-only
                // member drift (description/title/example) ignored entirely: a composition
                // whose members changed wording only is not a compatibility event.
                if (!JsonDeepEquals(StripProse(oldVal), StripProse(newVal)))
                {
                    EmitWithDetail(ChangeKinds.CompositionChanged, direction, path, method, location,
                        Truncate(oldStr), Truncate(newStr),
                        new Dictionary<string, string> { ["composition"] = keyword });
                }
            }
        }

        // discriminator
        {
            var hasOld = oldS.TryGetKeyword("discriminator", out var od);
            var hasNew = newS.TryGetKeyword("discriminator", out var nd);
            if ((hasOld || hasNew) && !JsonDeepEquals(od, nd))
            {
                // Granular details let the rule separate breaking property changes from
                // additive mapping extensions (false-positive control).
                var details = new Dictionary<string, string>();
                static JsonObject? AsObj(JsonNode? n) => n as JsonObject;
                static string? Str(JsonNode? n) =>
                    n is System.Text.Json.Nodes.JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

                var oldObj = AsObj(od);
                var newObj = AsObj(nd);
                var oldProp = Str(oldObj?["propertyName"]);
                var newProp = Str(newObj?["propertyName"]);

                details["aspect"] = !string.Equals(oldProp, newProp, StringComparison.Ordinal)
                    ? "propertyName"
                    : "mapping";

                var oldMap = oldObj?["mapping"] as JsonObject;
                var newMap = newObj?["mapping"] as JsonObject;
                if (oldMap is not null || newMap is not null)
                {
                    var removedKeys = (oldMap?.Select(kvp => kvp.Key) ?? Enumerable.Empty<string>())
                        .Where(k => newMap?.ContainsKey(k) != true)
                        .OrderBy(k => k, StringComparer.Ordinal).ToList();
                    var addedKeys = (newMap?.Select(kvp => kvp.Key) ?? Enumerable.Empty<string>())
                        .Where(k => oldMap?.ContainsKey(k) != true)
                        .OrderBy(k => k, StringComparer.Ordinal).ToList();
                    if (removedKeys.Count > 0) details["mappingRemoved"] = string.Join(",", removedKeys);
                    if (addedKeys.Count > 0) details["mappingAdded"] = string.Join(",", addedKeys);
                }

                EmitWithDetail(ChangeKinds.DiscriminatorChanged, direction, path, method, location,
                    Truncate(od is null ? null : SemanticChange.ValueToString(od)),
                    Truncate(nd is null ? null : SemanticChange.ValueToString(nd)),
                    details);
            }
        }
    }

    // ---------------- misc flags ----------------

    private void CompareMiscFlags(JsonObject o, JsonObject n,
        string path, HttpMethodKind method, ApiDirection direction, string location)
    {
        CompareFlag(o, n, "readOnly", "readOnly", ChangeKinds.ReadOnlyWriteOnlyChanged);
        CompareFlag(o, n, "writeOnly", "writeOnly", ChangeKinds.ReadOnlyWriteOnlyChanged);
        CompareFlag(o, n, "deprecated", "deprecated", ChangeKinds.SchemaDeprecatedChanged);

        // default
        {
            o.TryGetPropertyValue("default", out var od);
            n.TryGetPropertyValue("default", out var nd);
            if (!ValueEqualityComparer.Instance.Equals(od?.DeepClone(), nd?.DeepClone()) && (od is not null || nd is not null))
            {
                Emit(ChangeKinds.DefaultChanged, direction, path, method, location,
                    SemanticChange.ValueToString(od?.DeepClone()), SemanticChange.ValueToString(nd?.DeepClone()));
            }
        }

        // example/examples (informational only)
        {
            o.TryGetPropertyValue("example", out var oe);
            n.TryGetPropertyValue("example", out var ne);
            if ((oe is not null || ne is not null) && !JsonDeepEquals(oe, ne))
            {
                Emit(ChangeKinds.ExampleChanged, direction, path, method, location, null, null);
            }
        }

        void CompareFlag(JsonObject oo, JsonObject nn, string keyword, string kindDetail, string changeKind)
        {
            var ov = GetBool(oo, keyword);
            var nv = GetBool(nn, keyword);
            if (ov != nv)
            {
                EmitWithDetail(changeKind, direction, path, method, location,
                    ov?.ToString() ?? "false", nv?.ToString() ?? "false",
                    new Dictionary<string, string> { ["flag"] = kindDetail });
            }
        }
    }

    // ---------------- helpers ----------------

    private void EmitRequiredChange(ApiDirection direction, string path, HttpMethodKind method,
        string location, string property, bool becameRequired)
    {
        EmitWithDetail(ChangeKinds.RequiredPropertyChanged, direction, path, method, location,
            becameRequired ? "false" : "true",
            becameRequired ? "true" : "false",
            new Dictionary<string, string> { ["property"] = property, ["becameRequired"] = becameRequired ? "true" : "false" });
    }

    private void Emit(string kind, ApiDirection direction, string path, HttpMethodKind method,
        string location, string? oldValue, string? newValue)
        => EmitWithDetail(kind, direction, path, method, location, oldValue, newValue, new Dictionary<string, string>());

    private void EmitWithDetail(string kind, ApiDirection direction, string path, HttpMethodKind method,
        string location, string? oldValue, string? newValue, Dictionary<string, string> details)
    {
        _changes.Add(new SemanticChange
        {
            Kind = kind,
            Category = CategoryFor(direction),
            Path = path,
            HttpMethod = method,
            Location = location,
            Direction = direction,
            OldValue = oldValue,
            NewValue = newValue,
            Details = details.Count == 0 ? FrozenMap.Empty : new FrozenMap(details),
        });
    }

    private static FindingCategory CategoryFor(ApiDirection direction) => direction switch
    {
        ApiDirection.Request => FindingCategory.RequestBody,
        ApiDirection.Response => FindingCategory.Response,
        _ => FindingCategory.Schema,
    };

    internal static string ChildLocation(string location, string child)
        => string.IsNullOrEmpty(location) ? child : $"{location}.{child}";

    private static bool IsRequired(SchemaInfo schema, string property)
    {
        var req = schema.Node as JsonObject;
        if (req?.TryGetPropertyValue("required", out var r) == true && r is JsonArray arr)
            foreach (var item in arr)
                if (item?.GetValue<string>() == property) return true;
        return false;
    }

    private static JsonObject? GetProperties(JsonNode node)
    {
        if (node is JsonObject o && o.TryGetPropertyValue("properties", out var p) && p is JsonObject po)
            return po;
        return null;
    }

    private static List<JsonNode>? GetArrayValues(JsonNode node, string keyword)
    {
        if (node is JsonObject o && o.TryGetPropertyValue(keyword, out var v) && v is JsonArray arr)
        {
            var list = new List<JsonNode>();
            foreach (var i in arr)
                if (i is not null) list.Add(i);
            return list;
        }
        return null;
    }

    private static HashSet<string>? GetStringSet(JsonNode node, string keyword)
    {
        if (node is not JsonObject o || !o.TryGetPropertyValue(keyword, out var v) || v is not JsonArray arr)
            return null;
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var i in arr)
            if (i?.GetValue<string>() is { } s) set.Add(s);
        return set;
    }

    private static bool? GetBool(JsonObject o, string keyword)
    {
        if (o.TryGetPropertyValue(keyword, out var v) && v is JsonValue bv && bv.TryGetValue<bool>(out var b))
            return b;
        return null;
    }

    private static SchemaInfo? Wrap(JsonNode? node) =>
        node is null ? null : new SchemaInfo { Node = node, Spec = SpecVersion.Unknown };

    private static bool HasItems(JsonNode node) =>
        node is JsonObject o && o.TryGetPropertyValue("items", out var i) && i is not null;

    private static readonly string[] ProseKeys = ["description", "title", "example", "examples"];

    /// <summary>Deep clone without documentation-only keywords. Used to keep prose-only
    /// drift from opening container-level change gates (§55 false-positive control).</summary>
    internal static JsonNode? StripProse(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                var copy = new JsonObject();
                foreach (var (key, value) in obj)
                {
                    if (ProseKeys.Contains(key, StringComparer.Ordinal)) continue;
                    copy[key] = StripProse(value);
                }
                return copy;
            }
            case JsonArray arr:
            {
                var copy = new JsonArray();
                foreach (var item in arr) copy.Add(StripProse(item));
                return copy;
            }
            default:
                return node?.DeepClone();
        }
    }

    /// <summary>Order-insensitive deep JSON equality.</summary>
    public static bool JsonDeepEquals(JsonNode? a, JsonNode? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;

        switch (a, b)
        {
            case (JsonObject oa, JsonObject ob):
                if (oa.Count != ob.Count) return false;
                foreach (var (key, value) in oa)
                {
                    if (!ob.TryGetPropertyValue(key, out var other)) return false;
                    if (!JsonDeepEquals(value, other)) return false;
                }
                return true;
            case (JsonArray aa, JsonArray ab):
                if (aa.Count != ab.Count) return false;
                // Arrays compared order-sensitively except for enum-like sets; keep strict here.
                for (var i = 0; i < aa.Count; i++)
                    if (!JsonDeepEquals(aa[i], ab[i])) return false;
                return true;
            default:
                return System.Text.Json.JsonSerializer.Serialize(a, SemanticChange.SerializerOptions)
                       == System.Text.Json.JsonSerializer.Serialize(b, SemanticChange.SerializerOptions);
        }
    }

    private sealed class ValueEqualityComparer : IEqualityComparer<JsonNode?>
    {
        public static readonly ValueEqualityComparer Instance = new();

        public bool Equals(JsonNode? x, JsonNode? y) => JsonDeepEquals(x, y);

        public int GetHashCode(JsonNode? obj) =>
            obj is null ? 0 : System.Text.Json.JsonSerializer.Serialize(obj, SemanticChange.SerializerOptions).GetHashCode(StringComparison.Ordinal);
    }

    private static IOrderedEnumerable<string> Ordered(IEnumerable<string> source) =>
        source.OrderBy(x => x, StringComparer.Ordinal);

    private static string? StringOrNull(string? value) => value;

    private static string? Truncate(string? s) =>
        s is null || s.Length <= 200 ? s : s[..197] + "...";
}
