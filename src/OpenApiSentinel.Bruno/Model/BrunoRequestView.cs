using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace OpenApiSentinel.Bruno.Model;

/// <summary>A single key/value row inside params, headers, or form bodies.</summary>
public sealed record BrunoKeyValueRow(YamlMappingNode Node)
{
    public string? Name
    {
        get => BrunoYaml.AsString(BrunoYaml.Get(Node, "name"));
        set => BrunoYaml.SetString(Node, "name", value);
    }

    public string? Value
    {
        get => BrunoYaml.AsString(BrunoYaml.Get(Node, "value"));
        set => BrunoYaml.SetString(Node, "value", value);
    }

    /// <summary>OpenCollection uses <c>disabled: true</c> (absent = enabled).</summary>
    public bool Enabled
    {
        get => BrunoYaml.AsBool(BrunoYaml.Get(Node, "disabled")) != true;
        set
        {
            if (value)
            {
                BrunoYaml.RemoveKey(Node, "disabled");
            }
            else
            {
                BrunoYaml.SetBool(Node, "disabled", true);
            }
        }
    }

    /// <summary>For params: <c>query</c> or <c>path</c>; defaults to query.</summary>
    public string ParamType
    {
        get => BrunoYaml.AsString(BrunoYaml.Get(Node, "type")) is { Length: > 0 } t ? t : "query";
        set => BrunoYaml.SetString(Node, "type", value);
    }

    public string? Description
    {
        get
        {
            var raw = BrunoYaml.Get(Node, "description");
            return raw switch
            {
                YamlScalarNode { Value: not null } s => s.Value,
                YamlMappingNode map => BrunoYaml.AsString(BrunoYaml.Get(map, "content")),
                _ => null,
            };
        }
    }
}

/// <summary>
/// Typed façade over the <c>http:</c> block of a request item. Contract-derived fields are
/// updated through explicit methods; everything unknown is left untouched.
/// </summary>
public sealed class BrunoRequestView
{
    private readonly YamlMappingNode item;

    public BrunoRequestView(YamlMappingNode item)
    {
        this.item = item;
    }

    public YamlMappingNode ItemNode => item;

    /// <summary>The request's display name (info.name); null when unset.</summary>
    public string? Name =>
        BrunoYaml.AsString(BrunoYaml.Get(BrunoYaml.MappingOf(item, "info") ?? [], "name"));

    /// <summary>The http details mapping; creates it when missing.</summary>
    public YamlMappingNode Http
    {
        get
        {
            if (BrunoYaml.MappingOf(item, "http") is { } existing)
            {
                return existing;
            }

            if (BrunoYaml.HasKey(item, "http"))
            {
                BrunoYaml.RemoveKey(item, "http");
            }

            var created = new YamlMappingNode();
            item.Children.Add(BrunoYaml.Scalar("http"), created);
            return created;
        }
    }

    private YamlMappingNode HttpReadSource => BrunoYaml.MappingOf(item, "http") ?? [];

    public string? Method
    {
        get => BrunoYaml.AsString(BrunoYaml.Get(HttpReadSource, "method"));
        set => BrunoYaml.SetString(Http, "method", value);
    }

    public string? Url
    {
        get => BrunoYaml.AsString(BrunoYaml.Get(HttpReadSource, "url"));
        set => BrunoYaml.SetString(Http, "url", value);
    }

    // ---- params ----

    /// <summary>All param rows (query + path), in stored order. Empty when absent.</summary>
    public IReadOnlyList<BrunoKeyValueRow> Params()
    {
        if (BrunoYaml.SequenceOf(Http, "params") is not { } seq)
        {
            return [];
        }

        return [.. BrunoYaml.MappingItems(seq).Select(row => new BrunoKeyValueRow(row))];
    }

    /// <summary>Returns the live params sequence (creating when needed) for mutation.</summary>
    public YamlSequenceNode ParamsSequence() => BrunoYaml.GetOrCreateSequence(Http, "params");

    /// <summary>Replaces the whole params list; null removes the key entirely.
    /// Every row carries an explicit <c>type</c> (query|path): the official schema REQUIRES it.</summary>
    public void SetParams(IEnumerable<(string Name, string Value, string Type, bool Enabled)> rows)
    {
        var list = rows.ToList();
        if (list.Count == 0)
        {
            BrunoYaml.RemoveKey(Http, "params");
            return;
        }

        var seq = new YamlSequenceNode();
        foreach (var row in list)
        {
            var map = new YamlMappingNode();
            BrunoYaml.SetString(map, "name", row.Name);
            BrunoYaml.SetString(map, "value", row.Value);
            BrunoYaml.SetString(map, "type",
                string.Equals(row.Type, "path", StringComparison.Ordinal) ? "path" : "query");

            if (!row.Enabled)
            {
                BrunoYaml.SetBool(map, "disabled", true);
            }

            seq.Children.Add(map);
        }

        BrunoYaml.Set(Http, "params", seq);
    }

