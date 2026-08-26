using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace OpenApiSentinel.Bruno;

/// <summary>
/// Low-level YAML plumbing for Bruno OpenCollection files, built on YamlDotNet's
/// representation model so unknown keys, comments-adjacent ordering and scalar styles
/// survive a load → mutate → save cycle (fidelity-preserving round-trip).
/// </summary>
public static class BrunoYaml
{
    /// <summary>Safety cap for collection files (they are local, user-owned artifacts).</summary>
    public const int MaxCollectionBytes = 20 * 1024 * 1024;

    public static YamlMappingNode ParseDocument(string yaml, string sourceName)
    {
        if (yaml.Length > MaxCollectionBytes)
        {
            throw new BrunoFormatException(
                $"Bruno collection '{sourceName}' exceeds the {MaxCollectionBytes / (1024 * 1024)} MB limit.");
        }

        var stream = new YamlStream();
        try
        {
            using var reader = new StringReader(yaml);
            stream.Load(reader);
        }
        catch (Exception ex) when (ex is YamlException or ArgumentException)
        {
            throw new BrunoFormatException($"Bruno collection '{sourceName}' is not valid YAML: {ex.Message}", ex);
        }

        if (stream.Documents.Count == 0)
        {
            throw new BrunoFormatException($"Bruno collection '{sourceName}' is empty.");
        }

        if (stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            throw new BrunoFormatException(
                $"Bruno collection '{sourceName}' must be a YAML mapping at the document root.");
        }

        return root;
    }

    public static string ToYaml(YamlMappingNode root)
    {
        // We deliberately do NOT use YamlStream.Save: its emitter anchors any repeated
        // scalar VALUE (nodes are compared by value) producing random &id noise and
        // nondeterministic output. Walking events directly gives stable, anchor-free YAML.
        using var writer = new StringWriter();
        var emitter = new Emitter(writer, bestIndent: 2, bestWidth: int.MaxValue);
        emitter.Emit(new StreamStart());
        emitter.Emit(new DocumentStart());
        EmitNode(emitter, root);
        emitter.Emit(new DocumentEnd(true));
        emitter.Emit(new StreamEnd());
        return writer.ToString();
    }

    private static void EmitNode(IEmitter emitter, YamlNode node)
    {
        switch (node)
        {
            case YamlScalarNode scalar:
                EmitScalar(emitter, scalar);
                break;
            case YamlSequenceNode sequence:
                emitter.Emit(new SequenceStart(AnchorName.Empty, TagName.Empty, true, sequence.Style));
                foreach (var child in sequence.Children)
                {
                    EmitNode(emitter, child);
                }

                emitter.Emit(new SequenceEnd());
                break;
            case YamlMappingNode map:
                emitter.Emit(new MappingStart(AnchorName.Empty, TagName.Empty, true, map.Style));
                foreach (var entry in map.Children)
                {
                    EmitNode(emitter, entry.Key);
                    EmitNode(emitter, entry.Value);
                }

                emitter.Emit(new MappingEnd());
                break;
            default:
                throw new BrunoFormatException($"Unsupported YAML node type '{node.GetType().Name}'.");
        }
    }

    private static void EmitScalar(IEmitter emitter, YamlScalarNode scalar)
    {
        var value = scalar.Value ?? "null";

        // Loaded nodes carry the ORIGINAL presentation style (plain→Plain, quoted→*Quoted),
        // so honoring any explicitly-set style reproduces the author's intent exactly.
        // Style.Any means "fresh node created in code": pick a safe presentation.
        // Fresh STRING scalars whose text would be re-read as a YAML number/bool/null
        // (e.g. param value "1") are quoted so the emitted YAML keeps the string type —
        // required by the official OpenCollection schema for name/value fields.
        var style = scalar.Style switch
        {
            ScalarStyle.Plain when !NeedsQuoting(value) && !value.Contains('\n', StringComparison.Ordinal) =>
                ScalarStyle.Plain,
            ScalarStyle.Plain when value.Contains('\n', StringComparison.Ordinal) => ScalarStyle.Literal,
            ScalarStyle.Any when PlainScalarResolvesToNonString(value) => ScalarStyle.DoubleQuoted,
            ScalarStyle.Any => NeedsQuoting(value)
                ? ScalarStyle.DoubleQuoted
                : value.Contains('\n', StringComparison.Ordinal)
                    ? ScalarStyle.Literal
                    : ScalarStyle.Plain,
            _ => scalar.Style,
        };

        emitter.Emit(new Scalar(AnchorName.Empty, TagName.Empty, value, style, true, false));
    }

