using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using YamlDotNet.RepresentationModel;

namespace OpenApiSentinel.OpenApi;

/// <summary>
/// Parses JSON or YAML text into a <see cref="JsonNode"/> tree with strict safety bounds.
/// Both formats converge on the same node model so all downstream logic is format agnostic.
/// </summary>
public static class DocumentParser
{
    /// <summary>Detects the format from content and parses accordingly.</summary>
    public static JsonNode Parse(string sourceName, string content, ProcessingLimits limits)
    {
        var trimmed = content.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
            return ParseJson(sourceName, content, limits);
        return ParseYaml(sourceName, content, limits);
    }

    public static JsonNode ParseJson(string sourceName, string content, ProcessingLimits limits)
    {
        if (Encoding.UTF8.GetByteCount(content) > limits.MaxDocumentBytes)
            throw new SpecParseException(
                $"Document '{sourceName}' exceeds the maximum supported size of {limits.MaxDocumentBytes / (1024 * 1024)} MB.");

        JsonNode? root;
        try
        {
            var options = new JsonDocumentOptions
            {
                MaxDepth = limits.MaxDepth,
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
            };
            using var doc = System.Text.Json.JsonDocument.Parse(content, options);
            root = JsonSerializer.Deserialize<JsonNode>(doc.RootElement.GetRawText());
        }
        catch (JsonException ex)
        {
            throw new SpecParseException($"Document '{sourceName}' is not valid JSON: {ex.Message}", ex);
        }

        if (root is null)
            throw new SpecParseException($"Document '{sourceName}' is empty.");
        CountNodes(sourceName, root, limits);
        return root;
    }

    public static JsonNode ParseYaml(string sourceName, string content, ProcessingLimits limits)
    {
        if (Encoding.UTF8.GetByteCount(content) > limits.MaxDocumentBytes)
            throw new SpecParseException(
                $"Document '{sourceName}' exceeds the maximum supported size of {limits.MaxDocumentBytes / (1024 * 1024)} MB.");

        GuardAgainstYamlAliasBombs(sourceName, content, limits);

        YamlNode yamlRoot;
        try
        {
            // YamlDotNet's representation model preserves anchors/aliases as shared nodes.
            using var reader = new StringReader(content);
            var stream = new YamlStream();
            stream.Load(reader);
            if (stream.Documents.Count == 0)
                throw new SpecParseException($"Document '{sourceName}' contains no YAML document.");
            yamlRoot = stream.Documents[0].RootNode;
        }
        catch (SpecParseException) { throw; }
        catch (Exception ex) when (ex is YamlDotNet.Core.YamlException or ArgumentException)
        {
            throw new SpecParseException($"Document '{sourceName}' is not valid YAML: {ex.Message}", ex);
        }

        long nodeCount = 0;
        var json = ConvertYamlToJsonNode(sourceName, yamlRoot, 0, limits, ref nodeCount);
        if (json is null)
            throw new SpecParseException($"Document '{sourceName}' is empty.");
        return json;
    }

    /// <summary>
    /// Cheap pre-scan that rejects documents with an implausible anchor/alias ratio
    /// (classic YAML "billion laughs" expansion bombs) before parsing.
    /// </summary>
    private static void GuardAgainstYamlAliasBombs(string sourceName, string content, ProcessingLimits limits)
    {
        var anchorCount = CountMatches(content, "\n  &") + CountMatches(content, " &") ;
        anchorCount = Math.Min(anchorCount, CountMatches(content, ": &") + CountMatches(content, "- &"));
        var aliasUses = CountMatches(content, " *");

        if (anchorCount > limits.MaxYamlAnchors)
            throw new SpecParseException(
                $"Document '{sourceName}' declares {anchorCount} YAML anchors which exceeds the safety limit of {limits.MaxYamlAnchors}.");
        if (aliasUses > limits.MaxYamlAliasUses)
            throw new SpecParseException(
                $"Document '{sourceName}' uses {aliasUses} YAML aliases which exceeds the safety limit of {limits.MaxYamlAliasUses}.");
    }

