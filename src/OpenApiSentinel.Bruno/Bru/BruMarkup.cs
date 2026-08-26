using System.Text;
using OpenApiSentinel;

namespace OpenApiSentinel.Bruno;

public enum BruBlockKind
{
    Dictionary,
    Text,
    Raw,
}

public sealed record BruPair(string RawKey, bool Enabled, string Value);

/// <summary>One top-level block of a .bru request/folder file.</summary>
public sealed class BruBlock
{
    public required string Name { get; init; }

    public required BruBlockKind Kind { get; init; }

    /// <summary>Dictionary pairs in file order.</summary>
    public List<BruPair> Pairs { get; init; } = [];

    /// <summary>Text-block payload (outdented, without trailing newline).</summary>
    public string Text { get; init; } = "";

    /// <summary>Original source lines of a verbatim-preserved block (examples, vars, app…),
    /// including its header and closing brace.</summary>
    public IReadOnlyList<string> RawLines { get; init; } = [];

    /// <summary>Values of an inline array field inside a dictionary block (meta.tags).</summary>
    public List<string> TagsList { get; init; } = [];
}

/// <summary>A parsed .bru file: ordered top-level blocks.</summary>
public sealed class BruFile
{
    public List<BruBlock> Blocks { get; init; } = [];

    public BruBlock? First(string name) => Blocks.FirstOrDefault(b => b.Name == name);

    public IEnumerable<BruBlock> All(string name) => Blocks.Where(b => b.Name == name);
}

/// <summary>
/// Parser and serializer for Bruno's native .bru markup, mirroring the official grammar
/// (packages/bruno-lang/v2): every block opens with "&lt;name&gt; {" and closes at a bare
/// "}" line; "~" prefixes disable a pair; dictionary values run to end-of-line; text
/// blocks carry their lines verbatim under two-space indentation.
/// </summary>
public static class BruMarkup
{
    private static readonly HashSet<string> DictionaryBlocks = new(StringComparer.Ordinal)
    {
        "meta", "app", "settings", "grpc", "ws", "headers", "metadata",
        "params:path", "params:query", "query", "assert",
        "auth:awsv4", "auth:basic", "auth:bearer", "auth:digest", "auth:ntlm",
        "auth:oauth1", "auth:oauth2", "auth:wsse", "auth:apikey", "auth:akamai-edgegrid",
        "body:form-urlencoded", "body:multipart-form", "body:file", "body:grpc", "body:ws",
    };

    private static readonly HashSet<string> MethodBlocks = new(StringComparer.Ordinal)
    {
        "get", "post", "put", "delete", "patch", "options", "head", "connect", "trace", "http",
    };

    private static readonly HashSet<string> TextBlocks = new(StringComparer.Ordinal)
    {
        "body", "body:json", "body:text", "body:xml", "body:sparql",
        "body:graphql", "body:graphql:vars", "script:pre-request", "script:post-response",
        "tests", "docs",
    };

    private static readonly System.Text.RegularExpressions.Regex HeaderRegex =
        new(@"^([A-Za-z0-9_:.\-]+)[ \t]*\{[ \t]*$",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    public static bool IsBruFileName(string name) => name.EndsWith(".bru", StringComparison.Ordinal);

    /// <summary>Parses .bru content into ordered blocks.</summary>
    public static BruFile Parse(string content, string sourceName)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var blocks = new List<BruBlock>();
        var i = 0;
        while (i < lines.Length && lines[i].Length == 0)
        {
            i++;
        }

        while (i < lines.Length)
        {
            if (lines[i].Length == 0)
            {
                i++;
                continue;
            }

            var header = HeaderRegex.Match(lines[i]);
            if (!header.Success)
            {
                throw new BrunoFormatException(
                    $"'{sourceName}': expected a block header, found '{lines[i].Trim()}' (line {i + 1}).");
            }

            var name = header.Groups[1].Value;
            var headerLine = i;
            i++;

            // Every block — including nested-content example blocks — terminates at a
            // bare "}" line (column 0), exactly as the official grammar and writers emit.
            while (i < lines.Length && lines[i] != "}")
            {
                i++;
            }

            if (i >= lines.Length)
            {
                throw new BrunoFormatException(
                    $"'{sourceName}': block '{name}' (line {headerLine + 1}) is not closed.");
            }

            var bodyStart = headerLine + 1;
            var bodyEnd = i; // exclusive

            if (IsRawBlock(name))
            {
                var raw = new List<string>();
                for (var k = headerLine; k <= bodyEnd; k++)
                {
                    raw.Add(lines[k]);
                }

                blocks.Add(new BruBlock { Name = name, Kind = BruBlockKind.Raw, RawLines = raw });
                i++;
                continue;
            }

            var kind = Classify(name);
            if (kind == BruBlockKind.Dictionary)
            {
                var pairs = new List<BruPair>();
                var blockTags = new List<string>();
                for (var k = bodyStart; k < bodyEnd; k++)
                {
                    var trimmedLine = lines[k].Trim();
                    if (trimmedLine.Length == 0)
                    {
                        continue;
                    }

                    // Inline array field ("tags: [") — items run one per line until "]".
                    if (trimmedLine.EndsWith('['))
                    {
                        k++;
                        while (k < bodyEnd && lines[k].Trim() != "]")
                        {
                            if (lines[k].Trim().Length > 0)
                            {
                                blockTags.Add(lines[k].Trim());
                            }

                            k++;
                        }

                        continue;
                    }

                    var entry = ParsePair(lines[k], k + 1, sourceName);
                    if (entry is not null)
                    {
                        pairs.Add(entry);
                    }
                }

                blocks.Add(new BruBlock
                {
                    Name = name,
                    Kind = BruBlockKind.Dictionary,
                    Pairs = pairs,
                    TagsList = blockTags,
                });
            }
            else
            {
                var bodyLines = lines[bodyStart..bodyEnd];
                blocks.Add(new BruBlock
                {
                    Name = name,
                    Kind = BruBlockKind.Text,
                    Text = Outdent(bodyLines),
                });
            }

            i++;
        }

        return new BruFile { Blocks = blocks };
    }