    // ---- headers ----

    public IReadOnlyList<BrunoKeyValueRow> Headers()
    {
        if (BrunoYaml.SequenceOf(Http, "headers") is not { } seq)
        {
            return [];
        }

        return [.. BrunoYaml.MappingItems(seq).Select(row => new BrunoKeyValueRow(row))];
    }

    /// <summary>Merges header rows by name: existing rows keep their values unless empty,
    /// contract-supplied defaults fill gaps, removed names are dropped.</summary>
    public void MergeHeaders(IReadOnlyDictionary<string, string?> contractHeaders)
    {
        var seq = BrunoYaml.SequenceOf(Http, "headers");
        var existing = seq is null ? [] : BrunoYaml.MappingItems(seq).Select(row => new BrunoKeyValueRow(row)).ToList();
        var byName = new Dictionary<string, BrunoKeyValueRow>(StringComparer.Ordinal);
        foreach (var row in existing)
        {
            if (row.Name is { } name && !byName.ContainsKey(name))
            {
                byName[name] = row;
            }
        }

        // Remove rows whose names are no longer declared by the contract.
        var keep = existing.Where(row => row.Name is null || contractHeaders.ContainsKey(row.Name)).ToList();

        foreach (var (name, value) in contractHeaders.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            if (byName.TryGetValue(name, out var row))
            {
                if (string.IsNullOrEmpty(row.Value) && value is not null)
                {
                    row.Value = value;
                }
            }
            else
            {
                var map = new YamlMappingNode();
                BrunoYaml.SetString(map, "name", name);
                BrunoYaml.SetString(map, "value", value ?? "");
                var created = new BrunoKeyValueRow(map);
                keep.Add(created);
            }
        }

        if (keep.Count == 0)
        {
            BrunoYaml.RemoveKey(Http, "headers");
            return;
        }

        var outSeq = new YamlSequenceNode();
        foreach (var row in keep)
        {
            outSeq.Children.Add(row.Node);
        }

        BrunoYaml.Set(Http, "headers", outSeq);
    }

    // ---- body ----

    /// <summary>The body mapping or null when absent.</summary>
    public YamlMappingNode? BodyOrNull => BrunoYaml.MappingOf(Http, "body");

    public string? BodyType
    {
        get
        {
            var body = BodyOrNull;
            return body is null ? null : BrunoYaml.AsString(BrunoYaml.Get(body, "type"));
        }
    }

    /// <summary>Raw JSON text of a <c>body: {type: json, data: ...}</c> mapping; null otherwise.</summary>
    public string? JsonBodyRaw
    {
        get
        {
            var body = BodyOrNull;
            if (body is null || !string.Equals(BodyType, "json", StringComparison.Ordinal))
            {
                return null;
            }

            return BrunoYaml.AsString(BrunoYaml.Get(body, "data"));
        }
    }

    /// <summary>Sets (or replaces) the body with a JSON-mode body. Keeps position of an existing body key.</summary>
    public void SetJsonBody(string json)
    {
        var body = new YamlMappingNode();
        BrunoYaml.SetString(body, "type", "json");
        BrunoYaml.Set(body, "data", new YamlScalarNode(json) { Style = ScalarStyle.Literal });
        BrunoYaml.Set(Http, "body", body);
    }

