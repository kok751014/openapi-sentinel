using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using OpenApiSentinel.OpenApi.Model;

namespace OpenApiSentinel.Bruno;

public enum BodyKind
{
    None,
    Json,
    MultipartForm,
    FormUrlEncoded,
    Text,
    Xml,
}

/// <summary>One key/value row of a form-style request body.</summary>
public sealed record FormRow(string Name, string Value, bool IsFile = false, bool Enabled = true);

/// <summary>A fully materialized canonical request body.</summary>
public sealed record CanonicalBody(
    BodyKind Kind,
    string? Json = null,
    IReadOnlyList<FormRow>? Rows = null,
    string? Text = null)
{
    public static readonly CanonicalBody None = new(BodyKind.None);

    /// <summary>The Content-Type header value to declare, or null when the HTTP client must
    /// generate it (multipart boundaries) — declaring one without a boundary breaks requests.</summary>
    public string? ContentType => Kind switch
    {
        BodyKind.Json => "application/json",
        BodyKind.MultipartForm => null, // transport adds the boundary
        BodyKind.FormUrlEncoded => "application/x-www-form-urlencoded",
        BodyKind.Text => "text/plain",
        BodyKind.Xml => "application/xml",
        _ => null,
    };
}

/// <summary>
/// THE single generator of canonical request bodies. Both the operation's default body and
/// every generated example derive from this factory, so their structural shapes can never
/// diverge (array contracts produce array bodies everywhere, object contracts objects, …).
/// Supports application/json, multipart/form-data, application/x-www-form-urlencoded and
/// text/plain request bodies.
/// </summary>
public static class CanonicalBodyFactory
{
    /// <summary>Placeholder for file upload parts. Deliberately NOT an existing local path:
    /// the user must pick a real file before sending; nothing on disk is ever invented.</summary>
    public const string FilePlaceholder = "choose-a-file-to-upload";