    private static int CountMatches(string haystack, string needle)
    {
        var count = 0;
        var idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }
        return count;
    }

    private static void CountNodes(string sourceName, JsonNode root, ProcessingLimits limits)
    {
        long count = 0;
        CountCore(root);
        if (count > limits.MaxNodes)
            throw new SpecParseException(
                $"Document '{sourceName}' contains {count} nodes which exceeds the safety limit of {limits.MaxNodes}.");

        void CountCore(JsonNode n)
        {
            count++;
            switch (n)
            {
                case JsonObject o:
                    foreach (var p in o) CountCore(p.Value!);
                    break;
                case JsonArray a:
                    foreach (var i in a) CountCore(i!);
                    break;
            }
        }
    }

    private static JsonNode? ConvertYamlToJsonNode(
        string sourceName, YamlNode node, int depth, ProcessingLimits limits, ref long nodeCount)
    {
        if (depth > limits.MaxDepth)
            throw new SpecParseException(
                $"Document '{sourceName}' exceeds the maximum nesting depth of {limits.MaxDepth}.");
        if (++nodeCount > limits.MaxNodes)
            throw new SpecParseException(
                $"Document '{sourceName}' exceeds the maximum node count of {limits.MaxNodes}.");

        switch (node)
        {
            case YamlMappingNode map:
            {
                var obj = new JsonObject();
                foreach (var (key, value) in map)
                {
                    var keyStr = key switch
                    {
                        YamlScalarNode s => s.Value ?? "",
                        _ => throw new SpecParseException(
                            $"Document '{sourceName}' contains a non-scalar mapping key which is not valid in OpenAPI."),
                    };
                    var child = ConvertYamlToJsonNode(sourceName, value, depth + 1, limits, ref nodeCount);
                    if (child is not null) obj[keyStr] = child;
                }
                return obj;
            }
            case YamlSequenceNode seq:
            {
                var arr = new JsonArray();
                foreach (var item in seq)
                {
                    var child = ConvertYamlToJsonNode(sourceName, item, depth + 1, limits, ref nodeCount);
                    if (child is not null) arr.Add(child);
                }
                return arr;
            }
            case YamlScalarNode scalar:
                return ScalarToJson(scalar);
            default:
                return null;
        }
    }

    private static JsonNode? ScalarToJson(YamlScalarNode scalar)
    {
        if (scalar.Value is null) return null;

        // Respect explicit quoting/typing when present.
        if (scalar.Style == YamlDotNet.Core.ScalarStyle.SingleQuoted ||
            scalar.Style == YamlDotNet.Core.ScalarStyle.DoubleQuoted)
            return JsonValue.Create(scalar.Value);

        var v = scalar.Value;
        return v switch
        {
            "" or "~" or "null" or "Null" or "NULL" => null,
            "true" or "True" or "TRUE" => (JsonNode)JsonValue.Create(true),
            "false" or "False" or "FALSE" => JsonValue.Create(false),
            _ when long.TryParse(v, out var l) && v.Trim().Length <= 19 => JsonValue.Create(l),
            _ when double.TryParse(v, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var d)
                && LooksNumeric(v) => JsonValue.Create(d),
            _ => JsonValue.Create(v),
        };
    }

    private static bool LooksNumeric(string v)
    {
        // Avoid treating strings like "1e5x" or "+inf" oddly; require digit start and numeric chars.
        if (v.Length == 0) return false;
        if (!char.IsDigit(v[0]) && v[0] != '-' && v[0] != '+' && v[0] != '.') return false;
        foreach (var c in v)
            if (!char.IsAsciiDigit(c) && c != '.' && c != 'e' && c != 'E' && c != '+' && c != '-')
                return false;
        return true;
    }
}