    /// <summary>Sets (or replaces) the body with a form-style body ("multipart-form" or
    /// "form-urlencoded") matching the official OpenCollection HttpRequestBody schema.</summary>
    public void SetFormBody(string kind, IReadOnlyList<FormRow> rows)
    {
        var body = new YamlMappingNode();
        BrunoYaml.SetString(body, "type", kind);
        var data = new YamlSequenceNode();
        foreach (var row in rows)
        {
            var map = new YamlMappingNode();
            BrunoYaml.SetString(map, "name", row.Name);
            if (string.Equals(kind, "multipart-form", StringComparison.Ordinal))
            {
                BrunoYaml.SetString(map, "type", row.IsFile ? "file" : "text");
                if (row.Value.Contains('\n', StringComparison.Ordinal))
                {
                    // Multi-part values are strings per the official schema; newline content
                    // stays a single scalar (literal style) so YAML keeps it lossless.
                    BrunoYaml.Set(map, "value", new YamlScalarNode(row.Value) { Style = ScalarStyle.Literal });
                }
                else
                {
                    BrunoYaml.SetString(map, "value", row.Value);
                }
            }
            else
            {
                BrunoYaml.SetString(map, "value", row.Value);
            }

            if (!row.Enabled)
            {
                BrunoYaml.SetBool(map, "disabled", true);
            }

            data.Children.Add(map);
        }

        BrunoYaml.Set(body, "data", data);
        BrunoYaml.Set(Http, "body", body);
    }

    /// <summary>Sets (or replaces) the body with a plain-text body.</summary>
    public void SetTextBody(string text)
    {
        var body = new YamlMappingNode();
        BrunoYaml.SetString(body, "type", "text");
        BrunoYaml.Set(body, "data", new YamlScalarNode(text) { Style = ScalarStyle.Literal });
        BrunoYaml.Set(Http, "body", body);
    }

    /// <summary>Reads form-style body rows ("multipart-form" / "form-urlencoded"); empty when absent.</summary>
    public IReadOnlyList<FormRow> FormRows()
    {
        var body = BodyOrNull;
        if (body is null || BrunoYaml.SequenceOf(body, "data") is not { } data)
        {
            return [];
        }

        var type = BodyType;
        var isMultipart = string.Equals(type, "multipart-form", StringComparison.Ordinal);
        var rows = new List<FormRow>();
        foreach (var map in BrunoYaml.MappingItems(data))
        {
            var name = BrunoYaml.AsString(BrunoYaml.Get(map, "name"));
            if (name is null)
            {
                continue;
            }

            var value = BrunoYaml.Get(map, "value") switch
            {
                YamlScalarNode { Value: not null } s => s.Value,
                YamlSequenceNode seq => string.Join("|", seq.Children
                    .Select(c => c is YamlScalarNode { Value: not null } sv ? sv.Value : "")),
                _ => "",
            };

            var isFile = isMultipart &&
                         string.Equals(BrunoYaml.AsString(BrunoYaml.Get(map, "type")), "file", StringComparison.Ordinal);
            var enabled = BrunoYaml.AsBool(BrunoYaml.Get(map, "disabled")) != true;
            rows.Add(new FormRow(name, value, isFile, enabled));
        }

        return rows;
    }

    /// <summary>Raw text of a <c>body: {type: text, data: ...}</c> mapping; null otherwise.</summary>
    public string? TextBodyRaw
    {
        get
        {
            var body = BodyOrNull;
            if (body is null || !string.Equals(BodyType, "text", StringComparison.Ordinal))
            {
                return null;
            }

            return BrunoYaml.AsString(BrunoYaml.Get(body, "data"));
        }
    }

    /// <summary>Removes the body entirely (e.g. contract no longer declares one).</summary>
    public void RemoveBody() => BrunoYaml.RemoveKey(Http, "body");

    // ---- docs / meta ----

    public string? Docs
    {
        get
        {
            var raw = BrunoYaml.Get(ItemNode, "docs");
            return raw switch
            {
                YamlScalarNode { Value: not null } s => s.Value,
                YamlMappingNode map => BrunoYaml.AsString(BrunoYaml.Get(map, "content")),
                _ => null,
            };
        }
    }

    public void SetDocsIfEmpty(string markdown)
    {
        if (string.IsNullOrWhiteSpace(Docs))
        {
            BrunoYaml.SetString(ItemNode, "docs", markdown);
        }
    }

    public void SetName(string name)
    {
        var info = BrunoYaml.GetOrCreateMapping(item, "info");
        BrunoYaml.SetString(info, "name", name);
        if (BrunoYaml.AsString(BrunoYaml.Get(info, "type")) is not { Length: > 0 })
        {
            BrunoYaml.SetString(info, "type", "http");
        }
    }

    public void SetSeq(int seq)
    {
        var info = BrunoYaml.GetOrCreateMapping(item, "info");
        BrunoYaml.SetInt(info, "seq", seq);
    }

    // ---- examples ----

    public YamlSequenceNode? ExamplesOrNull => BrunoYaml.SequenceOf(item, "examples");

    public YamlSequenceNode ExamplesSequence() => BrunoYaml.GetOrCreateSequence(item, "examples");
}
