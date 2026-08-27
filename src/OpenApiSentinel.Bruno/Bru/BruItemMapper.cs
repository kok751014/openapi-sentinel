using System.Text;
using OpenApiSentinel.Bruno.Model;
using YamlDotNet.RepresentationModel;

namespace OpenApiSentinel.Bruno;

/// <summary>
/// Maps one .bru request file to the canonical OpenCollection-style YAML item and back.
/// Officially-mappable blocks become schema-legal canonical fields; anything without a
/// canonical home (examples written directly in .bru, vars, app, OAuth extras…) is kept
/// in a raw-preservation registry keyed by stable identity key and re-emitted verbatim,
/// so hand-authored content survives every sync.
/// </summary>
public static class BruItemMapper
{
    // ---- Sentinel metadata keys carried in the officially-supported metadata {} block ----
    public const string MetaKeyKey = "x-sentinel-key";
    public const string MetaKeyOperationId = "x-sentinel-operation-id";
    public const string MetaKeyMethod = "x-sentinel-method";
    public const string MetaKeyPath = "x-sentinel-path";
    public const string MetaKeyHash = "x-sentinel-hash";
    public const string MetaKeyGenerator = "x-sentinel-generator";
    public const string MetaKeyRequiresAuth = "x-sentinel-requires-auth";
    public const string MetaKeyStale = "x-sentinel-stale";
    public const string MetaKeyManagedParams = "x-sentinel-managed-params";
    public const string MetaKeyManagedHeaders = "x-sentinel-managed-headers";
    public const string MetaKeyManagedFormFields = "x-sentinel-managed-form-fields";
    public const string MetaKeyManagedBody = "x-sentinel-managed-body";
    public const string MetaKeyGeneratedExamples = "x-sentinel-generated-examples";

    /// <summary>Maps a parsed .bru request file to a canonical request item.</summary>
    /// <returns>The canonical item plus the Sentinel index entry derived from its metadata block.</returns>
    public static (BrunoItem Item, YamlMappingNode? SentinelEntry) ToCanonical(BruFile file, string sourceName)
    {
        var node = new YamlMappingNode();
        var entry = new YamlMappingNode();

        var http = new YamlMappingNode();
        var hasHttp = false;
        var runtime = new YamlMappingNode();
        var hasRuntime = false;

        foreach (var block in file.Blocks)
        {
            switch (block.Name)
            {
                case "meta":
                    ApplyMeta(block, node);
                    break;

                case "get" or "post" or "put" or "delete" or "patch" or "options" or "head" or "connect" or "trace" or "http":
                    hasHttp = true;
                    foreach (var pair in block.Pairs)
                    {
                        if (pair.RawKey == "url")
                        {
                            BrunoYaml.SetString(http, "url", pair.Value);
                        }
                        else if (pair.RawKey == "method")
                        {
                            BrunoYaml.SetString(http, "method", pair.Value.ToUpperInvariant());
                        }
                        else if (pair.RawKey is "body" or "auth" && pair.Value is "none")
                        {
                            // explicit "none" markers carry no data
                        }
                    }

                    if (!BrunoYaml.HasKey(http, "method"))
                    {
                        BrunoYaml.SetString(http, "method", block.Name.ToUpperInvariant());
                    }

                    break;

                case "params:path" or "query":
                    AddParams(http, block, "path", disabledDefault: false);
                    break;

                case "params:query":
                    AddParams(http, block, "query", disabledDefault: false);
                    break;

                case "headers":
                {
                    hasHttp = true;
                    var headers = BrunoYaml.GetOrCreateSequence(http, "headers");
                    foreach (var pair in block.Pairs)
                    {
                        headers.Children.Add(Row(pair, null));
                    }

                    break;
                }

                case "body":
                case "body:json":
                case "body:text":
                case "body:xml":
                case "body:sparql":
                case "body:form-urlencoded":
                case "body:multipart-form":
                    hasHttp = true;
                    SetCanonicalBody(http, block);
                    break;

                case "docs":
                    BrunoYaml.SetString(node, "docs", block.Text);
                    break;

                case "settings":
                {
                    var settings = new YamlMappingNode();
                    foreach (var pair in block.Pairs)
                    {
                        BrunoYaml.SetString(settings, pair.RawKey, pair.Value);
                    }

                    BrunoYaml.Set(node, "settings", settings);
                    break;
                }

                case "script:pre-request":
                    hasRuntime = true;
                    AddScript(runtime, "script:pre-request", block.Text);
                    break;

                case "script:post-response":
                    hasRuntime = true;
                    AddScript(runtime, "script:post-response", block.Text);
                    break;

                case "tests":
                    hasRuntime = true;
                    AddScript(runtime, "tests", block.Text);
                    break;

                case "assert":
                {
                    hasRuntime = true;
                    var assertions = BrunoYaml.GetOrCreateSequence(runtime, "assertions");
                    foreach (var pair in block.Pairs)
                    {
                        var row = new YamlMappingNode();
                        BrunoYaml.SetString(row, "expression", pair.Enabled ? pair.RawKey : "~" + pair.RawKey);
                        var parts = SplitAssertion(pair.Value);
                        BrunoYaml.SetString(row, "operator", parts.op);
                        BrunoYaml.SetString(row, "value", parts.value);
                        assertions.Children.Add(row);
                    }

                    break;
                }

                case "metadata":
                    ReadMetadata(block, entry);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"internal: unmapped block '{block.Name}' in {sourceName} must be routed to raw preservation.");
            }
        }

