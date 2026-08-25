using System.Text.Json;
using System.Text.Json.Nodes;
using OpenApiSentinel.OpenApi.Model;

namespace OpenApiSentinel.Bruno;

public enum ScenarioCategory
{
    SuccessFull = 10,
    SuccessRequiredOnly = 20,
    MissingRequired = 60,
    InvalidEnum = 50,
    BelowMinimum = 70,
    AboveMaximum = 80,
    MaxLengthExceeded = 90,
    MinLengthViolated = 100,
    WrongType = 110,
    PatternViolation = 120,
    InvalidFormat = 130,
    TooFewItems = 140,
    TooManyItems = 150,
    BoundaryMinimum = 160,
    BoundaryMaximum = 170,
    BoundaryMinLength = 180,
    BoundaryMaxLength = 190,
    BoundaryItemsMin = 200,
    BoundaryItemsMax = 210,
}

/// <summary>A fully materialized HTTP request payload for one scenario.</summary>
public sealed record ScenarioRequest(
    string Method,
    string Url,
    IReadOnlyList<(string Name, string Value, string Type, bool Enabled)> Params,
    IReadOnlyList<(string Name, string Value, bool Enabled)> Headers,
    string? JsonBody);

/// <summary>One generated test scenario (Layer 1).</summary>
public sealed record GeneratedScenario(
    string Id,
    string Name,
    string Description,
    ScenarioCategory Category,
    ScenarioRequest Request);

/// <summary>
/// Deterministic Layer-1 scenario generator. Produces 「可直接在 Bruno 點擊 Send」的測試案例
/// derived exclusively from the OpenAPI contract, ranked and capped by the configured
/// maxPerOperation limit. Authentication is ignored by design.
/// </summary>
public sealed class ScenarioGenerator
{
    private static readonly JsonSerializerOptions BodyWriterOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public BrunoExampleSettings Options { get; }

    public ScenarioGenerator(BrunoExampleSettings options)
    {
        Options = options;
    }

    /// <summary>
    /// Generates the scenario set for one operation. <paramref name="baseRequest"/> is the
    /// engine-built base request (成功－完整參數 shape); scenarios are deltas over it.
    /// </summary>
    public IReadOnlyList<GeneratedScenario> Generate(OperationIdentity identity, Operation operation, ScenarioRequest baseRequest)
    {
        var candidates = new List<(int Rank, int FieldOrder, GeneratedScenario Scenario)>();

        // ---- Success scenarios ----
        candidates.Add((0, 0, new GeneratedScenario(
            "success-full",
            ScenarioTexts.SuccessFull(),
            ScenarioTexts.SuccessFullDescription(),
            ScenarioCategory.SuccessFull,
            baseRequest)));

        var hasOptional = HasOptionalContent(operation);
        if (hasOptional)
        {
            candidates.Add((0, 1, new GeneratedScenario(
                "success-required-only",
                ScenarioTexts.SuccessRequiredOnly(),
                ScenarioTexts.SuccessRequiredOnlyDescription(),
                ScenarioCategory.SuccessRequiredOnly,
                BuildRequiredOnly(identity, operation, baseRequest))));
        }

        // ---- Field-driven violation & boundary scenarios ----
        var fieldOrder = 0;
        foreach (var field in EnumerateFields(operation))
        {
            AddFieldScenarios(candidates, field, baseRequest, order: fieldOrder++);
        }

        return candidates
            .OrderBy(c => c.Rank)
            .ThenBy(c => c.FieldOrder)
            .Select(c => c.Scenario)
            .Take(Math.Max(0, Options.MaxPerOperation))
            .ToList();
    }

