using YamlDotNet.RepresentationModel;

namespace OpenApiSentinel.Bruno;

/// <summary>
/// Parses the canonical-shape Bruno <c>example {'{'}</c> blocks written by Sentinel's own
/// serializer back into OpenCollection example mappings, so a native-workspace sync can
/// manage its generated examples incrementally. Blocks authored directly in Bruno Desktop
/// (different layout) do not parse here and fall back to verbatim raw preservation.
///
/// Recognized layout: children at indent 4; request/response sections open at 4 and close
/// at 4; section fields at 8; dictionary rows at 10; text-family payloads sit at &gt;10
/// until the exact closer "&lt;8&gt;}"; triple-single-quoted content ends at a line whose
/// trim is "'''".
/// </summary>
public static class BruExampleParser
{
    public static bool TryParse(BruBlock block, out YamlMappingNode? example)
    {
        var lines = block.RawLines.Skip(1).ToList(); // drop "example {"
        while (lines.Count > 0 && lines[^1].Trim().Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        if (lines.Count == 0 || lines[^1].Trim() != "}")
        {
        }

        lines.RemoveAt(lines.Count - 1);

        var result = new YamlMappingNode();
        var i = 0;

        // ---- name ----
        SkipBlanks();
        if (i >= lines.Count || !TryKeyValue(lines[i].Trim(), out var kv) || kv.Key != "name")
        {
            return Fail(out example);
        }

        BrunoYaml.SetString(result, "name", StripQuotes(kv.Value));
        i++;

        // ---- description (optional, single line) ----
        if (i < lines.Count && lines[i].Trim().Length > 0 &&
            TryKeyValue(lines[i].Trim(), out var desc) && desc.Key == "description")
        {
            BrunoYaml.SetString(result, "description", StripQuotes(desc.Value));
            i++;
        }

        // ---- sections ----
        foreach (var sectionName in new[] { "request", "response" })
        {
            SkipBlanks();
            if (i < lines.Count && lines[i].Trim().StartsWith(sectionName + ":", StringComparison.Ordinal))
            {
                if (!TrySection(lines, ref i, sectionName, out var section))
                {
                    return Fail(out example);
                }

                if (section is not null)
                {
                    BrunoYaml.Set(result, sectionName, section);
                }
            }
        }

        // Trailing unknown content → reject.
        while (i < lines.Count)
        {
            if (lines[i].Trim().Length > 0)
            {
                return Fail(out example);
            }

            i++;
        }

        example = result;
        return true;

        void SkipBlanks()
        {
            while (i < lines.Count && lines[i].Trim().Length == 0)
            {
                i++;
            }
        }
    }

    private static bool Fail(out YamlMappingNode? example)
    {
        example = null;
        return false;
    }

    private static bool TryKeyValue(string trimmed, out (string Key, string Value) pair)
    {
        pair = default;
        var colon = trimmed.IndexOf(':');
        if (colon <= 0)
        {
            return false;
        }

        pair = (trimmed[..colon].Trim(), trimmed[(colon + 1)..].Trim());
        return true;
    }

    private static string StripQuotes(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;

    /// <summary>Parses "&lt;name&gt;: { … }" where the closer sits at the same indent.</summary>
    private static bool TrySection(List<string> lines, ref int i, string expectedName, out YamlMappingNode? section)
    {
        section = null;
        var opener = lines[i];
        if (!(opener.Trim().StartsWith(expectedName + ':', StringComparison.Ordinal) && opener.Trim().EndsWith('{')))
        {
        }

        var sectionIndent = Indent(opener);
        i++;
        var node = new YamlMappingNode();

        while (i < lines.Count)
        {
            var current = lines[i];
            if (Indent(current) == sectionIndent && current.Trim() == "}")
            {
                i++;
                section = node;
                return true;
            }

            if (current.Trim().Length == 0)
            {
                i++;
                continue;
            }

            if (Indent(current) <= sectionIndent)
            {
                return false;
            }

            var fieldText = current.Trim();
            (string Key, string Value) field;
            if (fieldText.EndsWith('{'))
            {
                // Nested-dict openers like "body:json: {" contain colons inside the KEY;
                // split at the LAST colon so the full label stays in field.Key.
                var lastColon = fieldText.LastIndexOf(':');
                field = (fieldText[..lastColon].Trim(), fieldText[(lastColon + 1)..].Trim());
                            }
            else if (!TryKeyValue(fieldText, out field))
            {
                return false;
            }

            if (field.Value.Length == 0 || field.Value == "{")
            {
                i++;
                var fieldIndent = Indent(current);

                // Rows-style dictionaries: headers / params:* / body forms.
                if (IsRowDictionary(field.Key))
                {
                    var rows = new YamlSequenceNode();
                    while (i < lines.Count &&
                           !(Indent(lines[i]) == fieldIndent && lines[i].Trim() == "}"))
                    {
                        var rowLine = lines[i].Trim();
                        if (rowLine.Length > 0)
                        {
                            if (!TryRow(rowLine, out var row))
                            {
                            }

                            rows.Children.Add(row);
                        }

                        i++;
                    }

                    if (i >= lines.Count)
                    {
                    }

                    i++; // consume closer

                    switch (field.Key)
                    {
                        case "headers":
                            BrunoYaml.Set(node, "headers", rows);
                            break;
                        case "params:query":
                        case "params:path":
                        case "params":
                        {
                            var type = field.Key == "params:path" ? "path" : "query";
                            foreach (var row in rows.Children.OfType<YamlMappingNode>())
                            {
                                BrunoYaml.SetString(row, "type", type);
                            }

                            AppendParams(node, rows);
                            break;
                        }
                        default:
                        {
                            // body:form-urlencoded / body:multipart-form → body{type,data}
                            var bodyType = field.Key == "body:multipart-form" ? "multipart-form" : "form-urlencoded";
                            var bodyMap = new YamlMappingNode();
                            BrunoYaml.SetString(bodyMap, "type", bodyType);
                            BrunoYaml.Set(bodyMap, "data", rows);
                            BrunoYaml.Set(node, "body", bodyMap);
                            break;
                        }
                    }

                    continue;
                }

                // Text-family payload (body:json/text/xml/sparql): raw lines until closer.
                if (field.Key.StartsWith("body:", StringComparison.Ordinal))
                {
                    var payloadLines = new List<string>();
                    while (i < lines.Count &&
                           !(Indent(lines[i]) == fieldIndent && lines[i].Trim() == "}"))
                    {
                        var payloadLine = Indent(lines[i]) > fieldIndent ? lines[i][(fieldIndent + 2)..] : lines[i];
                        payloadLines.Add(payloadLine);
                        i++;
                    }

                    if (i >= lines.Count)
                    {
                    }

                    i++; // consume closer
                    var textBody = new YamlMappingNode();
                    BrunoYaml.SetString(textBody, "type", field.Key["body:".Length..]);
                    BrunoYaml.SetString(textBody, "data", string.Join("\n", payloadLines));
                    BrunoYaml.Set(node, "body", textBody);
                    continue;
                }

                // Generic scalar map (e.g. response.status {code,text}, body {type,content}).
                var map = new YamlMappingNode();
                var sawTripleQuote = false;
                while (i < lines.Count &&
                       !(Indent(lines[i]) == fieldIndent && lines[i].Trim() == "}"))
                {
                    var inner = lines[i];
                    var innerTrimmed = inner.Trim();
                    if (innerTrimmed.Length == 0)
                    {
                        i++;
                        continue;
                    }

                    if (!TryKeyValue(innerTrimmed, out var kv2))
                    {
                        return false;
                    }

                    if (kv2.Value == "'''")
                    {
                        sawTripleQuote = true;
                        var payload = new List<string>();
                        i++;
                        while (i < lines.Count && lines[i].Trim() != "'''")
                        {
                            payload.Add(Indent(lines[i]) > fieldIndent ? lines[i][(fieldIndent + 2)..] : lines[i]);
                            i++;
                        }

                        if (i >= lines.Count)
                        {
                        }

                        i++; // consume '''
                        BrunoYaml.SetString(map, kv2.Key, string.Join("\n", payload));
                        continue;
                    }

                    BrunoYaml.SetString(map, kv2.Key, StripQuotes(kv2.Value));
                    i++;
                }

                if (i >= lines.Count)
                {
                }

                i++; // consume closer

                if (field.Key == "status" && !sawTripleQuote)
                {
                    // Flatten {code,text} → status:number + statusText.
                    var code = BrunoYaml.AsInt(BrunoYaml.Get(map, "code"));
                    var text = BrunoYaml.AsString(BrunoYaml.Get(map, "text"));
                    if (code is not null)
                    {
                        BrunoYaml.SetInt(node, "status", code.Value);
                    }

                    if (!string.IsNullOrEmpty(text))
                    {
                        BrunoYaml.SetString(node, "statusText", text);
                    }
                }
                else if (field.Key == "body")
                {
                    // Response body: {type, content} → {type, data}.
                    var content = BrunoYaml.AsString(BrunoYaml.Get(map, "content")) ?? "";
                    var type = BrunoYaml.AsString(BrunoYaml.Get(map, "type")) ?? "text";
                    var bodyMap = new YamlMappingNode();
                    BrunoYaml.SetString(bodyMap, "type", type);
                    if (type != "binary")
                    {
                        BrunoYaml.Set(bodyMap, "data",
                            new YamlScalarNode(content) { Style = YamlDotNet.Core.ScalarStyle.Literal });
                    }

                    BrunoYaml.Set(node, "body", bodyMap);
                }
                else
                {
                    BrunoYaml.Set(node, field.Key, map);
                }

                continue;
            }

            if (field.Key != "mode")
            {
                BrunoYaml.SetString(node, field.Key, StripQuotes(field.Value));
            }

            i++;
        }

        return false;
    }

    private static bool IsRowDictionary(string key) =>
        key is "headers" or "params" or "params:query" or "params:path" or "body:form-urlencoded" or "body:multipart-form";

    private static bool TryRow(string rowLine, out YamlMappingNode row)
    {
        row = new YamlMappingNode();
        var enabled = !rowLine.StartsWith('~');
        var content = enabled ? rowLine : rowLine[1..];
        if (!TryKeyValue(content, out var pair))
        {
            return false;
        }

        BrunoYaml.SetString(row, "name", StripQuotes(pair.Key));
        BrunoYaml.SetString(row, "value", pair.Value);
        if (!enabled)
        {
            BrunoYaml.SetBool(row, "disabled", true);
        }

        return true;
    }

    private static void AppendParams(YamlMappingNode target, YamlSequenceNode rows)
    {
        var existing = BrunoYaml.SequenceOf(target, "params");
        var seq = existing ?? new YamlSequenceNode();
        foreach (var row in rows.Children)
        {
            seq.Children.Add(row);
        }

        BrunoYaml.Set(target, "params", seq);
    }

    private static int Indent(string line) => line.Length - line.TrimStart().Length;
}
