using System.Text.Json.Nodes;
using OpenApiSentinel.OpenApi.Model;

namespace OpenApiSentinel.Bruno;

/// <summary>Deterministic sample and violation values derived from an OpenAPI schema.</summary>
public static class SampleValueFactory
{
    /// <summary>How much of an object schema a generated payload covers.</summary>
    public enum Depth
    {
        /// <summary>All properties (required + optional) — mirrors 「完整參數」.</summary>
        Full,
        /// <summary>Only required properties.</summary>
        RequiredOnly,
    }

    public static JsonNode? Sample(SchemaInfo? schema, Depth depth = Depth.Full, int level = 0)
    {
        if (schema is null || level > 8)
        {
            return null;
        }

        // example / examples / default win over synthesized values.
        if (schema.TryGetKeyword("example", out var example) && example is not null)
        {
            return example.DeepClone();
        }

        if (schema.TryGetKeyword("examples", out var examplesNode) &&
            examplesNode is JsonArray examples &&
            examples.Count > 0 && examples[0] is { } first)
        {
            return first.DeepClone();
        }

        if (schema.TryGetKeyword("default", out var def) && def is not null)
        {
            return def.DeepClone();
        }

        // Declared enums are authoritative: the first value is a valid sample.
        if (HasNonEmptyEnum(schema))
        {
            var enumValue = FirstEnumValue(schema);
            if (enumValue is not null)
            {
                return enumValue;
            }
        }

        var primary = NonNullPrimaryType(schema);
        switch (primary)
        {
            case "object":
                return ObjectSample(schema, depth, level);
            case "array":
                return ArraySample(schema, depth, level);
            case "string":
                return JsonValue.Create(SampleString(schema));
            case "integer":
                return JsonValue.Create(SampleInteger(schema));
            case "number":
                return JsonValue.Create(SampleNumber(schema));
            case "boolean":
                return schema.TryGetKeyword("default", out var boolDef) &&
                       boolDef is JsonValue bv && bv.TryGetValue<bool>(out var b)
                    ? JsonValue.Create(b)
                    : JsonValue.Create(false);
            case "null":
                return JsonValue.Create<string?>(null);
            default:
                return JsonValue.Create("sample");
        }
    }

    private static string? NonNullPrimaryType(SchemaInfo schema)
    {
        foreach (var t in schema.EffectiveTypes())
        {
            if (t != "null")
            {
                return t;
            }
        }

        return null;
    }

    private static JsonObject ObjectSample(SchemaInfo schema, Depth depth, int level)
    {
        // Composition: prefer the first oneOf/anyOf branch; allOf handled by merged property view.
        if (schema.CompositionOf("oneOf") is { Count: > 0 } oneOf && oneOf[0] is JsonObject oneBranch)
        {
            return ObjectSample(Wrap(oneBranch, schema.Spec), depth, level + 1);
        }

        if (schema.CompositionOf("anyOf") is { Count: > 0 } anyOf && anyOf[0] is JsonObject anyBranch)
        {
            return ObjectSample(Wrap(anyBranch, schema.Spec), depth, level + 1);
        }

        var merged = MergeAllOf(schema);

        var result = new JsonObject();
        foreach (var (name, propertySchema) in MergedProperties(merged))
        {
            var isRequired = MergedRequired(merged).Contains(name);
            if (depth == Depth.RequiredOnly && !isRequired)
            {
                continue;
            }

            var value = Sample(propertySchema, depth == Depth.RequiredOnly ? Depth.RequiredOnly : Depth.Full, level + 1);
            if (value is not null)
            {
                result[name] = value;
            }
            else if (isRequired)
            {
                result[name] = JsonValue.Create("sample");
            }
        }

        return result;
    }

    internal static SchemaInfo Wrap(JsonNode node, SpecVersion spec) => new() { Node = node, Spec = spec };

