using System.Security.Cryptography;
using System.Text.Json.Nodes;
using OpenApiSentinel.Core;
using OpenApiSentinel.OpenApi.Model;
using YamlDotNet.RepresentationModel;

namespace OpenApiSentinel.Bruno;

/// <summary>Stable identity of one operation, used to match requests across syncs.</summary>
public sealed record OperationIdentity(string? OperationId, string Method, string NormalizedPath)
{
    /// <summary>
    /// Identity key: operationId when present (preferred), else method + normalized path.
    /// File names are never used for matching.
    /// </summary>
    public string StableKey => OperationId is { Length: > 0 }
        ? $"opid:{OperationId}"
        : $"m+p:{Method} {NormalizedPath}";

    public static OperationIdentity From(Operation operation) => new(
        string.IsNullOrWhiteSpace(operation.OperationId) ? null : operation.OperationId!.Trim(),
        operation.Method.ToHttpString(),
        NormalizePath(operation.Path));

    public static string NormalizePath(string path) =>
        "/" + path.Trim().TrimStart('/').TrimEnd('/');
}

/// <summary>
/// Sentinel-managed metadata stored in the collection-level
/// <c>extensions.x-sentinel.operations["&lt;StableKey&gt;"]</c> index. Items bind to their entry via
/// the schema-legal <c>info.tags</c> binding tag. This block is what distinguishes
/// contract-derived state from user-maintained content and what keeps identity stable
/// across file reorganizations.
/// </summary>
public static class SentinelMetaKeys
{
    public const string OperationId = "operationId";
    public const string Method = "method";
    public const string Path = "path";
    public const string IdentityKey = "identityKey";
    public const string ContentHash = "contentHash";
    public const string GeneratorVersion = "generatorVersion";
    public const string GeneratedExamples = "examples";
    public const string ExampleId = "id";
    public const string ExampleName = "name";
}

public static class OperationIdentityExtensions
{
    /// <summary>
    /// Resolves the stored identity of a request: its binding tag selects the index entry,
    /// whose method/path (or legacy identityKey) fields define identity. Null when the item
    /// is unbound or its entry carries no usable identity.
    /// </summary>
    public static OperationIdentity? IdentityOf(this Model.BrunoItem item, SentinelMetaStore store)
    {
        var key = item.SentinelTagKey;
        if (key is null)
        {
            return null;
        }

        // The entry may have been removed by hand — treat as unmarked rather than crash.
        if (store.EntryOrNull(key) is not { } meta)
        {
            return null;
        }

        return IdentityFromEntry(key, meta);
    }

    private static OperationIdentity? IdentityFromEntry(string stableKey, YamlMappingNode meta)
    {
        var opIdRaw = BrunoYaml.AsString(BrunoYaml.Get(meta, SentinelMetaKeys.OperationId));
        var opId = string.IsNullOrWhiteSpace(opIdRaw) ? null : opIdRaw!.Trim();
        var method = BrunoYaml.AsString(BrunoYaml.Get(meta, SentinelMetaKeys.Method));
        var path = BrunoYaml.AsString(BrunoYaml.Get(meta, SentinelMetaKeys.Path));

        // Explicit method/path fields are authoritative (always written together).
        if (!string.IsNullOrWhiteSpace(method) && !string.IsNullOrWhiteSpace(path))
        {
            return new OperationIdentity(opId, method!.ToUpperInvariant(), OperationIdentity.NormalizePath(path!));
        }

        // Legacy entries may only carry the composite key ("opid:x" / "m+p:M /p").
        if (stableKey.StartsWith("opid:", StringComparison.Ordinal))
        {
            return new OperationIdentity(stableKey[5..], "", "");
        }

        if (stableKey.StartsWith("m+p:", StringComparison.Ordinal))
        {
            var body = stableKey[4..];
            var space = body.IndexOf(' ');
            if (space > 0)
            {
                return new OperationIdentity(opId, body[..space].ToUpperInvariant(), OperationIdentity.NormalizePath(body[(space + 1)..]));
            }
        }

        return null;
    }