    private static void AddFieldScenarios(
        List<(int Rank, int FieldOrder, GeneratedScenario Scenario)> candidates,
        FieldRef field,
        ScenarioRequest baseRequest,
        int order)
    {
        var schema = field.Schema;
        var leaf = LeafName(field.DotPath);

        void Add(ScenarioCategory category)
        {
            var kind = MapToViolation(category);
            if (kind is null || !SampleValueFactory.SupportsViolation(schema, kind.Value))
            {
                return;
            }

            var mutated = ApplyValue(baseRequest, field, SampleValueFactory.ViolatingValue(schema, kind.Value));
            if (mutated is null)
            {
                return;
            }

            candidates.Add(((int)category, order, new GeneratedScenario(
                StableId(category, field.DotPath),
                ScenarioTexts.NameFor(category, leaf),
                ScenarioTexts.DescriptionFor(category, field.DotPath, schema),
                category,
                mutated)));
        }

        void AddMissing()
        {
            if (!field.Required || field.Location != FieldLocation.Body)
            {
                return; // query/header/path omission either breaks transport or equals optional absence
            }

            var mutated = RemoveBodyPath(baseRequest, field.DotPath);
            candidates.Add(((int)ScenarioCategory.MissingRequired, order, new GeneratedScenario(
                StableId(ScenarioCategory.MissingRequired, field.DotPath),
                ScenarioTexts.NameFor(ScenarioCategory.MissingRequired, leaf),
                ScenarioTexts.DescriptionFor(ScenarioCategory.MissingRequired, field.DotPath, schema),
                ScenarioCategory.MissingRequired,
                mutated)));
        }

        void AddBoundary(bool minimum, ScenarioCategory category)
        {
            // Gate each boundary category precisely so arrays do not duplicate under
            // length categories and vice versa.
            var primaryType = NonNullPrimary(schema);
            var allowed = category switch
            {
                ScenarioCategory.BoundaryMinimum => primaryType is "integer" or "number",
                ScenarioCategory.BoundaryMaximum => primaryType is "integer" or "number",
                ScenarioCategory.BoundaryMinLength => primaryType == "string" && SampleValueFactory.IntKeyword(schema, "minLength") > 0,
                ScenarioCategory.BoundaryMaxLength => primaryType == "string" && SampleValueFactory.IntKeyword(schema, "maxLength") > 0,
                _ => false,
            };

            if (!allowed)
            {
                return;
            }

            var value = category switch
            {
                ScenarioCategory.BoundaryItemsMin or ScenarioCategory.BoundaryItemsMax =>
                    RepeatArray((minimum ? SampleValueFactory.IntKeyword(schema, "minItems")
                                        : SampleValueFactory.IntKeyword(schema, "maxItems")) ?? 0),
                _ => SampleValueFactory.BoundaryValue(schema, minimum),
            };

            if (value is null)
            {
                return;
            }

            var mutated = ApplyValue(baseRequest, field, value);
            if (mutated is null)
            {
                return;
            }

            candidates.Add(((int)category, order, new GeneratedScenario(
                StableId(category, field.DotPath),
                ScenarioTexts.NameFor(category, leaf),
                ScenarioTexts.DescriptionFor(category, field.DotPath, schema),
                category,
                mutated)));
        }

        Add(ScenarioCategory.InvalidEnum);
        Add(ScenarioCategory.BelowMinimum);
        Add(ScenarioCategory.AboveMaximum);
        Add(ScenarioCategory.MaxLengthExceeded);
        Add(ScenarioCategory.MinLengthViolated);
        Add(ScenarioCategory.WrongType);
        Add(ScenarioCategory.PatternViolation);
        Add(ScenarioCategory.InvalidFormat);
        Add(ScenarioCategory.TooFewItems);
        Add(ScenarioCategory.TooManyItems);
        AddMissing();
        AddBoundary(minimum: true, ScenarioCategory.BoundaryMinimum);
        AddBoundary(minimum: false, ScenarioCategory.BoundaryMaximum);
        AddBoundary(minimum: true, ScenarioCategory.BoundaryMinLength);
        AddBoundary(minimum: false, ScenarioCategory.BoundaryMaxLength);

        // Array item-count boundaries reuse min/maxItems keywords directly.
        if (NonNullPrimary(schema) == "array")
        {
            var minItems = SampleValueFactory.IntKeyword(schema, "minItems");
            var maxItems = SampleValueFactory.IntKeyword(schema, "maxItems");
            if (minItems is { } lo && lo >= 0 && lo <= 16)
            {
                var mutated = ApplyValue(baseRequest, field, RepeatArray(lo));
                if (mutated is not null)
                {
                    candidates.Add(((int)ScenarioCategory.BoundaryItemsMin, order, new GeneratedScenario(
                        StableId(ScenarioCategory.BoundaryItemsMin, field.DotPath),
                        ScenarioTexts.NameFor(ScenarioCategory.BoundaryItemsMin, leaf),
                        ScenarioTexts.DescriptionFor(ScenarioCategory.BoundaryItemsMin, field.DotPath, schema),
                        ScenarioCategory.BoundaryItemsMin,
                        mutated)));
                }
            }

            if (maxItems is { } hi && hi >= 0 && hi <= 16)
            {
                var mutated = ApplyValue(baseRequest, field, RepeatArray(hi));
                if (mutated is not null)
                {
                    candidates.Add(((int)ScenarioCategory.BoundaryItemsMax, order, new GeneratedScenario(
                        StableId(ScenarioCategory.BoundaryItemsMax, field.DotPath),
                        ScenarioTexts.NameFor(ScenarioCategory.BoundaryItemsMax, leaf),
                        ScenarioTexts.DescriptionFor(ScenarioCategory.BoundaryItemsMax, field.DotPath, schema),
                        ScenarioCategory.BoundaryItemsMax,
                        mutated)));
                }
            }
        }
    }

