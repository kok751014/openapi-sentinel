using System.Text;
using OpenApiSentinel.Bruno.Model;
using YamlDotNet.RepresentationModel;

namespace OpenApiSentinel.Bruno;

/// <summary>
/// Serializes a canonical request item into .bru blocks. Block order mirrors the official
/// writers (meta → method → params → headers → body → auth → scripts/tests/assert →
/// settings → docs → raw user content → examples). Sentinel metadata travels in the
/// officially-supported <c>metadata</c> dictionary block; the binding tag is NOT written
/// into meta.tags (tags carry only user values).
/// </summary>
public static class BruItemWriter
{
    public static List<BruBlock> Build(BrunoItem item, YamlMappingNode? sentinelEntry)
    {
        var blocks = new List<BruBlock>();
        var info = BrunoYaml.MappingOf(item.Node, "info") ?? new YamlMappingNode();
        var http = BrunoYaml.MappingOf(item.Node, "http") ?? [];

        // ---- meta ----
        var meta = new BruBlock { Name = "meta", Kind = BruBlockKind.Dictionary };
        var name = BrunoYaml.AsString(BrunoYaml.Get(info, "name")) ?? "untitled";
        meta.Pairs.Add(new BruPair("name", true, name));
        var type = BrunoYaml.AsString(BrunoYaml.Get(info, "type")) ?? "http";
        meta.Pairs.Add(new BruPair("type", true, type));
        var seq = BrunoYaml.AsInt(BrunoYaml.Get(info, "seq"));
        if (seq is not null && type != "folder")
        {
            meta.Pairs.Add(new BruPair("seq", true, seq.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        if (BrunoYaml.SequenceOf(info, "tags") is { } tagsSeq)
        {
            foreach (var tag in tagsSeq.Children)
            {
                if (tag is YamlScalarNode { Value: not null } scalar &&
                    !scalar.Value.StartsWith(Model.BrunoItem.SentinelTagPrefix, StringComparison.Ordinal))
                {
                    meta.TagsList.Add(scalar.Value);
                }
            }
        }

        blocks.Add(meta);

        if (type == "folder")
        {
            return blocks; // folders serialize as meta-only folder.bru
        }

        // ---- method ----
        var method = (BrunoYaml.AsString(BrunoYaml.Get(http, "method")) ?? "GET").ToLowerInvariant();
        var url = BrunoYaml.AsString(BrunoYaml.Get(http, "url")) ?? "";
        var methodBlock = new BruBlock { Name = method, Kind = BruBlockKind.Dictionary };
        methodBlock.Pairs.Add(new BruPair("url", true, url));
        if (BrunoBodyMode(http) is { } bodyMode)
        {
            // Bruno's HTTP parser uses this canonical mode to select the matching
            // body:* block. Without it, Desktop can treat a POST/PUT as body-less.
            methodBlock.Pairs.Add(new BruPair("body", true, bodyMode));
        }

        blocks.Add(methodBlock);

        // ---- params: split query/path ----
        var pathParams = new BruBlock { Name = "params:path", Kind = BruBlockKind.Dictionary };
        var queryParams = new BruBlock { Name = "params:query", Kind = BruBlockKind.Dictionary };
        foreach (var row in ReadRows(BrunoYaml.SequenceOf(http, "params")))
        {
            var target = string.Equals(row.Type, "path", StringComparison.OrdinalIgnoreCase) ? pathParams : queryParams;
            target.Pairs.Add(ToPair(row));
        }

        if (pathParams.Pairs.Count > 0)
        {
            blocks.Add(pathParams);
        }

        if (queryParams.Pairs.Count > 0)
        {
            blocks.Add(queryParams);
        }

        // ---- headers ----
        var headers = ReadRows(BrunoYaml.SequenceOf(http, "headers")).ToList();
        if (headers.Count > 0)
        {
            var headerBlock = new BruBlock { Name = "headers", Kind = BruBlockKind.Dictionary };
            foreach (var row in headers)
            {
                headerBlock.Pairs.Add(ToPair(row));
            }

            blocks.Add(headerBlock);
        }

        // ---- body ----
        AppendBody(blocks, http);

        // ---- runtime: scripts + tests + assertions ----
        AppendRuntime(blocks, item);

        // ---- settings ----
        if (BrunoYaml.MappingOf(item.Node, "settings") is { } settings && settings.Children.Count > 0)
        {
            var settingsBlock = new BruBlock { Name = "settings", Kind = BruBlockKind.Dictionary };
            foreach (var pair in settings.Children)
            {
                if (pair.Key is YamlScalarNode { Value: not null } key)
                {
                    settingsBlock.Pairs.Add(new BruPair(key.Value, true, ScalarText(pair.Value)));
                }
            }

            blocks.Add(settingsBlock);
        }

        // ---- docs ----
        var docs = BrunoYaml.AsString(BrunoYaml.Get(item.Node, "docs"));
        if (!string.IsNullOrEmpty(docs))
        {
            blocks.Add(new BruBlock { Name = "docs", Kind = BruBlockKind.Text, Text = docs! });
        }

        // ---- sentinel metadata ----
        if (sentinelEntry is not null)
        {
            var metaBlock = new BruBlock { Name = "metadata", Kind = BruBlockKind.Dictionary };
            AddMeta(metaBlock, BruItemMapper.MetaKeyKey, BrunoYaml.AsString(BrunoYaml.Get(sentinelEntry, SentinelMetaKeys.IdentityKey)) ?? "");
            AddMeta(metaBlock, BruItemMapper.MetaKeyOperationId, BrunoYaml.AsString(BrunoYaml.Get(sentinelEntry, SentinelMetaKeys.OperationId)) ?? "");
            AddMeta(metaBlock, BruItemMapper.MetaKeyMethod, BrunoYaml.AsString(BrunoYaml.Get(sentinelEntry, SentinelMetaKeys.Method)) ?? "");
            AddMeta(metaBlock, BruItemMapper.MetaKeyPath, BrunoYaml.AsString(BrunoYaml.Get(sentinelEntry, SentinelMetaKeys.Path)) ?? "");
            AddMeta(metaBlock, BruItemMapper.MetaKeyHash, BrunoYaml.AsString(BrunoYaml.Get(sentinelEntry, SentinelMetaKeys.ContentHash)) ?? "");
            AddMeta(metaBlock, BruItemMapper.MetaKeyGenerator, BrunoYaml.AsString(BrunoYaml.Get(sentinelEntry, SentinelMetaKeys.GeneratorVersion)) ?? "");
            if (BrunoYaml.AsBool(BrunoYaml.Get(sentinelEntry, "requiresAuth")) is { } requiresAuth)
            {
                AddMeta(metaBlock, BruItemMapper.MetaKeyRequiresAuth, requiresAuth ? "true" : "false");
            }

            if (BrunoYaml.AsBool(BrunoYaml.Get(sentinelEntry, "stale")) is { } stale)
            {
                AddMeta(metaBlock, BruItemMapper.MetaKeyStale, stale ? "true" : "false");
            }

            Csv(metaBlock, BruItemMapper.MetaKeyManagedParams, BrunoYaml.SequenceOf(sentinelEntry, "managedParams"));
            Csv(metaBlock, BruItemMapper.MetaKeyManagedHeaders, BrunoYaml.SequenceOf(sentinelEntry, "managedHeaders"));
            Csv(metaBlock, BruItemMapper.MetaKeyManagedFormFields, BrunoYaml.SequenceOf(sentinelEntry, "managedFormFields"));
            if (BrunoYaml.AsBool(BrunoYaml.Get(sentinelEntry, "managedBody")) is { } managedBody)
            {
                AddMeta(metaBlock, BruItemMapper.MetaKeyManagedBody, managedBody ? "true" : "false");
            }

            var generatedExamples = GeneratedExampleRegistry.Read(sentinelEntry);
            if (generatedExamples.Count > 0)
            {
                AddMeta(metaBlock, BruItemMapper.MetaKeyGeneratedExamples,
                    GeneratedExampleRegistry.Encode(generatedExamples));
            }

            blocks.Add(metaBlock);
        }

        return blocks;
    }

    private static void AddMeta(BruBlock block, string key, string value) =>
        block.Pairs.Add(new BruPair(key, true, value));

    private static void Csv(BruBlock block, string key, YamlSequenceNode? seq)
    {
        if (seq is null || seq.Children.Count == 0)
        {
            return;
        }

        var csv = string.Join(",", seq.Children
            .OfType<YamlScalarNode>()
            .Where(s => s.Value is { })
            .Select(s => s.Value!.Trim()));
        if (csv.Length > 0)
        {
            AddMeta(block, key, csv);
        }
    }

    private sealed record RowData(string Name, string Value, string Type, bool Enabled, bool IsFile);

    private static IEnumerable<RowData> ReadRows(YamlSequenceNode? seq)
    {
        if (seq is null)
        {
            yield break;
        }

        foreach (var map in BrunoYaml.MappingItems(seq))
        {
            var name = BrunoYaml.AsString(BrunoYaml.Get(map, "name")) ?? "";
            var type = BrunoYaml.AsString(BrunoYaml.Get(map, "type")) ?? "";
            var enabled = BrunoYaml.AsBool(BrunoYaml.Get(map, "disabled")) != true;
            string value;
            if (type == "file")
            {
                // File rows store one path or a sequence of paths; serialize as a|b list.
                value = BrunoYaml.Get(map, "value") switch
                {
                    YamlScalarNode { Value: not null } s => s.Value,
                    YamlSequenceNode arr => string.Join("|", arr.Children
                        .Select(c => c is YamlScalarNode { Value: not null } sv ? sv.Value : "")),
                    _ => "",
                };
            }
            else
            {
                value = BrunoYaml.Get(map, "value") switch
                {
                    YamlScalarNode { Value: not null } s => s.Value,
                    _ => "",
                };
            }

            yield return new RowData(name, value, type, enabled, type == "file");
        }
    }

    private static BruPair ToPair(RowData row) =>
        new(row.Name, row.Enabled, row.IsFile ? BrunoFileValue(row.Value) : row.Value);

    private static void AppendBody(List<BruBlock> blocks, YamlMappingNode http)
    {
        var body = BrunoYaml.MappingOf(http, "body");
        if (body is null)
        {
            return;
        }

        var bodyType = BrunoYaml.AsString(BrunoYaml.Get(body, "type"));

        switch (bodyType)
        {
            case "json":
            case "text":
            case "xml":
            case "sparql":
                blocks.Add(new BruBlock
                {
                    Name = bodyType == "json" ? "body:json" : $"body:{bodyType}",
                    Kind = BruBlockKind.Text,
                    Text = BrunoYaml.AsString(BrunoYaml.Get(body, "data")) ?? "",
                });
                break;

            case "form-urlencoded":
            case "multipart-form":
            {
                var isMultipart = bodyType == "multipart-form";
                var formBlock = new BruBlock
                {
                    Name = isMultipart ? "body:multipart-form" : "body:form-urlencoded",
                    Kind = BruBlockKind.Dictionary,
                };
                foreach (var row in ReadRows(BrunoYaml.SequenceOf(body, "data")))
                {
                    var value = isMultipart && row.IsFile ? BrunoFileValue(row.Value) : row.Value;
                    formBlock.Pairs.Add(new BruPair(row.Name, row.Enabled, value));
                }

                blocks.Add(formBlock);
                break;
            }
        }
    }

    private static string? BrunoBodyMode(YamlMappingNode http)
    {
        var body = BrunoYaml.MappingOf(http, "body");
        return body is null ? null : BrunoYaml.AsString(BrunoYaml.Get(body, "type")) switch
        {
            "json" => "json",
            "multipart-form" => "multipartForm",
            "form-urlencoded" => "formUrlEncoded",
            "text" => "text",
            "xml" => "xml",
            "sparql" => "sparql",
            _ => null,
        };
    }

    private static string BrunoFileValue(string value) => "@file(" + value + ")";

    private static void AppendRuntime(List<BruBlock> blocks, BrunoItem item)
    {
        if (BrunoYaml.MappingOf(item.Node, "runtime") is not { } runtime)
        {
            return;
        }

        if (BrunoYaml.SequenceOf(runtime, "scripts") is { } scripts)
        {
            foreach (var script in BrunoYaml.MappingItems(scripts))
            {
                var type = BrunoYaml.AsString(BrunoYaml.Get(script, "type")) ?? "";
                var code = BrunoYaml.AsString(BrunoYaml.Get(script, "code")) ?? "";
                var blockName = type switch
                {
                    "before-request" or "pre-request" => "script:pre-request",
                    "after-response" or "post-response" => "script:post-response",
                    _ => "tests",
                };
                blocks.Add(new BruBlock { Name = blockName, Kind = BruBlockKind.Text, Text = code });
            }
        }

        if (BrunoYaml.SequenceOf(runtime, "assertions") is { } assertions && assertions.Children.Count > 0)
        {
            var assertBlock = new BruBlock { Name = "assert", Kind = BruBlockKind.Dictionary };
            foreach (var assertion in BrunoYaml.MappingItems(assertions))
            {
                var expression = BrunoYaml.AsString(BrunoYaml.Get(assertion, "expression")) ?? "";
                var op = BrunoYaml.AsString(BrunoYaml.Get(assertion, "operator")) ?? "eq";
                var value = BrunoYaml.AsString(BrunoYaml.Get(assertion, "value")) ?? "";
                assertBlock.Pairs.Add(new BruPair(expression, true, $"{op} {value}".Trim()));
            }

            blocks.Add(assertBlock);
        }
    }

    private static string ScalarText(YamlNode node) => node switch
    {
        YamlScalarNode { Value: not null } s => s.Value,
        _ => "",
    };
}

/// <summary>Serializes a canonical example mapping into an official `example {}` raw block.</summary>
public static class BruExampleWriter
{
    public static BruBlock FromCanonical(YamlMappingNode example)
    {
        var sb = new StringBuilder();
        sb.Append("example {\n");
        // Bruno v2's example writer indents the example content by two spaces.
        // Its nested request dictionaries then step through 4 and 6 spaces.
        var indent = new StringBuilder("  ");
        var name = BrunoYaml.AsString(BrunoYaml.Get(example, "name"));
        if (!string.IsNullOrEmpty(name))
        {
            sb.Append(indent).Append("name: ").Append(name).Append('\n');
        }

        var description = ExampleDescription(example);
        if (!string.IsNullOrEmpty(description))
        {
            sb.Append(indent).Append("description: ").Append(description.Replace("\n", "\n    ", StringComparison.Ordinal)).Append('\n');
        }

        if (BrunoYaml.MappingOf(example, "request") is { } request)
        {
            sb.Append('\n').Append(indent).AppendLine("request: {");
            WriteRequest(sb, request);
            sb.Append(indent).AppendLine("}");
        }

        if (BrunoYaml.MappingOf(example, "response") is { } response)
        {
            sb.Append('\n').Append(indent).AppendLine("response: {");
            WriteResponse(sb, response);
            sb.Append(indent).AppendLine("}");
        }

        sb.Append('}');
        return new BruBlock { Name = "example", Kind = BruBlockKind.Raw, RawLines = [.. sb.ToString().Replace("\r\n", "\n").Split('\n')] };
    }

    private static string ExampleDescription(YamlMappingNode example)
    {
        var raw = BrunoYaml.Get(example, "description");
        return raw switch
        {
            YamlScalarNode { Value: not null } s => s.Value,
            YamlMappingNode map => BrunoYaml.AsString(BrunoYaml.Get(map, "content")) ?? "",
            _ => "",
        };
    }

    private static void WriteRequest(StringBuilder sb, YamlMappingNode request)
    {
        var pad = new StringBuilder("    ");
        var url = BrunoYaml.AsString(BrunoYaml.Get(request, "url"));
        if (!string.IsNullOrEmpty(url))
        {
            sb.Append(pad).Append("url: ").Append(url).Append('\n');
        }

        var method = BrunoYaml.AsString(BrunoYaml.Get(request, "method"));
        if (!string.IsNullOrEmpty(method))
        {
            sb.Append(pad).Append("method: ").Append(method.ToLowerInvariant()).Append('\n');
        }

        // params: split by declared type.
        var queryPairs = new List<(string Key, string Value, bool Enabled)>();
        var pathPairs = new List<(string Key, string Value, bool Enabled)>();
        foreach (var row in Rows(request, "params"))
        {
            var target = row.Type == "path" ? pathPairs : queryPairs;
            target.Add((row.Name, row.Value, row.Enabled));
        }

        void EmitPairs(string label, IEnumerable<(string Key, string Value, bool Enabled)> pairs)
        {
            var list = pairs.ToList();
            if (list.Count == 0)
            {
                return;
            }

            sb.Append('\n').Append(pad).Append(label).Append(": {\n");
            foreach (var (key, value, enabled) in list)
            {
                sb.Append("      ").Append(enabled ? "" : "~").Append(key).Append(": ").Append(value).Append('\n');
            }

            sb.Append(pad).Append("}\n");
        }

        EmitPairs("params:query", queryPairs);
        EmitPairs("params:path", pathPairs);

        var headerRows = Rows(request, "headers").ToList();
        if (headerRows.Count > 0)
        {
            sb.Append('\n').Append(pad).Append("headers: {\n");
            foreach (var row in headerRows)
            {
                sb.Append("      ").Append(row.Enabled ? "" : "~").Append(row.Name).Append(": ").Append(row.Value).Append('\n');
            }

            sb.Append(pad).Append("}\n");
        }

        if (BrunoYaml.MappingOf(request, "body") is { } body)
        {
            var type = BrunoYaml.AsString(BrunoYaml.Get(body, "type")) ?? "json";
            sb.Append('\n').Append(pad).Append("mode: ").Append(BrunoBodyMode(type)).Append('\n');
            switch (type)
            {
                case "json":
                case "text":
                case "xml":
                    EmitText(sb, pad.ToString(), $"body:{type}", BrunoYaml.AsString(BrunoYaml.Get(body, "data")) ?? "");
                    break;
                case "form-urlencoded":
                case "multipart-form":
                {
                    sb.Append('\n').Append(pad).Append($"body:{type}: {{\n");
                    foreach (var row in Rows(body, "data"))
                    {
                        var value = row.Value;
                        if (type == "multipart-form" && row.IsFile)
                        {
                            value = BrunoFileValue(value);
                        }

                        sb.Append("      ")
                          .Append(row.Enabled ? "" : "~").Append(row.Name).Append(": ")
                          .Append(value).Append('\n');
                    }

                    sb.Append(pad).Append("}\n");
                    break;
                }
            }
        }
    }

    private static void WriteResponse(StringBuilder sb, YamlMappingNode response)
    {
        var pad = new StringBuilder("    ");
        var status = BrunoYaml.AsInt(BrunoYaml.Get(response, "status"));
        var statusText = BrunoYaml.AsString(BrunoYaml.Get(response, "statusText"));
        if (status.HasValue || !string.IsNullOrEmpty(statusText))
        {
            sb.Append('\n').Append(pad).Append("status: {\n");
            if (status.HasValue)
            {
                sb.Append("      code: ").Append(status.Value).Append('\n');
            }

            if (!string.IsNullOrEmpty(statusText))
            {
                sb.Append("      text: ").Append(statusText).Append('\n');
            }

            sb.Append(pad).Append("}\n");
        }

        if (BrunoYaml.MappingOf(response, "body") is { } body &&
            BrunoYaml.AsString(BrunoYaml.Get(body, "type")) is { Length: > 0 } bodyType)
        {
            sb.Append('\n').Append(pad).Append("body: {\n");
            sb.Append("      type: ").Append(bodyType).Append('\n');
            var content = BrunoYaml.AsString(BrunoYaml.Get(body, "data")) ?? "";
            sb.Append("      content: '''\n");
            foreach (var line in content.Replace("\r\n", "\n").Split('\n'))
            {
                sb.Append(line.Length > 0 ? "        " + line + "\n" : "\n");
            }

            sb.Append("      '''\n");
            sb.Append(pad).Append("}\n");
        }
    }

    private static void EmitText(StringBuilder sb, string pad, string label, string text)
    {
        // Payload sits TWO levels deeper than the section closer so the terminating
        // "<indent>}" line is unambiguous even when the payload contains braces.
        var deep = new StringBuilder(pad.Length + 2);
        deep.Append(' ').Append(' ');
        _ = deep;
        sb.Append('\n').Append(pad).Append(label).Append(": {\n");
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            sb.Append("      ").Append(line).Append('\n');
        }

        sb.Append(pad).Append("}\n");
    }

    private static string BrunoBodyMode(string type) => type switch
    {
        "multipart-form" => "multipartForm",
        "form-urlencoded" => "formUrlEncoded",
        _ => type,
    };

    private static string BrunoFileValue(string value) => "@file(" + value + ")";

    private sealed record ExRow(string Name, string Value, string Type, bool Enabled, bool IsFile);

    private static IEnumerable<ExRow> Rows(YamlMappingNode container, string key)
    {
        if (BrunoYaml.SequenceOf(container, key) is not { } seq)
        {
            yield break;
        }

        foreach (var map in BrunoYaml.MappingItems(seq))
        {
            var name = BrunoYaml.AsString(BrunoYaml.Get(map, "name")) ?? "";
            var type = BrunoYaml.AsString(BrunoYaml.Get(map, "type")) ?? "";
            var enabled = BrunoYaml.AsBool(BrunoYaml.Get(map, "disabled")) != true;
            var isFile = type == "file";
            var value = BrunoYaml.Get(map, "value") switch
            {
                YamlScalarNode { Value: not null } s => s.Value,
                YamlSequenceNode arr when isFile => string.Join("|", arr.Children
                    .Select(c => c is YamlScalarNode { Value: not null } sv ? sv.Value : "")),
                _ => "",
            };

            yield return new ExRow(name, value, type, enabled, isFile);
        }
    }
}