    /// <summary>Binds the item to this identity's index entry and records the current content hash.</summary>
    public static void WriteIdentity(this Model.BrunoItem item, SentinelMetaStore store, OperationIdentity identity, string contentHash)
    {
        item.BindSentinelTag(identity.StableKey);
        var meta = store.GetOrCreateEntry(identity.StableKey);
        BrunoYaml.SetString(meta, SentinelMetaKeys.OperationId, identity.OperationId);
        BrunoYaml.SetString(meta, SentinelMetaKeys.Method, identity.Method);
        BrunoYaml.SetString(meta, SentinelMetaKeys.Path, identity.NormalizedPath);
        BrunoYaml.SetString(meta, SentinelMetaKeys.IdentityKey, identity.StableKey);
        BrunoYaml.SetString(meta, SentinelMetaKeys.ContentHash, contentHash);
    }

    public static string? StoredContentHash(this Model.BrunoItem item, SentinelMetaStore store) =>
        item.SentinelTagKey is { } key && store.EntryOrNull(key) is { } meta
            ? BrunoYaml.AsString(BrunoYaml.Get(meta, SentinelMetaKeys.ContentHash))
            : null;

    public static void SetGeneratorVersion(this Model.BrunoItem item, SentinelMetaStore store, string version) =>
        BrunoYaml.SetString(store.GetOrCreateEntry(item.SentinelTagKey ?? throw new InvalidOperationException(
                "generatorVersion requires a bound identity; call WriteIdentity first.")),
            SentinelMetaKeys.GeneratorVersion, version);

    /// <summary>Reads the recorded generated-example registry as an ordered id → name list.</summary>
    public static List<(string Id, string Name)> RecordedExamples(this Model.BrunoItem item, SentinelMetaStore store)
    {
        var result = new List<(string, string)>();
        if (item.SentinelTagKey is not { } key || store.EntryOrNull(key) is not { } meta)
        {
            return result;
        }

        if (BrunoYaml.SequenceOf(meta, SentinelMetaKeys.GeneratedExamples) is not { } seq)
        {
            return result;
        }

        foreach (var entry in BrunoYaml.MappingItems(seq))
        {
            var id = BrunoYaml.AsString(BrunoYaml.Get(entry, SentinelMetaKeys.ExampleId));
            var name = BrunoYaml.AsString(BrunoYaml.Get(entry, SentinelMetaKeys.ExampleName));
            if (id is not null && name is not null)
            {
                result.Add((id, name));
            }
        }

        return result;
    }

    public static void WriteRecordedExamples(this Model.BrunoItem item, SentinelMetaStore store, IReadOnlyList<(string Id, string Name)> entries)
    {
        var meta = store.GetOrCreateEntry(item.SentinelTagKey!);
        if (entries.Count == 0)
        {
            BrunoYaml.RemoveKey(meta, SentinelMetaKeys.GeneratedExamples);
            return;
        }

        var seq = new YamlSequenceNode();
        foreach (var (id, name) in entries)
        {
            var map = new YamlMappingNode();
            BrunoYaml.SetString(map, SentinelMetaKeys.ExampleId, id);
            BrunoYaml.SetString(map, SentinelMetaKeys.ExampleName, name);
            seq.Children.Add(map);
        }

        BrunoYaml.Set(meta, SentinelMetaKeys.GeneratedExamples, seq);
    }
}