    /// <summary>Maps a scenario category to the violation kind it exercises; null for non-violation categories.</summary>
    private static ViolationKind? MapToViolation(ScenarioCategory category) => category switch
    {
        ScenarioCategory.WrongType => ViolationKind.WrongType,
        ScenarioCategory.InvalidEnum => ViolationKind.InvalidEnum,
        ScenarioCategory.BelowMinimum => ViolationKind.BelowMinimum,
        ScenarioCategory.AboveMaximum => ViolationKind.AboveMaximum,
        ScenarioCategory.MaxLengthExceeded => ViolationKind.MaxLengthExceeded,
        ScenarioCategory.MinLengthViolated => ViolationKind.MinLengthViolated,
        ScenarioCategory.PatternViolation => ViolationKind.PatternViolation,
        ScenarioCategory.InvalidFormat => ViolationKind.InvalidFormat,
        ScenarioCategory.TooFewItems => ViolationKind.TooFewItems,
        ScenarioCategory.TooManyItems => ViolationKind.TooManyItems,
        _ => null,
    };

    private static JsonArray RepeatArray(int count)
    {
        var arr = new JsonArray();
        for (var i = 0; i < count; i++)
        {
            arr.Add(JsonValue.Create("item"));
        }

        return arr;
    }

    private static string? NonNullPrimary(SchemaInfo schema)
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