    internal static SchemaInfo MergeAllOf(SchemaInfo schema)
    {
        if (schema.CompositionOf("allOf") is not { Count: > 0 })
        {
            return schema;
        }

        var merged = new JsonObject();
        var spec = schema.Spec;
        foreach (var branch in schema.CompositionOf("allOf")!)
        {
            if (branch is not JsonObject branchObj)
            {
                continue;
            }

            var branchMerged = MergeAllOf(Wrap(branchObj, spec));
            if (branchMerged.Node is JsonObject flat)
            {
                foreach (var property in flat)
                {
                    if (property.Key is not ("allOf" or "oneOf" or "anyOf"))
                    {
                        merged[property.Key] = property.Value?.DeepClone();
                    }
                }
            }

            // Flatten nested compositions of the branch too.
            if (branchObj.TryGetPropertyValue("allOf", out var innerAll) && innerAll is JsonArray)
            {
                foreach (var sub in MergeAllOf(Wrap(branchObj, spec)).CompositionOf("allOf") ?? [])
                {
                    if (sub is JsonObject subObj)
                    {
                        foreach (var property in subObj.Where(p => p.Key is not ("allOf" or "oneOf" or "anyOf")))
                        {
                            merged[property.Key] = property.Value?.DeepClone();
                        }
                    }
                }
            }
        }

        return Wrap(merged, spec);
    }

    internal static IEnumerable<KeyValuePair<string, SchemaInfo>> MergedProperties(SchemaInfo schema)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (schema.Node is JsonObject obj &&
            obj.TryGetPropertyValue("properties", out var props) && props is JsonObject propsObj)
        {
            foreach (var property in propsObj)
            {
                if (seen.Add(property.Key))
                {
                    yield return new(property.Key, Wrap(property.Value!, schema.Spec));
                }
            }
        }