    private static readonly Regex BinaryFormatRegex = new("^(binary|byte)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static BodyKind ResolveKind(RequestBodyInfo body)
    {
        foreach (var contentType in OrderedContentTypes(body))
        {
            if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                return BodyKind.Json;
            }
        }

        foreach (var contentType in OrderedContentTypes(body))
        {
            if (contentType.Contains("multipart", StringComparison.OrdinalIgnoreCase))
            {
                return BodyKind.MultipartForm;
            }

            if (contentType.Contains("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
            {
                return BodyKind.FormUrlEncoded;
            }

            if (contentType.Contains("xml", StringComparison.OrdinalIgnoreCase))
            {
                return BodyKind.Xml;
            }

            if (contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
            {
                return BodyKind.Text;
            }
        }

        return BodyKind.None;
    }

    private static IEnumerable<string> OrderedContentTypes(RequestBodyInfo body) =>
        body.Content.Keys.Order(StringComparer.OrdinalIgnoreCase);

    /// <summary>Produces the canonical body at the requested property depth.</summary>
    public static CanonicalBody Create(RequestBodyInfo body, SampleValueFactory.Depth depth)
    {
        var kind = ResolveKind(body);
        if (kind == BodyKind.None || PrimarySchema(body, kind) is not { } schema)
        {
            return CanonicalBody.None;
        }

        return kind switch
        {
            BodyKind.Json => Json(schema, depth),
            BodyKind.MultipartForm => Form(schema, depth, allowFiles: true),
            BodyKind.FormUrlEncoded => Form(schema, depth, allowFiles: false),
            BodyKind.Text => new CanonicalBody(BodyKind.Text, Text: SampleValueFactory.SampleString(schema)),
            BodyKind.Xml => new CanonicalBody(BodyKind.Xml, Text: SampleValueFactory.SampleString(schema)),
            _ => CanonicalBody.None,
        };
    }

    public static CanonicalBody Json(SchemaInfo schema, SampleValueFactory.Depth depth)
    {
        var sample = SampleValueFactory.Sample(schema, depth, level: 2);
        return sample is null
            ? CanonicalBody.None
            : new CanonicalBody(BodyKind.Json, Json: sample.ToJsonString(JsonOptions));
    }

    private static CanonicalBody Form(SchemaInfo schema, SampleValueFactory.Depth depth, bool allowFiles)
    {
        var merged = SampleValueFactory.MergeAllOf(schema);
        var required = SampleValueFactory.MergedRequired(merged);
        var rows = new List<FormRow>();
        foreach (var (name, propertySchema) in SampleValueFactory.MergedProperties(merged))
        {
            if (depth == SampleValueFactory.Depth.RequiredOnly && !required.Contains(name))
            {
                continue;
            }

            rows.AddRange(PropertyRows(name, propertySchema, allowFiles));
        }

        return rows.Count == 0 ? CanonicalBody.None : new CanonicalBody(
            allowFiles ? BodyKind.MultipartForm : BodyKind.FormUrlEncoded, Rows: rows);
    }

    /// <summary>Rows for one declared form property: primitives/enums/UUIDs become text parts,
    /// arrays repeat the part name once per element, and binary parts become safe file
    /// placeholders that never point at a real local file.</summary>
    private static IEnumerable<FormRow> PropertyRows(string name, SchemaInfo propertySchema, bool allowFiles)
    {
        // Composition: probe the first branch (mirrors SampleValueFactory.Sample policy).
        var effective = propertySchema;
        if (SampleValueFactory.MergeAllOf(propertySchema) is { } merged &&
            !ReferenceEquals(merged.Node, propertySchema.Node))
        {
            effective = merged;
        }

        if (IsBinary(effective) && allowFiles)
        {
            // Safe placeholder: no directory separators, no existing-file implication.
            yield return new FormRow(name, FilePlaceholder, IsFile: true);
            yield break;
        }

        if (!allowFiles && IsBinary(effective))
        {
            yield return new FormRow(name, "<binary content>");
            yield break;
        }

        var primary = NonNullPrimary(effective);
        if (primary == "array")
        {
            // Repeat the part name: unconstrained arrays demonstrate multi-part semantics
            // with two elements; declared minItems raises the count (capped for sanity).
            var count = Math.Clamp(Math.Max(SampleValueFactory.IntKeyword(effective, "minItems") ?? 0, 2), 1, 3);
            foreach (var row in ElementRows(name, ElementSchema(effective), Math.Min(count, 3), allowFiles))
            {
                yield return row;
            }

            yield break;
        }

        var sample = SampleValueFactory.Sample(effective, SampleValueFactory.Depth.Full, level: 3);
        if (sample is null)
        {
            yield break;
        }

        yield return new FormRow(name, SerializeScalar(sample));
    }

    private static IEnumerable<FormRow> ElementRows(string name, SchemaInfo? elementSchema, int count, bool allowFiles)
    {
        if (elementSchema is not null && IsBinary(elementSchema) && allowFiles)
        {
            for (var i = 0; i < count; i++)
            {
                yield return new FormRow(name, FilePlaceholder, IsFile: true);
            }

            yield break;
        }

        for (var i = 0; i < count; i++)
        {
            var sample = elementSchema is null
                ? null
                : SampleValueFactory.Sample(elementSchema, SampleValueFactory.Depth.Full, level: 4);
            yield return new FormRow(name, sample is null ? "item" : SerializeScalar(sample));
        }
    }

    private static SchemaInfo? ElementSchema(SchemaInfo arraySchema)
    {
        if (arraySchema.Node is System.Text.Json.Nodes.JsonObject obj &&
            obj.TryGetPropertyValue("items", out var items) && items is not null && items is not System.Text.Json.Nodes.JsonArray)
        {
            return SampleValueFactory.Wrap(items, arraySchema.Spec);
        }

        return null;
    }

    private static bool IsBinary(SchemaInfo schema)
    {
        if ((schema.Format is "binary" || BinaryFormatRegex.IsMatch(schema.Format ?? "")) && NonNullPrimary(schema) == "string")
        {
            return true;
        }

        // ASP.NET Core Swashbuckle emits {"type":"string","format":"binary"}; some generators
        // declare file uploads as explicit {"type":"file"} — honor that too.
        return NonNullPrimary(schema) == "file";
    }

    private static SchemaInfo? PrimarySchema(RequestBodyInfo body, BodyKind kind)
    {
        foreach (var contentType in OrderedContentTypes(body))
        {
            var matches = kind switch
            {
                BodyKind.Json => contentType.Contains("json", StringComparison.OrdinalIgnoreCase),
                BodyKind.MultipartForm => contentType.Contains("multipart", StringComparison.OrdinalIgnoreCase),
                BodyKind.FormUrlEncoded => contentType.Contains("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase),
                BodyKind.Text => contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase),
                BodyKind.Xml => contentType.Contains("xml", StringComparison.OrdinalIgnoreCase),
                _ => false,
            };

            if (matches)
            {
                return body.Content[contentType];
            }
        }

        return null;
    }

    internal static string SerializeScalar(JsonNode value) => value switch
    {
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        JsonValue v when v.TryGetValue<bool>(out var b) => b ? "true" : "false",
        _ => value.ToJsonString(),
    };

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

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