    private static bool HasOptionalContent(Operation operation)
    {
        foreach (var parameter in operation.Parameters)
        {
            if (!parameter.Required)
            {
                return true;
            }
        }

        if (operation.RequestBody?.Content.Count > 0)
        {
            var schema = PrimaryBodySchema(operation.RequestBody);
            if (schema is not null)
            {
                var merged = SampleValueFactory.MergeAllOf(schema);
                var required = SampleValueFactory.MergedRequired(merged);
                foreach (var property in SampleValueFactory.MergedProperties(merged))
                {
                    if (!required.Contains(property.Key))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    public static SchemaInfo? PrimaryBodySchema(RequestBodyInfo body)
    {
        foreach (var contentType in body.Content.Keys.Order(StringComparer.OrdinalIgnoreCase))
        {
            if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                return body.Content[contentType];
            }
        }

        foreach (var contentType in body.Content.Keys.Order(StringComparer.OrdinalIgnoreCase))
        {
            return body.Content[contentType];
        }

        return null;
    }

    /// <summary>The 成功－僅輸入必填參數 variant of the base request.</summary>
    private static ScenarioRequest BuildRequiredOnly(OperationIdentity identity, Operation operation, ScenarioRequest baseRequest)
    {
        var requiredParamNames = operation.Parameters.Where(p => p.Required).Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var paramsFiltered = baseRequest.Params
            .Where(p => p.Type == "path" || requiredParamNames.Contains(p.Name))
            .ToList();        var headersFiltered = FilterHeadersToRequired(operation, baseRequest.Headers, requiredParamNames);

        string? body = null;
        if (operation.RequestBody is { } requestBody)
        {
            var schema = PrimaryBodySchema(requestBody);
            if (schema is not null && baseRequest.JsonBody is not null)
            {
                var requiredOnly = SampleValueFactory.Sample(
                    schema, SampleValueFactory.Depth.RequiredOnly, level: 2);
                body = (requiredOnly as JsonObject ?? new JsonObject()).ToJsonString(BodyWriterOptions);
            }
        }

        return baseRequest with { Params = paramsFiltered, Headers = headersFiltered, JsonBody = body };
    }

    private static List<(string Name, string Value, bool Enabled)> FilterHeadersToRequired(
        Operation operation,
        IReadOnlyList<(string Name, string Value, bool Enabled)> headers,
        HashSet<string> requiredParamNames)
    {
        var requiredHeaderNames = operation.Parameters
            .Where(p => p.Required && p.In == ParameterLocation.Header)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);
        var result = new List<(string, string, bool)>();
        foreach (var header in headers)
        {
            var keep = header.Name == "Cookie"
                ? operation.Parameters.Any(p => p.In == ParameterLocation.Cookie && p.Required &&
                                                requiredParamNames.Contains(p.Name))
                : header.Name == "Content-Type"
                    ? true
                    : requiredHeaderNames.Contains(header.Name);
            if (keep)
            {
                result.Add(header);
            }
        }

        return result;
    }

    // ---- field enumeration ----

    public enum FieldLocation
    {
        Query,
        Header,
        Cookie,
        Path,
        Body,
    }

    public sealed record FieldRef(FieldLocation Location, string DotPath, string ParamName, SchemaInfo Schema, bool Required);

    internal static IEnumerable<FieldRef> EnumerateFields(Operation operation)
    {
        foreach (var parameter in operation.Parameters)
        {
            if (parameter.Schema is null)
            {
                continue;
            }

            var location = parameter.In switch
            {
                ParameterLocation.Query => FieldLocation.Query,
                ParameterLocation.Header => FieldLocation.Header,
                ParameterLocation.Cookie => FieldLocation.Cookie,
                ParameterLocation.Path => FieldLocation.Path,
                _ => FieldLocation.Query,
            };

            yield return new FieldRef(location, parameter.Name, parameter.Name, parameter.Schema, parameter.Required);
        }

        if (operation.RequestBody is { } requestBody)
        {
            var schema = PrimaryBodySchema(requestBody);
            if (schema is not null)
            {
                foreach (var field in EnumerateBodyFields(SampleValueFactory.MergeAllOf(schema)))
                {
                    yield return field;
                }
            }
        }
    }

    private static IEnumerable<FieldRef> EnumerateBodyFields(SchemaInfo schema, string prefix = "", int level = 0)
    {
        if (level > 3)
        {
            yield break; // bounded nesting keeps candidate sets deterministic and small
        }

        var required = SampleValueFactory.MergedRequired(schema);
        foreach (var (name, propertySchema) in SampleValueFactory.MergedProperties(schema))
        {
            var dotPath = prefix.Length == 0 ? name : $"{prefix}.{name}";
            yield return new FieldRef(FieldLocation.Body, dotPath, name, propertySchema, required.Contains(name));

            var primary = NonNullPrimary(propertySchema);
            if (primary == "object")
            {
                foreach (var nested in EnumerateBodyFields(propertySchema, dotPath, level + 1))
                {
                    yield return nested;
                }
            }
        }
    }

    // ---- mutations ----

    private static ScenarioRequest? ApplyValue(ScenarioRequest baseRequest, FieldRef field, JsonNode value)
    {
        var serialized = ValueToString(value);
        switch (field.Location)
        {
            case FieldLocation.Body:
            {
                if (baseRequest.JsonBody is null)
                {
                    return null;
                }

                try
                {
                    if (JsonNode.Parse(baseRequest.JsonBody) is not JsonObject root)
                    {
                        return null;
                    }

                    if (!TrySetPath(root, field.DotPath.Split('.'), value.DeepClone()))                    {
                        return null;
                    }

                    return baseRequest with { JsonBody = root.ToJsonString(BodyWriterOptions) };
                }
                catch (JsonException)
                {
                    return null;
                }
            }

            case FieldLocation.Query or FieldLocation.Path:
            {
                var updated = baseRequest.Params.ToList();
                var index = updated.FindIndex(p => string.Equals(p.Name, field.ParamName, StringComparison.Ordinal));
                if (index >= 0)
                {
                    updated[index] = updated[index] with { Value = serialized };
                }
                else
                {
                    updated.Add((field.ParamName, serialized, field.Location == FieldLocation.Path ? "path" : "query", true));
                }

                return baseRequest with { Params = updated };
            }

            case FieldLocation.Header or FieldLocation.Cookie:
            {
                var updated = baseRequest.Headers.ToList();
                var index = updated.FindIndex(h => string.Equals(h.Name, field.ParamName, StringComparison.Ordinal));
                if (index >= 0)
                {
                    updated[index] = updated[index] with { Value = serialized };
                }
                else
                {
                    updated.Add((field.ParamName, serialized, true));
                }

                return baseRequest with { Headers = updated };
            }

            default:
                return null;
        }
    }

    private static ScenarioRequest RemoveBodyPath(ScenarioRequest baseRequest, string dotPath)
    {
        if (baseRequest.JsonBody is null)
        {
            return baseRequest;
        }

        try
        {
            if (JsonNode.Parse(baseRequest.JsonBody) is not JsonObject root)
            {
                return baseRequest;
            }

            RemovePath(root, dotPath.Split('.'));
            return baseRequest with { JsonBody = root.ToJsonString(BodyWriterOptions) };
        }
        catch (JsonException)
        {
            return baseRequest;
        }
    }

    private static void RemovePath(JsonObject obj, string[] segments)
    {
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (obj[segments[i]] is not JsonObject next)
            {
                return;
            }

            obj = next;
        }

        obj.Remove(segments[^1]);
    }

    private static bool TrySetPath(JsonObject obj, string[] segments, JsonNode value)
    {
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (obj[segments[i]] is not JsonObject next)
            {
                return false; // do not fabricate intermediate objects absent from the base body
            }

            obj = next;
        }

        obj[segments[^1]] = value;
        return true;
    }