        if (schema.CompositionOf("allOf") is { } branches)
        {
            foreach (var branch in branches)
            {
                if (branch is not JsonObject)
                {
                    continue;
                }

                foreach (var nested in MergedProperties(MergeAllOf(Wrap(branch, schema.Spec))))
                {
                    if (seen.Add(nested.Key))
                    {
                        yield return nested;
                    }
                }
            }
        }
    }

    internal static IReadOnlySet<string> MergedRequired(SchemaInfo schema)
    {
        var required = new HashSet<string>(StringComparer.Ordinal);
        CollectRequired(schema, required);
        return required;
    }

    private static void CollectRequired(SchemaInfo schema, ISet<string> into)
    {
        if (schema.Node is JsonObject obj &&
            obj.TryGetPropertyValue("required", out var req) && req is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item?.GetValue<string>() is { Length: > 0 } name)
                {
                    into.Add(name);
                }
            }
        }

        if (schema.CompositionOf("allOf") is { } branches)
        {
            foreach (var branch in branches)
            {
                if (branch is JsonObject)
                {
                    CollectRequired(Wrap(branch, schema.Spec), into);
                }
            }
        }
    }

    private static JsonArray ArraySample(SchemaInfo schema, Depth depth, int level)
    {
        var result = new JsonArray();
        var minItems = IntKeyword(schema, "minItems") ?? 0;
        var count = Math.Max(1, Math.Min(minItems, 3));
        JsonNode? elementSchema = null;
        if (schema.Node is JsonObject obj &&
            obj.TryGetPropertyValue("items", out var items) && items is not null && items is not JsonArray)
        {
            elementSchema = items;
        }

        for (var i = 0; i < count; i++)
        {
            var element = Sample(elementSchema is null ? null : Wrap(elementSchema, schema.Spec), depth, level + 1);
            result.Add(element ?? JsonValue.Create("sample"));
        }

        return result;
    }

    public static long SampleInteger(SchemaInfo schema)
    {
        var min = IntKeyword(schema, "minimum");
        var max = IntKeyword(schema, "maximum");
        var exclusiveMin = IntKeyword(schema, "exclusiveMinimum");
        var exclusiveMax = IntKeyword(schema, "exclusiveMaximum");

        long candidate = 1;
        if (min.HasValue)
        {
            candidate = Math.Max(candidate, min.Value);
        }

        if (exclusiveMin.HasValue)
        {
            candidate = Math.Max(candidate, exclusiveMin.Value + 1);
        }

        if (max.HasValue && candidate > max.Value)
        {
            candidate = max.Value;
        }

        if (exclusiveMax.HasValue && candidate >= exclusiveMax.Value)
        {
            candidate = exclusiveMax.Value - 1;
        }

        if (candidate < 0 && !min.HasValue && !exclusiveMin.HasValue)
        {
            candidate = 1;
        }

        return candidate;
    }

    public static double SampleNumber(SchemaInfo schema)
    {
        var min = DoubleKeyword(schema, "minimum");
        var max = DoubleKeyword(schema, "maximum");
        double candidate = 1;
        if (min.HasValue)
        {
            candidate = Math.Max(candidate, min.Value);
        }

        if (max.HasValue && candidate > max.Value)
        {
            candidate = max.Value;
        }

        return candidate;
    }

    public static string SampleString(SchemaInfo schema)
    {
        var format = schema.Format?.ToLowerInvariant();
        var text = format switch
        {
            "date" => "2026-01-01",
            "date-time" => "2026-01-01T00:00:00Z",
            "time" => "00:00:00Z",
            "email" or "idn-email" => "user@example.com",
            "uuid" => "00000000-0000-0000-0000-000000000000",
            "uri" or "uri-reference" or "iri" => "https://example.com/resource",
            "hostname" or "idn-hostname" => "api.example.com",
            "ipv4" => "192.0.2.1",
            "ipv6" => "2001:db8::1",
            "byte" => "c2FtcGxl",
            "binary" => "binary-content",
            "password" => "sample-password",
            _ => "sample",
        };

        var minLength = IntKeyword(schema, "minLength");
        if (minLength is { } lower && text.Length < lower)
        {
            text = text.Length > 0
                ? text + new string('a', lower - text.Length)
                : new string('a', lower);
        }

        var maxLength = IntKeyword(schema, "maxLength");
        if (maxLength is { } upper && text.Length > upper && upper >= 0)
        {
            text = text[..Math.Max(0, upper)];
        }

        return text;
    }

    /// <summary>Deterministic string sample for a parameter (used in URLs, params, headers).</summary>
    public static string ParameterSampleText(ParameterInfo parameter)
    {
        if (parameter.Schema is null)
        {
            return "sample";
        }

        var value = Sample(parameter.Schema, Depth.Full, level: 2);
        return value switch
        {
            System.Text.Json.Nodes.JsonValue v when v.TryGetValue<string>(out var s) => s,
            null => "sample",
            _ => value.ToJsonString(),
        };
    }

    /// <summary>A value violating the declared string type/format for validation-failure scenarios.</summary>
    public static JsonNode ViolatingValue(SchemaInfo schema, ViolationKind kind)
    {
        var primary = NonNullPrimaryType(schema);
        return kind switch
        {
            ViolationKind.WrongType => primary switch
            {
                "string" => JsonValue.Create(12345),
                "integer" or "number" => JsonValue.Create("not-a-number"),
                "boolean" => JsonValue.Create("not-a-boolean"),
                "array" => JsonValue.Create("not-an-array"),
                "object" => new JsonArray(),
                _ => JsonValue.Create("sentinel-type-probe"),
            },
            ViolationKind.InvalidEnum => JsonValue.Create("__sentinel_invalid_enum__"),
            ViolationKind.BelowMinimum => JsonValue.Create(LongKeywordLoose(schema, "minimum") is { } min ? min - 1 : (LongKeywordLoose(schema, "exclusiveMinimum") ?? 0) - 1),
            ViolationKind.AboveMaximum => JsonValue.Create(LongKeywordLoose(schema, "maximum") is { } max ? max + 1 : (LongKeywordLoose(schema, "exclusiveMaximum") ?? 0) + 1),
            ViolationKind.MaxLengthExceeded =>
                JsonValue.Create(new string('a', Math.Max(1, (IntKeyword(schema, "maxLength") ?? 1) + 1))),
            ViolationKind.MinLengthViolated => JsonValue.Create(string.Empty),
            ViolationKind.PatternViolation => JsonValue.Create(PatternViolator.FindViolatingSample(schema) ?? "__sentinel_pattern_probe__"),
            ViolationKind.InvalidFormat => JsonValue.Create(InvalidFormatSample(schema.Format)),
            ViolationKind.TooFewItems => new JsonArray(),
            ViolationKind.TooManyItems => OversizedArray(schema),
            _ => JsonValue.Create("__sentinel_probe__"),
        };
    }

    private static JsonArray OversizedArray(SchemaInfo schema)
    {
        var maxItems = IntKeyword(schema, "maxItems") ?? 0;
        var arr = new JsonArray();
        for (var i = 0; i <= Math.Max(0, maxItems); i++)
        {
            arr.Add(JsonValue.Create("item"));
        }

        return arr;
    }

    private static long? LongKeywordLoose(SchemaInfo schema, string keyword)
    {
        if (!schema.TryGetKeyword(keyword, out var node) || node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<long>(out var l))
        {
            return l;
        }

        if (value.TryGetValue<double>(out var d))
        {
            return (long)Math.Floor(d);
        }

        return null;
    }

    public static string InvalidFormatSample(string? format) => format?.ToLowerInvariant() switch
    {
        "email" or "idn-email" => "not-an-email",
        "date" => "31-12-2026",
        "date-time" => "not-a-date-time",
        "time" => "not-a-time",
        "uuid" => "not-a-uuid",
        "uri" or "uri-reference" or "iri" => ":: not a uri",
        "hostname" or "idn-hostname" => "-bad host-",
        "ipv4" => "999.999.999.999",
        "ipv6" => "not-an-ipv6",
        _ => "invalid-format-value",
    };

    public static int? IntKeyword(SchemaInfo schema, string keyword) =>
        LongKeywordLoose(schema, keyword) is { } v && v is >= int.MinValue and <= int.MaxValue ? (int)v : null;

    private static double? DoubleKeyword(SchemaInfo schema, string keyword)
    {
        if (!schema.TryGetKeyword(keyword, out var node) || node is not JsonValue value)
        {
            return null;
        }

        return value.TryGetValue<double>(out var d) ? d : null;
    }

    /// <summary>True when the schema declares a constraint that enables this violation category.</summary>
    public static bool SupportsViolation(SchemaInfo schema, ViolationKind kind)
    {
        var primary = NonNullPrimaryType(schema);
        return kind switch
        {
            ViolationKind.WrongType => primary is not null,
            ViolationKind.InvalidEnum => HasNonEmptyEnum(schema),
            ViolationKind.BelowMinimum => primary is "integer" or "number" &&
                (LongKeywordLoose(schema, "minimum").HasValue || LongKeywordLoose(schema, "exclusiveMinimum").HasValue),
            ViolationKind.AboveMaximum => primary is "integer" or "number" &&
                (LongKeywordLoose(schema, "maximum").HasValue || LongKeywordLoose(schema, "exclusiveMaximum").HasValue),
            ViolationKind.BoundaryMinimum => primary is "integer" or "number" &&
                (LongKeywordLoose(schema, "minimum").HasValue || LongKeywordLoose(schema, "exclusiveMinimum").HasValue),
            ViolationKind.BoundaryMaximum => primary is "integer" or "number" &&
                (LongKeywordLoose(schema, "maximum").HasValue || LongKeywordLoose(schema, "exclusiveMaximum").HasValue),
            ViolationKind.MaxLengthExceeded => primary == "string" && IntKeyword(schema, "maxLength") > 0,
            ViolationKind.MinLengthViolated => primary == "string" && IntKeyword(schema, "minLength") > 0,
            ViolationKind.BoundaryMinLength => primary == "string" && IntKeyword(schema, "minLength") > 0,
            ViolationKind.BoundaryMaxLength => primary == "string" && IntKeyword(schema, "maxLength") > 0,
            ViolationKind.PatternViolation => primary == "string" &&
                schema.TryGetKeyword("pattern", out var p) && p is JsonValue &&
                PatternViolator.HasViolatingSample(schema),
            ViolationKind.InvalidFormat => primary == "string" &&
                schema.Format is { Length: > 0 } &&
                InvalidFormatSample(schema.Format) != SampleString(schema),
            ViolationKind.TooFewItems => primary == "array" && IntKeyword(schema, "minItems") > 0,
            ViolationKind.TooManyItems => primary == "array" && IntKeyword(schema, "maxItems") >= 0,
            _ => false,
        };
    }

    public static bool HasNonEmptyEnum(SchemaInfo schema)
    {
        // Enum lives on the schema or on anyOf/oneOf branches (first branch wins for probing).
        if (EnumValues(schema) is { Count: > 0 })
        {
            return true;
        }

        foreach (var keyword in (ReadOnlySpan<string>)["oneOf", "anyOf"])
        {
            if (schema.CompositionOf(keyword) is { Count: > 0 } branches && branches[0] is JsonObject branch &&
                EnumValues(Wrap(branch, schema.Spec)) is { Count: > 0 })
            {
                return true;
            }
        }

        return false;
    }

    public static IReadOnlyList<JsonNode?> EnumValues(SchemaInfo schema)
    {
        if (schema.TryGetKeyword("enum", out var enumNode) && enumNode is JsonArray arr)
        {
            return [.. arr];
        }

        return [];
    }

    /// <summary>The first declared enum value (deterministic).</summary>
    public static JsonNode? FirstEnumValue(SchemaInfo schema)
    {
        if (EnumValues(schema) is { Count: > 0 } values)
        {
            return values[0]?.DeepClone();
        }

        foreach (var keyword in (ReadOnlySpan<string>)["oneOf", "anyOf"])
        {
            if (schema.CompositionOf(keyword) is { Count: > 0 } branches && branches[0] is JsonObject branch)
            {
                var nested = FirstEnumValue(Wrap(branch, schema.Spec));
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    /// <summary>A valid value exactly on the declared minimum boundary (for 邊界值 scenarios).</summary>
    public static JsonNode? BoundaryValue(SchemaInfo schema, bool minimum)
    {
        var primary = NonNullPrimaryType(schema);
        if (primary is "integer" or "number")
        {
            if (minimum)
            {
                if (LongKeywordLoose(schema, "minimum") is { } min)
                {
                    return JsonValue.Create(min);
                }

                if (LongKeywordLoose(schema, "exclusiveMinimum") is { } exMin)
                {
                    return JsonValue.Create(exMin + 1);
                }
            }
            else
            {
                if (LongKeywordLoose(schema, "maximum") is { } max)
                {
                    return JsonValue.Create(max);
                }

                if (LongKeywordLoose(schema, "exclusiveMaximum") is { } exMax)
                {
                    return JsonValue.Create(exMax - 1);
                }
            }

            return null;
        }

        if (primary == "string")
        {
            var minLength = IntKeyword(schema, "minLength");
            var maxLength = IntKeyword(schema, "maxLength");
            if (minimum && minLength is { } lo)
            {
                return JsonValue.Create(new string('a', lo));
            }

            if (!minimum && maxLength is { } hi)
            {
                return JsonValue.Create(new string('b', hi));
            }
        }

        if (primary == "array")
        {
            var minItems = IntKeyword(schema, "minItems");
            var maxItems = IntKeyword(schema, "maxItems");
            var target = minimum ? minItems : maxItems;
            if (target is { } count && count >= 0 && count <= 16)
            {
                var arr = new JsonArray();
                for (var i = 0; i < count; i++)
                {
                    arr.Add(JsonValue.Create("item"));
                }

                return arr;
            }
        }

        return null;
    }
}

public enum ViolationKind
{
    WrongType,
    InvalidEnum,
    BelowMinimum,
    AboveMaximum,
    MaxLengthExceeded,
    MinLengthViolated,
    PatternViolation,
    InvalidFormat,
    TooFewItems,
    TooManyItems,
    BoundaryMinimum,
    BoundaryMaximum,
    BoundaryMinLength,
    BoundaryMaxLength,
}

/// <summary>Finds a string that provably violates (or satisfies) a declared regex pattern.</summary>
public static class PatternViolator
{
    private static readonly string[] Candidates =
    [
        "", "@@", "..", "###", "no spaces here", "!!!",
    ];

    public static bool HasViolatingSample(SchemaInfo schema) => FindViolatingSample(schema) is not null;

    public static string? FindViolatingSample(SchemaInfo schema)
    {
        if (!schema.TryGetKeyword("pattern", out var patternNode) ||
            patternNode is not System.Text.Json.Nodes.JsonValue value ||
            !value.TryGetValue<string>(out var pattern))
        {
            return null;
        }

        System.Text.RegularExpressions.Regex regex;
        try
        {
            regex = new System.Text.RegularExpressions.Regex(
                pattern,
                System.Text.RegularExpressions.RegexOptions.None,
                TimeSpan.FromMilliseconds(250));
        }
        catch (System.Text.RegularExpressions.RegexParseException)
        {
            return null;
        }
        catch (System.ArgumentException)
        {
            return null;
        }

        foreach (var candidate in Candidates)
        {
            try
            {
                if (!regex.IsMatch(candidate))
                {
                    return candidate;
                }
            }
            catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
            {
                return null;
            }
        }

        return null;
    }
}