    /// <summary>
    /// True when a PLAIN scalar with this text would be parsed back as null/bool/int/float
    /// rather than a string under the YAML 1.2 core schema.
    /// </summary>
    public static bool PlainScalarResolvesToNonString(string value) => value switch
    {
        "" or "~" or "null" or "Null" or "NULL" => true,
        "true" or "True" or "TRUE" or "false" or "False" or "FALSE" => true,
        _ when int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _) => true,
        _ when double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _) &&
               LooksNumeric(value) => true,
        _ => false,
    };

    private static bool LooksNumeric(string value) =>
        value.Contains('0') || value.Contains('1') || value.Contains('2') || value.Contains('3') ||
        value.Contains('4') || value.Contains('5') || value.Contains('6') || value.Contains('7') ||
        value.Contains('8') || value.Contains('9');

    private static readonly HashSet<char> IndicatorChars = ['#', '&', '*', '!', '|', '>', '\'', '"', '%', '@', '`'];

    internal static bool NeedsQuoting(string value)
    {
        if (value.Length == 0)
        {
            return true;
        }

        switch (value[0])
        {
            case '-' or '?' or ':':
                return value.Length == 1 || value[1] == ' ';
            case ',' or '[' or ']' or '{' or '}':
                return true;
            default:
                break;
        }

        if (IndicatorChars.Contains(value[0]))
        {
            return true;
        }

        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (ch != '\n' && char.IsControl(ch))
            {
                return true;
            }
        }

        if (value.Contains(": ", StringComparison.Ordinal) || value.Contains(" #", StringComparison.Ordinal))
        {
            return true;
        }

        return char.IsWhiteSpace(value[^1]) || char.IsWhiteSpace(value[0]) && value[0] != '\n';
    }

    /// <summary>Copies a representation-model subtree so it can live in another document.</summary>
    public static YamlNode DeepClone(YamlNode node) => node switch
    {
        YamlScalarNode s => new YamlScalarNode(s.Value) { Style = s.Style, Tag = s.Tag },
        YamlSequenceNode seq => CloneSequence(seq),
        YamlMappingNode map => CloneMapping(map),
        _ => throw new BrunoFormatException($"Unsupported YAML node type '{node.GetType().Name}'."),
    };

    private static YamlSequenceNode CloneSequence(YamlSequenceNode seq)
    {
        var clone = new YamlSequenceNode { Style = seq.Style };
        foreach (var child in seq.Children)
        {
            clone.Children.Add(DeepClone(child));
        }

        return clone;
    }

    private static YamlMappingNode CloneMapping(YamlMappingNode map)
    {
        var clone = new YamlMappingNode { Style = map.Style };
        foreach (var entry in map.Children)
        {
            clone.Children.Add(DeepClone(entry.Key), DeepClone(entry.Value));
        }

        return clone;
    }

    public static YamlScalarNode Scalar(string value) => new(value);

    public static YamlScalarNode Scalar(bool value) => new(value ? "true" : "false") { Style = ScalarStyle.Plain };

    public static YamlScalarNode Scalar(int value) => new(
        value.ToString(System.Globalization.CultureInfo.InvariantCulture)) { Style = ScalarStyle.Plain };

    public static string? AsString(YamlNode? node) =>
        node is YamlScalarNode { Value: not null } scalar ? scalar.Value : null;

    public static bool? AsBool(YamlNode? node) => AsString(node)?.Trim().ToLowerInvariant() switch
    {
        "true" or "yes" or "on" => true,
        "false" or "no" or "off" => false,
        _ => null,
    };

    public static int? AsInt(YamlNode? node) =>
        AsString(node) is { } text &&
        int.TryParse(text.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n)
            ? n
            : null;

    public static YamlNode? Get(YamlMappingNode map, string key)
    {
        foreach (var entry in map.Children)
        {
            if (entry.Key is YamlScalarNode { Value: not null } k && k.Value == key)
            {
                return entry.Value;
            }
        }

        return null;
    }

    public static bool HasKey(YamlMappingNode map, string key) => Get(map, key) is not null;

    /// <summary>Returns the mapping under <paramref name="key"/>, creating and appending it when absent.</summary>
    public static YamlMappingNode GetOrCreateMapping(YamlMappingNode map, string key)
    {
        if (Get(map, key) is YamlMappingNode existing)
        {
            return existing;
        }

        RemoveKey(map, key);
        var created = new YamlMappingNode();
        map.Children.Add(Scalar(key), created);
        return created;
    }

    /// <summary>Returns the sequence under <paramref name="key"/>, creating and appending it when absent.</summary>
    public static YamlSequenceNode GetOrCreateSequence(YamlMappingNode map, string key)
    {
        if (Get(map, key) is YamlSequenceNode existing)
        {
            return existing;
        }

        RemoveKey(map, key);
        var created = new YamlSequenceNode();
        map.Children.Add(Scalar(key), created);
        return created;
    }

    /// <summary>Replaces the value of <paramref name="key"/> while keeping its original position.</summary>
    public static void Set(YamlMappingNode map, string key, YamlNode value)
    {
        var keyNode = new YamlScalarNode(key);
        var replacementIndex = -1;
        var i = 0;
        foreach (var entry in map.Children)
        {
            if (entry.Key is YamlScalarNode { Value: not null } k && k.Value == key)
            {
                replacementIndex = i;
                break;
            }

            i++;
        }

        if (replacementIndex >= 0)
        {
            var children = map.Children.ToList();
            map.Children.Clear();
            for (var j = 0; j < children.Count; j++)
            {
                if (j == replacementIndex)
                {
                    map.Children.Add(children[j].Key, value);
                }
                else
                {
                    map.Children.Add(children[j].Key, children[j].Value);
                }
            }

            return;
        }

        map.Children.Add(keyNode, value);
    }

    public static void SetString(YamlMappingNode map, string key, string? value)
    {
        if (value is null)
        {
            RemoveKey(map, key);
            return;
        }

        Set(map, key, Scalar(value));
    }

    public static void SetBool(YamlMappingNode map, string key, bool value) => Set(map, key, Scalar(value));

    public static void SetInt(YamlMappingNode map, string key, int value) => Set(map, key, Scalar(value));

    public static bool RemoveKey(YamlMappingNode map, string key)
    {
        YamlNode? foundKey = null;
        YamlNode? foundValue = null;
        foreach (var entry in map.Children)
        {
            if (entry.Key is YamlScalarNode { Value: not null } k && k.Value == key)
            {
                foundKey = entry.Key;
                foundValue = entry.Value;
                break;
            }
        }

        if (foundKey is null)
        {
            return false;
        }

        map.Children.Remove(foundKey);
        _ = foundValue;
        return true;
    }

    public static YamlSequenceNode? SequenceOf(YamlMappingNode map, string key) => Get(map, key) as YamlSequenceNode;

    public static YamlMappingNode? MappingOf(YamlMappingNode map, string key) => Get(map, key) as YamlMappingNode;

    /// <summary>Appends an entry preserving insertion order; sequences in OpenCollection are ordered lists.</summary>
    public static void Append(YamlSequenceNode seq, YamlNode node) => seq.Children.Add(node);

    public static IEnumerable<YamlMappingNode> MappingItems(YamlSequenceNode seq)
    {
        foreach (var child in seq.Children)
        {
            if (child is YamlMappingNode map)
            {
                yield return map;
            }
        }
    }
}