        if (hasHttp)
        {
            BrunoYaml.Set(node, "http", http);
        }

        if (hasRuntime)
        {
            BrunoYaml.Set(node, "runtime", runtime);
        }

        return (new BrunoItem(node), entry.Children.Count > 0 ? entry : null);

        static void AddScript(YamlMappingNode runtimeTarget, string kind, string code)
        {
            var scripts = BrunoYaml.GetOrCreateSequence(runtimeTarget, "scripts");
            var row = new YamlMappingNode();
            var type = kind switch
            {
                "script:pre-request" => "before-request",
                "script:post-response" => "after-response",
                _ => "tests",
            };
            BrunoYaml.SetString(row, "type", type);
            BrunoYaml.Set(row, "code", new YamlScalarNode(code) { Style = YamlDotNet.Core.ScalarStyle.Literal });
            scripts.Children.Add(row);
        }
    }

    private static void ApplyMeta(BruBlock meta, YamlMappingNode item)
    {
        var info = new YamlMappingNode();
        var userTags = new List<string>();
        foreach (var pair in meta.Pairs)
        {
            switch (pair.RawKey)
            {
                case "name":
                    BrunoYaml.SetString(info, "name", pair.Value);
                    break;
                case "type":
                    BrunoYaml.SetString(info, "type", pair.Value);
                    break;
                case "seq":
                    BrunoYaml.SetInt(info, "seq", int.TryParse(pair.Value, out var seq) ? seq : 0);
                    break;
                default:
                    userTags.Add(pair.Value.Length > 0 ? pair.Value : pair.RawKey);
                    break;
            }
        }

        foreach (var tag in meta.TagsList)
        {
            userTags.Add(tag);
        }

        if (userTags.Count > 0)
        {
            var tagsSeq = new YamlSequenceNode();
            foreach (var tag in userTags)
            {
                tagsSeq.Children.Add(BrunoYaml.Scalar(tag));
            }

            BrunoYaml.Set(info, "tags", tagsSeq);
        }

        BrunoYaml.Set(item, "info", info);
    }

    public sealed record MetaParse(string Name, string Type, int Seq, List<string> Tags);

    /// <summary>Parses a meta block's display fields (used by the workspace reader).</summary>
    public static MetaParse ParseMeta(BruBlock meta)
    {
        string name = "";
        var type = "http";
        var seq = 0;
        foreach (var pair in meta.Pairs)
        {
            switch (pair.RawKey)
            {
                case "name":
                    name = pair.Value;
                    break;
                case "type":
                    type = pair.Value;
                    break;
                case "seq":
                    _ = int.TryParse(pair.Value, out seq);
                    break;
            }
        }

        return new MetaParse(name, type, seq, [.. meta.TagsList]);
    }

    private static YamlMappingNode Row(BruPair pair, string? type)
    {
        var row = new YamlMappingNode();
        BrunoYaml.SetString(row, "name", pair.RawKey);
        BrunoYaml.SetString(row, "value", pair.Value);
        if (type is not null)
        {
            BrunoYaml.SetString(row, "type", type);
        }

        if (!pair.Enabled)
        {
            BrunoYaml.SetBool(row, "disabled", true);
        }

        return row;
    }

    private static void AddParams(YamlMappingNode http, BruBlock block, string type, bool disabledDefault)
    {
        _ = disabledDefault;
        var paramsSeq = BrunoYaml.GetOrCreateSequence(http, "params");
        foreach (var pair in block.Pairs)
        {
            paramsSeq.Children.Add(Row(pair, type));
        }
    }

    private static void SetCanonicalBody(YamlMappingNode http, BruBlock block)
    {
        var body = new YamlMappingNode();
        switch (block.Name)
        {
            case "body":
            case "body:json":
            case "body:text":
            case "body:xml":
            case "body:sparql":
            {
                var type = block.Name switch
                {
                    "body" or "body:json" => "json",
                    "body:text" => "text",
                    "body:xml" => "xml",
                    _ => "sparql",
                };
                BrunoYaml.SetString(body, "type", type);
                BrunoYaml.Set(body, "data", new YamlScalarNode(block.Text) { Style = YamlDotNet.Core.ScalarStyle.Literal });
                break;
            }

            case "body:form-urlencoded":
            case "body:multipart-form":
            {
                BrunoYaml.SetString(body, "type",
                    block.Name == "body:multipart-form" ? "multipart-form" : "form-urlencoded");
                var data = new YamlSequenceNode();
                foreach (var pair in block.Pairs)
                {
                    var row = new YamlMappingNode();
                    BrunoYaml.SetString(row, "name", pair.RawKey);
                    if (block.Name == "body:multipart-form")
                    {
                        if (TryFileValue(pair.Value, out var files))
                        {
                            BrunoYaml.SetString(row, "type", "file");
                            if (files.Length == 1)
                            {
                                BrunoYaml.SetString(row, "value", files[0]);
                            }
                            else
                            {
                                var arr = new YamlSequenceNode();
                                foreach (var file in files)
                                {
                                    arr.Children.Add(BrunoYaml.Scalar(file));
                                }

                                BrunoYaml.Set(row, "value", arr);
                            }
                        }
                        else if (pair.Value.StartsWith('@'))
                        {
                            // Compatibility for Sentinel files emitted before native writer
                            // output was corrected to @file(...).
                            BrunoYaml.SetString(row, "type", "file");
                            BrunoYaml.SetString(row, "value", pair.Value[1..]);
                        }
                        else
                        {
                            BrunoYaml.SetString(row, "type", "text");
                            BrunoYaml.SetString(row, "value", pair.Value);
                        }
                    }
                    else
                    {
                        BrunoYaml.SetString(row, "value", pair.Value);
                    }

                    if (!pair.Enabled)
                    {
                        BrunoYaml.SetBool(row, "disabled", true);
                    }

                    data.Children.Add(row);
                }

                BrunoYaml.Set(body, "data", data);
                break;
            }
        }

        BrunoYaml.Set(http, "body", body);
    }

    private static bool TryFileValue(string value, out string[] files)
    {
        files = [];
        if (!value.StartsWith("@file(", StringComparison.Ordinal) || !value.EndsWith(')'))
        {
            return false;
        }

        files = value[6..^1].Split('|', StringSplitOptions.RemoveEmptyEntries);
        return files.Length > 0;
    }

    private static (string op, string value) SplitAssertion(string value)
    {
        var space = value.IndexOf(' ');
        return space > 0
            ? (value[..space], value[(space + 1)..].Trim())
            : ("eq", value);
    }

    private static void ReadMetadata(BruBlock block, YamlMappingNode entry)
    {
        foreach (var pair in block.Pairs)
        {
            switch (pair.RawKey)
            {
                case MetaKeyKey:
                    BrunoYaml.SetString(entry, SentinelMetaKeys.IdentityKey, pair.Value);
                    break;
                case MetaKeyOperationId:
                    BrunoYaml.SetString(entry, SentinelMetaKeys.OperationId, NullIfEmpty(pair.Value));
                    break;
                case MetaKeyMethod:
                    BrunoYaml.SetString(entry, SentinelMetaKeys.Method, pair.Value);
                    break;
                case MetaKeyPath:
                    BrunoYaml.SetString(entry, SentinelMetaKeys.Path, pair.Value);
                    break;
                case MetaKeyHash:
                    BrunoYaml.SetString(entry, SentinelMetaKeys.ContentHash, pair.Value);
                    break;
                case MetaKeyGenerator:
                    BrunoYaml.SetString(entry, SentinelMetaKeys.GeneratorVersion, pair.Value);
                    break;
                case MetaKeyRequiresAuth:
                    BrunoYaml.SetBool(entry, "requiresAuth", pair.Value is "true");
                    break;
                case MetaKeyStale:
                    BrunoYaml.SetBool(entry, "stale", pair.Value is "true");
                    break;
                case MetaKeyManagedParams:
                    CsvToSeq(entry, "managedParams", pair.Value);
                    break;
                case MetaKeyManagedHeaders:
                    CsvToSeq(entry, "managedHeaders", pair.Value);
                    break;
                case MetaKeyManagedFormFields:
                    CsvToSeq(entry, "managedFormFields", pair.Value);
                    break;
                case MetaKeyManagedBody:
                    BrunoYaml.SetBool(entry, "managedBody", pair.Value is "true");
                    break;
                case MetaKeyGeneratedExamples:
                    GeneratedExampleRegistry.Write(entry, GeneratedExampleRegistry.Decode(pair.Value));
                    break;
            }
        }
    }

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    private static void CsvToSeq(YamlMappingNode target, string key, string csv)
    {
        var parts = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return;
        }

        var seq = new YamlSequenceNode();
        foreach (var part in parts)
        {
            seq.Children.Add(BrunoYaml.Scalar(part));
        }

        BrunoYaml.Set(target, key, seq);
    }
}
