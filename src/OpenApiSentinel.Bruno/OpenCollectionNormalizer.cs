using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace OpenApiSentinel.Bruno;

/// <summary>
/// Brings any loaded collection (legacy generator output or user-edited files) up to shapes
/// the official OpenCollection v1.0 schema accepts, WITHOUT changing meaning:
/// - every <c>params</c> row gets an explicit <c>type</c> (query|path); legacy rows default to
///   <c>query</c> with a diagnostic — the documented safe fallback policy;
/// - string-typed schema slots (names, values, urls, methods, body data…) are coerced out of
///   accidental YAML number/bool/null typing (e.g. an unquoted <c>value: 12345</c>);
/// - the illegal legacy <c>enabled:</c> row flag is removed (<c>enabled:false</c> becomes the
///   schema's <c>disabled: true</c>);
/// - numeric-looking quoted <c>seq</c> values are normalized back to numbers.
/// Structural violations the normalizer cannot repair are left untouched on purpose — the
/// schema validator gate reports them precisely instead of guessing.
/// </summary>
public static class OpenCollectionNormalizer
{
    public static List<string> Normalize(YamlDotNet.RepresentationModel.YamlMappingNode root)
    {
        var notes = new List<string>();
        NormalizeCollectionRoot(root, notes);
        return notes;
    }

    private static void NormalizeCollectionRoot(YamlMappingNode root, List<string> notes)
    {
        ForceString(root, "opencollection");

        if (BrunoYaml.MappingOf(root, "info") is { } info)
        {
            ForceString(info, "name");
            ForceString(info, "summary");
            ForceString(info, "version");
        }

        NormalizeRequestDefaults(root);

        if (BrunoYaml.SequenceOf(root, "items") is { } items)
        {
            foreach (var item in BrunoYaml.MappingItems(items))
            {
                NormalizeItem(item, notes);
            }
        }
    }

    /// <summary>RequestDefaults headers (root or folder level) use the same row shape as request headers.</summary>
    private static void NormalizeRequestDefaults(YamlMappingNode container)
    {
        if (BrunoYaml.MappingOf(container, "request") is not { } defaults ||
            BrunoYaml.SequenceOf(defaults, "headers") is not { } headers)
        {
            return;
        }

        foreach (var row in BrunoYaml.MappingItems(headers))
        {
            ForceString(row, "name");
            ForceString(row, "value");
            ReplaceLegacyEnabledFlag(row);
        }
    }

    private static void NormalizeItem(YamlMappingNode item, List<string> notes)
    {
        if (BrunoYaml.MappingOf(item, "info") is { } info)
        {
            ForceString(info, "name");
            ForceString(info, "type");
            NormalizeSeq(info);
            ForceString(info, "description", allowEmptyNull: true);
            if (BrunoYaml.SequenceOf(info, "tags") is { } tags)
            {
                foreach (var child in tags.Children.OfType<YamlScalarNode>().ToArray())
                {
                    ForceScalarString(child, tags);
                }
            }
        }

        if (BrunoYaml.SequenceOf(item, "items") is { } children)
        {
            foreach (var child in BrunoYaml.MappingItems(children))
            {
                NormalizeItem(child, notes);
            }

            NormalizeRequestDefaults(item);
            return; // folders have no http block
        }

        if (BrunoYaml.MappingOf(item, "http") is { } http)
        {
            ForceString(http, "method");
            ForceString(http, "url");

            if (BrunoYaml.SequenceOf(http, "params") is { } paramsSeq)
            {
                foreach (var row in BrunoYaml.MappingItems(paramsSeq))
                {
                    NormalizeKeyValueRow(row, notes, isParam: true);
                }
            }

            if (BrunoYaml.SequenceOf(http, "headers") is { } headersSeq)
            {
                foreach (var row in BrunoYaml.MappingItems(headersSeq))
                {
                    NormalizeKeyValueRow(row, notes, isParam: false);
                }
            }

            if (BrunoYaml.MappingOf(http, "body") is { } body)
            {
                ForceString(body, "type");
                if (BrunoYaml.Get(body, "data") is YamlScalarNode rawData && rawData.Style == ScalarStyle.Literal)
                {
                    // literal block already a string — nothing to do
                }
                else
                {
                    ForceString(body, "data");
                }

                if (BrunoYaml.SequenceOf(body, "data") is { } formData)
                {
                    foreach (var row in BrunoYaml.MappingItems(formData))
                    {
                        var isMultipart = string.Equals(BrunoYaml.AsString(BrunoYaml.Get(body, "type")), "multipart-form", StringComparison.Ordinal);
                        ForceString(row, "name");
                        if (isMultipart)
                        {
                            ForceString(row, "type");
                            if (BrunoYaml.Get(row, "value") is YamlScalarNode partValue &&
                                partValue.Value is not null)
                            {
                                var needsFix = BrunoYaml.PlainScalarResolvesToNonString(partValue.Value);
                                if (needsFix && partValue.Style is ScalarStyle.Any or ScalarStyle.Plain)
                                {
                                    BrunoYaml.Set(row, "value", new YamlScalarNode(partValue.Value) { Style = ScalarStyle.DoubleQuoted });
                                }
                            }
                        }
                        else
                        {
                            ForceString(row, "value");
                        }

                        ReplaceLegacyEnabledFlag(row);
                    }
                }
            }

            if (BrunoYaml.SequenceOf(item, "examples") is { } examples)
            {
                foreach (var example in BrunoYaml.MappingItems(examples))
                {
                    NormalizeExample(example, notes);
                }
            }
        }

        ForceString(item, "docs", allowEmptyNull: true);
    }

