using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenApiSentinel.Bruno.Model;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace OpenApiSentinel.Bruno.Validation;

/// <summary>One schema violation with a precise, user-facing location.</summary>
public sealed record OpenCollectionValidationError(string Path, string Message)
{
    public string Format() => string.IsNullOrEmpty(Path) ? Message : $"{Path} {Message}";
}

/// <summary>Thrown when a generated collection violates the official OpenCollection schema.</summary>
public sealed class OpenCollectionValidationException : Exception
{
    public OpenCollectionValidationException(IReadOnlyList<OpenCollectionValidationError> errors)
        : base(FormatMessage(errors))
    {
        Errors = errors;
    }

    public IReadOnlyList<OpenCollectionValidationError> Errors { get; }

    public static string FormatMessage(IReadOnlyList<OpenCollectionValidationError> errors) =>
        "OpenCollection validation failed:\n" + string.Join("\n",
            errors.Take(MaxReportedErrors).Select(e => $"  - {e.Format()}")) +
        (errors.Count > MaxReportedErrors ? $"\n  ... and {errors.Count - MaxReportedErrors} more." : "");

    public const int MaxReportedErrors = 20;
}

/// <summary>
/// Validates OpenCollection YAML against the OFFICIAL pinned v1.0 JSON Schema
/// (draft-07 subset used by the published schema: type/enum/const/required/
/// properties/additionalProperties/items/oneOf/$ref). The schema ships as an
/// embedded resource so validation never depends on the network.
/// </summary>
public static class OpenCollectionSchemaValidator
{
    public const string SchemaVersion = "1.0.0";

    private static readonly Lazy<JsonObject> Schema = new(LoadSchema);

    public static (bool IsValid, IReadOnlyList<OpenCollectionValidationError> Errors) Validate(
        YamlMappingNode root, string? definition = null)
    {
        var schema = definition is null
            ? Schema.Value
            : Schema.Value.TryGetPropertyValue("$defs", out var defs) && defs is JsonObject defsMap &&
              defsMap.TryGetPropertyValue(definition, out var def) && def is { }
                ? def
                : throw new BrunoFormatException($"Pinned OpenCollection schema has no definition '$defs.{definition}'.");
        var ctx = new ValidationContext(Schema.Value);
        var errors = new List<OpenCollectionValidationError>();
        ValidateNode(ConvertToJson(root), schema, "$", errors, ctx);
        return (errors.Count == 0, errors);
    }

    public static (bool IsValid, IReadOnlyList<OpenCollectionValidationError> Errors) Validate(
        string yaml, string sourceName, string? definition = null)
    {
        var parsed = BrunoYaml.ParseDocument(yaml, sourceName);
        return Validate(parsed, definition);
    }

    // ------------------------------------------------------------------
    // YAML → JSON conversion. Scalar styles matter: a PLAIN scalar is resolved
    // with the YAML 1.2 core schema exactly like every standard parser does, so
    // `value: 1` becomes a JSON number and correctly FAILS the schema's
    // `"type": "string"`. Styled scalars (quoted/literal/folded) are strings.
    // ------------------------------------------------------------------
    internal static JsonNode? ConvertToJson(YamlNode node) => node switch
    {
        YamlScalarNode scalar => ConvertScalar(scalar),
        YamlSequenceNode seq => new JsonArray([.. seq.Children.Select(child => ConvertToJson(child))]),
        YamlMappingNode map => ConvertMapping(map),
        _ => throw new BrunoFormatException($"Unsupported YAML node type '{node.GetType().Name}'."),
    };

    private static JsonObject ConvertMapping(YamlMappingNode map)
    {
        var obj = new JsonObject();
        foreach (var entry in map.Children)
        {
            var key = entry.Key is YamlScalarNode { Value: not null } k ? k.Value : entry.Key.ToString();
            obj[key] = ConvertToJson(entry.Value);
        }

        return obj;
    }

