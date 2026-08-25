using System.Text.Json;
using System.Text.Json.Nodes;
using OpenApiSentinel.OpenApi.Model;

namespace OpenApiSentinel.Bruno;

public sealed record BodyMergeOutcome(string Json, IReadOnlyList<string> Notes);

/// <summary>
/// Merges a user's existing JSON request body with a contract schema while preserving
/// manual values: existing leaf values survive, missing required properties are added,
/// properties no longer declared are removed, and formatting of untouched subtrees is kept.
/// </summary>
public static class JsonBodyMerger
{
    private static readonly JsonSerializerOptions WriterOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Produces the merged body for the base request (full depth).</summary>
    public static BodyMergeOutcome Merge(string? existingJson, SchemaInfo bodySchema)
    {
        var notes = new List<string>();
        JsonObject? existing = null;
        if (!string.IsNullOrWhiteSpace(existingJson))
        {
            try
            {
                if (JsonNode.Parse(existingJson) is JsonObject parsed)
                {
                    existing = parsed;
                }
                else
                {
                    notes.Add("body-not-object");
                }
            }
            catch (JsonException)
            {
                notes.Add("body-unparseable");
            }
        }

        var mergedSchema = SampleValueFactory.MergeAllOf(bodySchema);
        var result = MergeObject(existing, mergedSchema, notes, level: 0);
        return new BodyMergeOutcome(result.ToJsonString(WriterOptions), notes);
    }

    private static JsonObject MergeObject(JsonObject? existing, SchemaInfo schema, List<string> notes, int level)
    {
        var result = new JsonObject();

        // Pass 1 — contract-declared properties in declaration order.
        foreach (var (name, propertySchema) in SampleValueFactory.MergedProperties(schema))
        {
            var required = SampleValueFactory.MergedRequired(schema).Contains(name);
            if (existing is not null && existing.TryGetPropertyValue(name, out var existingValue) && existingValue is not null)
            {
                result[name] = MergeValue(existingValue, propertySchema, notes, level + 1);
            }
            else
            {
                // Missing from the user's body: add required properties always; add optional
                // ones only when there was no body at all yet (fresh generation).
                if (required || existing is null)
                {
                    var sample = SampleValueFactory.Sample(propertySchema, SampleValueFactory.Depth.Full, level + 1);
                    result[name] = sample ?? JsonValue.Create("sample");
                    if (!required)
                    {
                        notes.Add($"added-optional:{name}");
                    }
                }
            }
        }

        // Pass 2 — keep user properties the contract does not declare only when the schema
        // allows additional properties; otherwise drop them and record a note.
        if (existing is not null)
        {
            var allowsAdditional = AllowsAdditionalProperties(schema);
            foreach (var pair in existing)
            {
                if (!result.ContainsKey(pair.Key))
                {
                    if (allowsAdditional)
                    {
                        result[pair.Key] = pair.Value?.DeepClone();
                    }
                    else
                    {
                        notes.Add($"removed-undeclared:{pair.Key}");
                    }
                }
            }
        }

        return result;
    }

    private static JsonNode MergeValue(JsonNode existingValue, SchemaInfo schema, List<string> notes, int level)
    {
        if (level > 8)
        {
            return existingValue.DeepClone();
        }

        var primary = NonNullPrimary(schema);
        switch (primary)
        {
            case "object" when existingValue is JsonObject existingObj:
                return MergeObject(existingObj, schema, notes, level);
            case "array" when existingValue is JsonArray existingArr:
                return PadArrayToMinItems(existingArr, schema);
            default:
                // Scalars keep the user's value verbatim; type mismatches are the user's data.
                return existingValue.DeepClone();
        }
    }

    private static JsonArray PadArrayToMinItems(JsonArray existing, SchemaInfo schema)
    {
        var minItems = SampleValueFactory.IntKeyword(schema, "minItems") ?? 0;
        if (existing.Count >= minItems)
        {
            return existing.DeepClone().AsArray();
        }

        var result = new JsonArray();
        foreach (var item in existing)
        {
            result.Add(item?.DeepClone());
        }

        JsonNode? elementSchema = null;
        if (schema.Node is JsonObject obj &&
            obj.TryGetPropertyValue("items", out var items) && items is not null && items is not JsonArray)
        {
            elementSchema = items;
        }

        while (result.Count < minItems)
        {
            result.Add(SampleValueFactory.Sample(
                elementSchema is null ? null : SampleValueFactory.Wrap(elementSchema, schema.Spec),
                SampleValueFactory.Depth.Full, level: 4) ?? JsonValue.Create("sample"));
        }

        return result;
    }

    internal static bool AllowsAdditionalProperties(SchemaInfo schema)
    {
        if (schema.TryGetKeyword("additionalProperties", out var ap))
        {
            return ap switch
            {
                JsonValue v when v.TryGetValue<bool>(out var b) => b,
                JsonObject => true,
                _ => false,
            };
        }

        return true;
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
}