    private static bool IsRawBlock(string name) =>
        name == "example" || name.StartsWith("vars:", StringComparison.Ordinal) ||
        !(DictionaryBlocks.Contains(name) || MethodBlocks.Contains(name) || TextBlocks.Contains(name));

    private static BruBlockKind Classify(string name)
    {
        if (DictionaryBlocks.Contains(name) || MethodBlocks.Contains(name))
        {
            return BruBlockKind.Dictionary;
        }

        return BruBlockKind.Text;
    }

    internal static BruBlockKind ClassifyForTest(string name) => Classify(name);

    private static BruPair? ParsePair(string line, int lineNumber, string sourceName)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        var colon = IndexOfValueColon(trimmed);
        if (colon <= 0)
        {
            throw new BrunoFormatException(
                $"'{sourceName}': cannot parse dictionary line '{trimmed}' (line {lineNumber}).");
        }

        var key = trimmed[..colon].Trim();
        var enabled = true;
        if (key.StartsWith('~'))
        {
            enabled = false;
            key = key[1..];
        }

        if (key.Length >= 2 && key[0] == '"' && key[^1] == '"')
        {
            key = key.Length > 2 ? key[1..^1] : "";
        }

        var value = colon + 1 < trimmed.Length ? trimmed[(colon + 1)..].Trim() : "";
        return new BruPair(key, enabled, value);
    }

    /// <summary>Finds the key/value separator: the first ":" that is neither part of a URL
    /// scheme ("http://" / "https://" / …) nor inside a quoted key.</summary>
    internal static int IndexOfValueColon(string line)
    {
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            switch (line[i])
            {
                case '"':
                    inQuotes = !inQuotes;
                    break;
                case ':' when !inQuotes:
                    // A scheme separator is a colon directly followed by "//".
                    if (!(i + 2 < line.Length && line[i + 1] == '/' && line[i + 2] == '/'))
                    {
                        return i;
                    }

                    break;
            }
        }

        return -1;
    }

    /// <summary>Removes two leading indentation levels (the official writers indent by 2).</summary>
    private static string Outdent(IReadOnlyList<string> bodyLines)
    {
        var result = new StringBuilder();
        foreach (var line in bodyLines)
        {
            if (line.StartsWith("  ", StringComparison.Ordinal))
            {
                result.Append(line[2..]);
            }
            else if (line.Length > 0)
            {
                result.Append(line.Trim());
            }

            result.Append('\n');
        }

        return result.ToString().TrimEnd('\n');
    }

    /// <summary>Serializes one dictionary/text/raw block exactly like the official writers
    /// (two-space indent, blank line after each block).</summary>
    public static string Serialize(BruBlock block)
    {
        var sb = new StringBuilder();
        if (block.Kind == BruBlockKind.Raw)
        {
            foreach (var line in block.RawLines)
            {
                sb.Append(line).Append('\n');
            }

            sb.Append('\n');
            return sb.ToString();
        }

        sb.Append(block.Name).Append(" {\n");
        if (block.Kind == BruBlockKind.Text)
        {
            foreach (var line in block.Text.Replace("\r\n", "\n").Split('\n'))
            {
                sb.Append("  ").Append(line).Append('\n');
            }
        }
        else
        {
            foreach (var pair in block.Pairs)
            {
                sb.Append("  ");
                if (!pair.Enabled)
                {
                    sb.Append('~');
                }

                sb.Append(EmitKey(pair.RawKey)).Append(": ").Append(pair.Value).Append('\n');
            }
        }

        sb.Append("}\n\n");
        return sb.ToString();
    }

    public static string Serialize(IEnumerable<BruBlock> blocks)
    {
        var combined = new StringBuilder();
        foreach (var block in blocks)
        {
            combined.Append(Serialize(block));
        }

        return combined.ToString().TrimEnd('\n') + "\n";
    }

    private static string EmitKey(string key)
    {
        var needsQuote = key.Contains(':') || key.Contains('"') ||
                         key.Contains('{') || key.Contains('}');
        return needsQuote ? "\"" + key.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"" : key;
    }
}