    private static JsonValue? ConvertScalar(YamlScalarNode scalar)
    {
        var text = scalar.Value ?? "";
        if (scalar.Style is not ScalarStyle.Any and not ScalarStyle.Plain)
        {
            return JsonValue.Create(text);
        }

        return ResolvePlainScalar(text) switch
        {
            NullSentinel => null,
            true => JsonValue.Create(true),
            false => JsonValue.Create(false),
            long l => JsonValue.Create(l),
            double d => double.IsFinite(d) ? JsonValue.Create(d) : JsonValue.Create(text),
            _ => JsonValue.Create(text),
        };
    }

    /// <summary>Marks plain-null resolution without clashing with the string "" result.</summary>
    private sealed class NullSentinel
    {
        public static readonly NullSentinel Instance = new();
        private NullSentinel() { }
    }

    internal static object? ResolvePlainScalarForTests(string text) => ResolvePlainScalar(text);

    private static readonly System.Text.RegularExpressions.Regex IntRegex =
        new(@"^[-+]?[0-9]+$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex FloatRegex =
        new(@"^[-+]?(\.[0-9]+|[0-9]+(\.[0-9]*)?)([eE][-+]?[0-9]+)?$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static object? ResolvePlainScalar(string text) => text switch
    {
        "" or "~" or "null" or "Null" or "NULL" => NullSentinel.Instance,
        "true" or "True" or "TRUE" => true,
        "false" or "False" or "FALSE" => false,
        ".inf" or ".Inf" or ".INF" or "+.inf" => double.PositiveInfinity,
        "-.inf" or "-.Inf" or "-.INF" => double.NegativeInfinity,
        ".nan" or ".NaN" or ".NAN" => double.NaN,
        _ when IntRegex.IsMatch(text) => long.Parse(text, System.Globalization.CultureInfo.InvariantCulture),
        _ when FloatRegex.IsMatch(text) &&
               double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) => f,
        _ => text,
    };

    // ------------------------------------------------------------------
    // Draft-07 subset validation
    // ------------------------------------------------------------------

    private sealed class ValidationContext(JsonObject root)
    {
        public JsonObject Root { get; } = root;

        private readonly Dictionary<string, JsonNode?> refCache = new(StringComparer.Ordinal);

        public JsonNode? ResolveRef(string reference)
        {
            if (!reference.StartsWith('#'))
            {
                throw new BrunoFormatException($"Pinned OpenCollection schema uses an unsupported external $ref: '{reference}'.");
            }

            if (refCache.TryGetValue(reference, out var cached))
            {
                return cached;
            }

            var current = (JsonNode)Root;
            foreach (var segment in reference.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (segment == "#")
                {
                    continue;
                }

                var decoded = Uri.UnescapeDataString(segment.Replace("~1", "/").Replace("~0", "~"));
                current = current is JsonObject o && o.TryGetPropertyValue(decoded, out var next) ? next : null;
                if (current is null)
                {
                    break;
                }
            }

            refCache[reference] = current;
            return current;
        }
    }

    private static void ValidateNode(
        JsonNode? node,
        JsonNode schema,
        string path,
        List<OpenCollectionValidationError> errors,
        ValidationContext ctx)
    {
        if (schema is JsonObject map)
        {
            if (map.TryGetPropertyValue("$ref", out var refNode) && refNode is JsonValue refValue &&
                refValue.TryGetValue<string>(out var reference))
            {
                var target = ctx.ResolveRef(reference);
                if (target is not null)
                {
                    ValidateNode(node, target, path, errors, ctx);
                    return;
                }
            }

            var hasType = map.TryGetPropertyValue("type", out var typeNode);
            var hasEnum = map.TryGetPropertyValue("enum", out var enumNode);
            var hasConst = map.TryGetPropertyValue("const", out var constNode);
            var hasOneOf = map.TryGetPropertyValue("oneOf", out var oneOfNode);

            if (hasOneOf && oneOfNode is JsonArray branches)
            {
                ValidateOneOf(node, branches, path, errors, ctx);
                return;
            }

            var branchErrors = new List<OpenCollectionValidationError>();
            if (hasType)
            {
                CheckType(node, typeNode!, path, branchErrors);
            }

            var typeOk = branchErrors.Count == 0;
            if (typeOk && hasEnum && enumNode is JsonArray allowed)
            {
                CheckEnum(node!, allowed, path, branchErrors);
            }

            if (typeOk && hasConst && constNode is { } constant)
            {
                CheckConst(node, constant, path, branchErrors);
            }

            if (typeOk && node is JsonObject obj)
            {
                ValidateObject(obj, map, path, branchErrors, ctx);
            }
            else if (typeOk && node is JsonArray arr)
            {
                if (map.TryGetPropertyValue("items", out var itemsSchema) && itemsSchema is not null)
                {
                    for (var i = 0; i < arr.Count; i++)
                    {
                        ValidateNode(arr[i], itemsSchema, $"{path}[{i}]", branchErrors, ctx);
                    }
                }
            }

            errors.AddRange(branchErrors);
            return;
        }

        // Boolean schemas: {} / true accept anything; false accepts nothing.
        if (schema is JsonValue v && v.TryGetValue<bool>(out var boolSchema) && !boolSchema)
        {
            errors.Add(new(path, "is not allowed here."));
        }
    }

    private static void ValidateOneOf(
        JsonNode? node,
        JsonArray branches,
        string path,
        List<OpenCollectionValidationError> errors,
        ValidationContext ctx)
    {
        var perBranch = new List<List<OpenCollectionValidationError>>();
        var passing = 0;
        foreach (var branch in branches)
        {
            if (branch is null)
            {
                continue;
            }

            var branchErrors = new List<OpenCollectionValidationError>();
            ValidateNode(node, branch, path, branchErrors, ctx);
            perBranch.Add(branchErrors);
            if (branchErrors.Count == 0)
            {
                passing++;
            }
        }

        if (passing == 0 && perBranch.Count > 0)
        {
            // Report the branch that got closest (fewest violations) so diagnostics stay actionable.
            var best = perBranch.MinBy(b => b.Count)!;
            errors.AddRange(best);
        }
        else if (passing > 1)
        {
            // Overlapping branches are ambiguous but not invalid input — accept.
        }
    }

    private static void CheckType(JsonNode? node, JsonNode typeNode, string path, List<OpenCollectionValidationError> errors)
    {
        IEnumerable<string> expected = typeNode switch
        {
            JsonValue sv when sv.TryGetValue<string>(out var t) => [t],
            JsonArray tv => tv.Where(n => n is JsonValue).Select(n => n!.GetValue<string>() ?? ""),
            _ => [],
        };

        var actual = ActualKind(node);
        // JSON Schema draft-07: "integer" is a subset of "number".
        if (expected.Contains(actual, StringComparer.Ordinal) ||
            (actual == "integer" && expected.Contains("number", StringComparer.Ordinal)))
        {
            return;
        }

        errors.Add(new(path, $"must be {string.Join(" or ", expected)} (found {actual})."));
    }

    private static string ActualKind(JsonNode? node) => node switch
    {
        null => "null",
        JsonObject => "object",
        JsonArray => "array",
        JsonValue v when v.TryGetValue<bool>(out _) => "boolean",
        JsonValue v when v.TryGetValue<long>(out _) => "integer",
        JsonValue v when IsJsonNumber(v) => "number",
        _ => "string",
    };

    private static bool IsJsonNumber(JsonValue v) =>
        v.TryGetValue<decimal>(out _) || v.TryGetValue<double>(out _);

    private static void CheckEnum(JsonNode node, JsonArray allowed, string path, List<OpenCollectionValidationError> errors)
    {
        if (allowed.Any(option => option is not null && JsonEquals(option!, node)))
        {
            return;
        }

        var options = string.Join(", ", allowed.Select(o => o?.ToJsonString() ?? "null"));
        errors.Add(new(path, $"must be one of: {options} (found {(node is JsonValue v ? v.ToJsonString() : "null")})."));
    }

    private static void CheckConst(JsonNode? node, JsonNode constant, string path, List<OpenCollectionValidationError> errors)
    {
        if (node is null || !JsonEquals(constant, node))
        {
            errors.Add(new(path, $"must equal {constant.ToJsonString()}."));
        }
    }

    private static void ValidateObject(
        JsonObject obj,
        JsonObject schemaMap,
        string path,
        List<OpenCollectionValidationError> errors,
        ValidationContext ctx)
    {
        var properties = schemaMap.TryGetPropertyValue("properties", out var props) && props is JsonObject p ? p : null;
        var additionalProperties = schemaMap.TryGetPropertyValue("additionalProperties", out var apNode) ? apNode : null;

        if (schemaMap.TryGetPropertyValue("required", out var requiredNode) && requiredNode is JsonArray required)
        {
            foreach (var req in required)
            {
                if (req is JsonValue rv && rv.TryGetValue<string>(out var name) && !obj.ContainsKey(name))
                {
                    errors.Add(new(JoinPath(path, name), "is required"));
                }
            }
        }

        if (properties is not null)
        {
            foreach (var prop in properties)
            {
                if (prop.Value is { } propSchema && obj.TryGetPropertyValue(prop.Key, out var value))
                {
                    ValidateNode(value, propSchema, JoinPath(path, prop.Key), errors, ctx);
                }
            }
        }

        if (additionalProperties is JsonValue apBool && apBool.TryGetValue<bool>(out var allowAdditional) && !allowAdditional)
        {
            foreach (var key in obj.Select(kvp => kvp.Key))
            {
                if (properties is null || !properties.ContainsKey(key))
                {
                    var allowedList = properties is null ? "(none)" : string.Join(", ", properties.Select(p => p.Key).Order(StringComparer.Ordinal));
                    errors.Add(new(JoinPath(path, key), $"is not an allowed property here. Allowed: {allowedList}."));
                }
            }
        }
        else if (additionalProperties is { } apSchema && apSchema is not JsonValue)
        {
            foreach (var entry in obj)
            {
                if (properties is null || !properties.ContainsKey(entry.Key))
                {
                    ValidateNode(entry.Value, apSchema, JoinPath(path, entry.Key), errors, ctx);
                }
            }
        }
    }

    private static string JoinPath(string parent, string key) =>
        parent == "$" ? key : $"{parent}.{key}";

    private static bool JsonEquals(JsonNode a, JsonNode b)
    {
        if (a is JsonValue av && b is JsonValue bv)
        {
            var ak = ActualKind(av);
            var bk = ActualKind(bv);
            if (ak != bk)
            {
                // integer vs number compare numerically
                if ((ak is "integer" or "number") && bk is "integer" or "number")
                {
                    return ToDecimal(av) == ToDecimal(bv);
                }

                return false;
            }

            return string.Equals(NormalizeScalar(av), NormalizeScalar(bv), StringComparison.Ordinal);
        }

        return a.ToJsonString(JsonOptionsComparable) == b.ToJsonString(JsonOptionsComparable);
    }

    private static decimal? ToDecimal(JsonValue v) =>
        v.TryGetValue<decimal>(out var d) ? d : null;

    private static string NormalizeScalar(JsonValue v) =>
        v.TryGetValue<string>(out var s) ? s : v.ToJsonString();

    private static readonly JsonSerializerOptions JsonOptionsComparable = new();

    private static JsonObject LoadSchema()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"{assembly.GetName().Name}.Validation.opencollection.schema.json";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new BrunoFormatException($"Embedded OpenCollection schema resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        var parsed = JsonNode.Parse(reader.ReadToEnd());
        return parsed as JsonObject
            ?? throw new BrunoFormatException("Embedded OpenCollection schema resource did not parse into a JSON object.");
    }
}