    private static string ValueToString(JsonNode value) => value switch
    {
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        _ => value.ToJsonString(BodyWriterOptions),
    };

    private static string LeafName(string dotPath)
    {
        var last = dotPath.LastIndexOf('.');
        return last >= 0 ? dotPath[(last + 1)..] : dotPath;
    }

    internal static string StableId(ScenarioCategory category, string dotPath)
    {
        var prefix = category switch
        {
            ScenarioCategory.SuccessFull => "success-full",
            ScenarioCategory.SuccessRequiredOnly => "success-required-only",
            ScenarioCategory.MissingRequired => "missing",
            ScenarioCategory.InvalidEnum => "invalid-enum",
            ScenarioCategory.BelowMinimum => "below-min",
            ScenarioCategory.AboveMaximum => "above-max",
            ScenarioCategory.MaxLengthExceeded => "over-max-length",
            ScenarioCategory.MinLengthViolated => "under-min-length",
            ScenarioCategory.WrongType => "wrong-type",
            ScenarioCategory.PatternViolation => "pattern-violation",
            ScenarioCategory.InvalidFormat => "invalid-format",
            ScenarioCategory.TooFewItems => "too-few-items",
            ScenarioCategory.TooManyItems => "too-many-items",
            ScenarioCategory.BoundaryMinimum => "boundary-min",
            ScenarioCategory.BoundaryMaximum => "boundary-max",
            ScenarioCategory.BoundaryMinLength => "boundary-min-length",
            ScenarioCategory.BoundaryMaxLength => "boundary-max-length",
            ScenarioCategory.BoundaryItemsMin => "boundary-items-min",
            ScenarioCategory.BoundaryItemsMax => "boundary-items-max",
            _ => "scenario",
        };

        if (prefix is "success-full" or "success-required-only")
        {
            return prefix;
        }

        return $"{prefix}-{SanitizeId(dotPath)}";
    }

    private static string SanitizeId(string text)
    {
        var result = new System.Text.StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.')
            {
                result.Append(ch);
            }
            else
            {
                result.Append($"x{((int)ch):x2}");
            }
        }

        return result.ToString();
    }
}

/// <summary>Traditional-Chinese (zh-TW) display names and descriptions for generated scenarios.</summary>
public static class ScenarioTexts
{
    public const string LanguageTag = "zh-TW";

    public static string SuccessFull() => "成功－完整參數";

    public static string SuccessFullDescription() =>
        "此範例使用 OpenAPI 規格中可推導的完整有效參數，用於驗證 API 在一般有效輸入下的回應。";

    public static string SuccessRequiredOnly() => "成功－僅輸入必填參數";

    public static string SuccessRequiredOnlyDescription() =>
        "此範例僅填入 API 規格中標示為必填的欄位，用於確認最小必要輸入是否可正常處理。";