    private static void NormalizeExample(YamlMappingNode example, List<string> notes)
    {
        ForceString(example, "name");
        ForceString(example, "description", allowEmptyNull: true);

        if (BrunoYaml.MappingOf(example, "request") is { } request)
        {
            ForceString(request, "url");
            ForceString(request, "method");

            if (BrunoYaml.SequenceOf(request, "headers") is { } headersSeq)
            {
                foreach (var row in BrunoYaml.MappingItems(headersSeq))
                {
                    NormalizeKeyValueRow(row, notes, isParam: false);
                }
            }

            if (BrunoYaml.SequenceOf(request, "params") is { } paramsSeq)
            {
                foreach (var row in BrunoYaml.MappingItems(paramsSeq))
                {
                    NormalizeKeyValueRow(row, notes, isParam: true);
                }
            }

            if (BrunoYaml.MappingOf(request, "body") is { } body)
            {
                ForceString(body, "type");
                ForceString(body, "data");
            }
        }

        if (BrunoYaml.MappingOf(example, "response") is { } response)
        {
            NormalizeNumber(response, "status");
            ForceString(response, "statusText");
            if (BrunoYaml.SequenceOf(response, "headers") is { } responseHeaders)
            {
                foreach (var row in BrunoYaml.MappingItems(responseHeaders))
                {
                    NormalizeKeyValueRow(row, notes, isParam: false);
                }
            }

            if (BrunoYaml.MappingOf(response, "body") is { } responseBody)
            {
                ForceString(responseBody, "type");
                ForceString(responseBody, "data");
            }
        }
    }

    private static void NormalizeKeyValueRow(YamlMappingNode row, List<string> notes, bool isParam)
    {
        ForceString(row, "name");
        ForceString(row, "value");

        if (!isParam)
        {
            ReplaceLegacyEnabledFlag(row);
            return;
        }

        var existing = BrunoYaml.AsString(BrunoYaml.Get(row, "type"));
        switch (existing)
        {
            case null:
                // Documented fallback policy: our generator historically emitted only query/path
                // parameters and omitted type on query rows; default them and say so.
                BrunoYaml.SetString(row, "type", "query");
                notes.Add($"參數 '{BrunoYaml.AsString(BrunoYaml.Get(row, "name")) ?? "?"}' 缺少 type，已依相容政策補為 query。");
                break;
            case "query" or "path":
                break;
            case "Query":
                BrunoYaml.SetString(row, "type", "query");
                break;
            case "PATH":
                BrunoYaml.SetString(row, "type", "path");
                break;
            default:
                // Unknown location — leave it so the validator reports a precise enum error.
                break;
        }

        ReplaceLegacyEnabledFlag(row);
    }

    private static void ReplaceLegacyEnabledFlag(YamlMappingNode row)
    {
        // Old generator versions wrote an ILLEGAL `enabled:` flag (the schema only knows
        // `disabled`). Semantics are identical to absence; false maps to disabled:true.
        if (!BrunoYaml.HasKey(row, "enabled"))
        {
            return;
        }

        var wasEnabled = BrunoYaml.AsBool(BrunoYaml.Get(row, "enabled")) != false;
        BrunoYaml.RemoveKey(row, "enabled");
        if (!wasEnabled)
        {
            BrunoYaml.SetBool(row, "disabled", true);
        }
    }

    /// <summary>Forces a scalar-valued key into safe string presentation (quoted when the plain form would re-read as number/bool/null).</summary>
    private static void ForceString(YamlMappingNode map, string key, bool allowEmptyNull = false)
    {
        if (BrunoYaml.Get(map, key) is not YamlScalarNode scalar || scalar.Value is null)
        {
            return;
        }

        var text = scalar.Value;
        var needsFix = text.Length == 0
            ? !allowEmptyNull || scalar.Style != ScalarStyle.Plain
            : BrunoYaml.PlainScalarResolvesToNonString(text);
        if (!needsFix)
        {
            return;
        }

        if (scalar.Style is ScalarStyle.Any or ScalarStyle.Plain)
        {
            BrunoYaml.Set(map, key, new YamlScalarNode(text) { Style = ScalarStyle.DoubleQuoted });
        }
    }

    private static void ForceScalarString(YamlScalarNode scalar, YamlSequenceNode parent)
    {
        if (scalar.Value is null)
        {
            return;
        }

        if (scalar.Style == ScalarStyle.Any ||
            (scalar.Style == ScalarStyle.Plain && BrunoYaml.PlainScalarResolvesToNonString(scalar.Value)))
        {
            var index = parent.Children.IndexOf(scalar);
            if (index >= 0)
            {
                parent.Children[index] = new YamlScalarNode(scalar.Value) { Style = ScalarStyle.DoubleQuoted };
            }
        }
    }

    /// <summary>Sequence is schema-typed "number": quoted numeric strings go back to plain integers.</summary>
    private static void NormalizeSeq(YamlMappingNode info)
    {
        if (BrunoYaml.Get(info, "seq") is not YamlScalarNode { Value: not null } scalar)
        {
            return;
        }

        if (int.TryParse(scalar.Value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n) &&
            scalar.Style != ScalarStyle.Plain)
        {
            BrunoYaml.Set(info, "seq", new YamlScalarNode(n.ToString(System.Globalization.CultureInfo.InvariantCulture)) { Style = ScalarStyle.Plain });
        }
    }

    private static void NormalizeNumber(YamlMappingNode map, string key)
    {
        if (BrunoYaml.Get(map, key) is not YamlScalarNode { Value: not null } scalar || scalar.Style == ScalarStyle.Plain)
        {
            return;
        }

        if (double.TryParse(scalar.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
        {
            BrunoYaml.Set(map, key, new YamlScalarNode(scalar.Value) { Style = ScalarStyle.Plain });
        }
    }
}