/// <summary>
/// Deterministic fingerprint of everything in a contract that the sync derives requests
/// from. Pure prose (descriptions/summaries/vendor extensions) is excluded so doc-only
/// edits do not trigger collection churn; structural changes always change the hash.
/// </summary>
public static class ContractFingerprint
{
    private static readonly System.Text.Json.JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
    };

    public static string Compute(OpenApi.Model.ApiDocument document)
    {
        var operations = new List<JsonNode>();
        foreach (var path in document.Paths.Values.OrderBy(p => p.Path, StringComparer.Ordinal))
        {
            foreach (var op in path.Operations.Values.OrderBy(o => o.Method))
            {
                operations.Add(ProjectOperation(OperationIdentity.From(op), op));
            }
        }

        operations.Sort((a, b) => string.CompareOrdinal(a["k"]!.GetValue<string>(), b["k"]!.GetValue<string>()));

        var projection = new JsonArray();
        if (document.Servers.Count > 0)
        {
            projection.Add(document.Servers[0].Url);
        }

        foreach (var op in operations)
        {
            projection.Add(op);
        }

        var json = System.Text.Json.JsonSerializer.Serialize(projection, Options);
        return Sha256Hex(json);
    }

    /// <summary>Fingerprint of a single operation's contract-derived surface.</summary>
    public static string ComputeOperationHash(OperationIdentity identity, Operation operation) =>
        Sha256Hex(System.Text.Json.JsonSerializer.Serialize(ProjectOperation(identity, operation), Options));

    private static JsonObject ProjectOperation(OperationIdentity identity, Operation operation)
    {
        var node = new JsonObject
        {
            ["k"] = identity.StableKey,
            ["m"] = identity.Method,
            ["p"] = identity.NormalizedPath,
        };

        if (identity.OperationId is not null)
        {
            node["oid"] = identity.OperationId;
        }

        if (operation.Deprecated)
        {
            node["dep"] = true;
        }

        if (operation.Tags.Count > 0)
        {
            var tags = new JsonArray();
            foreach (var tag in operation.Tags.Order(StringComparer.Ordinal))
            {
                tags.Add(tag);
            }

            node["tags"] = tags;
        }

        var parameters = new JsonArray();
        foreach (var parameter in operation.Parameters)
        {
            parameters.Add(new JsonObject
            {
                ["n"] = parameter.Name,
                ["in"] = parameter.In.ToString().ToLowerInvariant(),
                ["req"] = parameter.Required,
                ["style"] = parameter.Style,
                ["explode"] = parameter.Explode,
                ["schema"] = StripProse(parameter.Schema?.Node),
            });
        }

        if (parameters.Count > 0)
        {
            node["params"] = parameters;
        }

        if (operation.RequestBody is { } body)
        {
            var contentTypes = new JsonArray();
            foreach (var contentType in body.Content.Keys.Order(StringComparer.OrdinalIgnoreCase))
            {
                contentTypes.Add(contentType);
            }

            JsonNode? primarySchema = null;
            foreach (var contentType in body.Content.Keys.Order(StringComparer.OrdinalIgnoreCase))
            {
                primarySchema = StripProse(body.Content[contentType].Node);
                break;
            }

            node["body"] = new JsonObject
            {
                ["req"] = body.Required,
                ["cts"] = contentTypes,
                ["schema"] = primarySchema,
            };
        }

        var responses = new JsonObject();
        foreach (var code in operation.Responses.Keys.Order(StringComparer.Ordinal))
        {
            var response = operation.Responses[code];
            var responseNode = new JsonObject();
            var contentTypes = new JsonArray();
            foreach (var contentType in response.Content.Keys.Order(StringComparer.OrdinalIgnoreCase))
            {
                contentTypes.Add(contentType);
            }

            if (contentTypes.Count > 0)
            {
                responseNode["cts"] = contentTypes;
            }

            var headers = new JsonArray();
            foreach (var header in response.Headers.OrderBy(h => h.Name, StringComparer.Ordinal))
            {
                headers.Add(new JsonObject
                {
                    ["n"] = header.Name,
                    ["req"] = header.Required,
                    ["schema"] = StripProse(header.Schema?.Node),
                });
            }

            if (headers.Count > 0)
            {
                responseNode["headers"] = headers;
            }

            responses[code] = responseNode;
        }

        if (responses.Count > 0)
        {
            node["resp"] = responses;
        }

        return node;
    }

    /// <summary>Removes prose-only keys so documentation edits never invalidate hashes.</summary>
    internal static JsonNode? StripProse(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                var clone = new JsonObject();
                foreach (var property in obj)
                {
                    if (property.Key is "description" or "summary" or "title" ||
                        property.Key.StartsWith("x-", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    clone[property.Key] = StripProse(property.Value);
                }

                return clone;
            }

            case JsonArray array:
            {
                var clone = new JsonArray();
                foreach (var item in array)
                {
                    clone.Add(StripProse(item));
                }

                return clone;
            }

            default:
                return node?.DeepClone();
        }
    }

    public static string Sha256Hex(string text)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