    public static string NameFor(ScenarioCategory category, string field) => category switch
    {
        ScenarioCategory.MissingRequired => $"失敗－缺少 {field}",
        ScenarioCategory.InvalidEnum => $"失敗－{field} 使用無效列舉值",
        ScenarioCategory.BelowMinimum => $"失敗－{field} 低於允許最小值",
        ScenarioCategory.AboveMaximum => $"失敗－{field} 超過允許最大值",
        ScenarioCategory.MaxLengthExceeded => $"失敗－{field} 超過最大長度",
        ScenarioCategory.MinLengthViolated => $"失敗－{field} 低於最小長度",
        ScenarioCategory.WrongType => $"失敗－{field} 型別錯誤",
        ScenarioCategory.PatternViolation => $"失敗－{field} 不符合格式模式",
        ScenarioCategory.InvalidFormat => $"失敗－{field} 格式不正確",
        ScenarioCategory.TooFewItems => $"失敗－{field} 項目數不足",
        ScenarioCategory.TooManyItems => $"失敗－{field} 超過項目數上限",
        ScenarioCategory.BoundaryMinimum => $"邊界值－{field} 使用最小允許值",
        ScenarioCategory.BoundaryMaximum => $"邊界值－{field} 使用最大允許值",
        ScenarioCategory.BoundaryMinLength => $"邊界值－{field} 使用最小長度",
        ScenarioCategory.BoundaryMaxLength => $"邊界值－{field} 使用最大長度",
        ScenarioCategory.BoundaryItemsMin => $"邊界值－{field} 使用最小項目數",
        ScenarioCategory.BoundaryItemsMax => $"邊界值－{field} 使用最大項目數",
        _ => $"情境－{field}",
    };

    public static string DescriptionFor(ScenarioCategory category, string dotPath, SchemaInfo schema)
    {
        return category switch
        {
            ScenarioCategory.MissingRequired =>
                $"此範例刻意不傳入必填欄位 `{dotPath}`，用於確認 API 的必填欄位驗證行為。",
            ScenarioCategory.InvalidEnum =>
                $"此範例將 `{dotPath}` 設定為 OpenAPI 規格允許範圍以外的值，用於確認後端是否正確執行列舉值驗證。",
            ScenarioCategory.BelowMinimum =>
                $"此範例將 `{dotPath}` 設定為低於規格允許最小值的數值，用於確認後端的最小值驗證行為。",
            ScenarioCategory.AboveMaximum =>
                $"此範例將 `{dotPath}` 設定為超過規格允許最大值的數值，用於確認後端的最大值驗證行為。",
            ScenarioCategory.MaxLengthExceeded =>
                $"此範例提供的 `{dotPath}` 字串長度超過規格宣告的上限，用於確認最大長度驗證行為。",
            ScenarioCategory.MinLengthViolated =>
                $"此範例提供的 `{dotPath}` 字串長度低於規格宣告的下限，用於確認最小長度驗證行為。",
            ScenarioCategory.WrongType =>
                $"此範例將 `{dotPath}` 設定為與規格宣告不符的資料型別，用於確認後端的型別驗證行為。",
            ScenarioCategory.PatternViolation =>
                $"此範例提供的 `{dotPath}` 值不符合規格宣告的 pattern 模式，用於確認格式模式驗證行為。",
            ScenarioCategory.InvalidFormat =>
                $"此範例將 `{dotPath}` 設定為不符合「{schema.Format}」格式的值，用於確認後端的格式驗證行為。",
            ScenarioCategory.TooFewItems =>
                $"此範例提供的 `{dotPath}` 陣列項目數少於規格最小限制，用於確認陣列長度驗證行為。",
            ScenarioCategory.TooManyItems =>
                $"此範例提供的 `{dotPath}` 陣列項目數超過規格上限，用於確認陣列長度驗證行為。",
            ScenarioCategory.BoundaryMinimum =>
                $"此範例將 `{dotPath}` 設定為規格允許的最小邊界值，用於驗證邊界輸入可被後端正確處理。",
            ScenarioCategory.BoundaryMaximum =>
                $"此範例將 `{dotPath}` 設定為規格允許的最大邊界值，用於驗證邊界輸入可被後端正確處理。",
            ScenarioCategory.BoundaryMinLength =>
                $"此範例將 `{dotPath}` 設定為規格允許的最小字串長度，用於驗證邊界輸入可被後端正確處理。",
            ScenarioCategory.BoundaryMaxLength =>
                $"此範例將 `{dotPath}` 設定為規格允許的最大字串長度，用於驗證邊界輸入可被後端正確處理。",
            ScenarioCategory.BoundaryItemsMin =>
                $"此範例將 `{dotPath}` 的陣列項目數設定為規格允許的最小值，用於驗證邊界輸入可被後端正確處理。",
            ScenarioCategory.BoundaryItemsMax =>
                $"此範例將 `{dotPath}` 的陣列項目數設定為規格允許的最大值，用於驗證邊界輸入可被後端正確處理。",
            _ => $"此範例用於驗證後端在欄位 `{dotPath}` 特定輸入下的實際回應。",
        };
    }
}
