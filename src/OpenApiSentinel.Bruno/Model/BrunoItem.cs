using YamlDotNet.RepresentationModel;

namespace OpenApiSentinel.Bruno.Model;

/// <summary>
/// Typed façade over one OpenCollection item mapping (a folder or an HTTP request).
/// All reads/writes go through the wrapped <see cref="YamlMappingNode"/> so keys the
/// tool does not model are preserved verbatim across a sync.
/// </summary>
public sealed class BrunoItem
{
    /// <summary>
    /// Prefix of the single Sentinel binding tag stored in the schema-legal
    /// <c>info.tags</c> list. The tag points at the request's metadata entry inside the
    /// collection-level <c>extensions.x-sentinel.operations</c> index; request items NEVER
    /// carry a Sentinel metadata block (official HttpRequest schema: additionalProperties:false).
    /// </summary>
    public const string SentinelTagPrefix = "x-sentinel:";

    public BrunoItem(YamlMappingNode node)
    {
        Node = node;
    }

    public YamlMappingNode Node { get; }

    private YamlMappingNode? InfoOrNull => BrunoYaml.MappingOf(Node, "info");

    public bool IsFolder =>
        string.Equals(BrunoYaml.AsString(BrunoYaml.Get(InfoOrNull ?? new YamlMappingNode(), "type")), "folder",
            StringComparison.Ordinal);

    public string? Name
    {
        get
        {
            var info = InfoOrNull;
            return info is null ? null : BrunoYaml.AsString(BrunoYaml.Get(info, "name"));
        }
    }

    public int? Seq
    {
        get
        {
            var info = InfoOrNull;
            return info is null ? null : BrunoYaml.AsInt(BrunoYaml.Get(info, "seq"));
        }
    }

    /// <summary>The <c>http:</c> details mapping; null for folders and non-HTTP items.</summary>
    public YamlMappingNode? Http => BrunoYaml.MappingOf(Node, "http");

    public bool IsHttpRequest => Http is not null;

    /// <summary>Nested items of a folder; empty for requests.</summary>
    public IEnumerable<BrunoItem> Children
    {
        get
        {
            if (BrunoYaml.SequenceOf(Node, "items") is not { } seq)
            {
                yield break;
            }

            foreach (var child in BrunoYaml.MappingItems(seq))
            {
                yield return new BrunoItem(child);
            }
        }
    }

    /// <summary>The stable identity key recorded by this item's binding tag; null when unbound.</summary>
    public string? SentinelTagKey
    {
        get
        {
            if (BrunoYaml.SequenceOf(InfoOrNull ?? new YamlMappingNode(), "tags") is not { } tags)
            {
                return null;
            }

            foreach (var tag in tags.Children)
            {
                if (tag is YamlScalarNode { Value: not null } scalar &&
                    scalar.Value.StartsWith(SentinelTagPrefix, StringComparison.Ordinal))
                {
                    return scalar.Value[SentinelTagPrefix.Length..];
                }
            }

            return null;
        }
    }

    /// <summary>Binds this item to <paramref name="stableKey"/> via its info tag, replacing any previous binding. Other tags are preserved.</summary>
    public void BindSentinelTag(string stableKey)
    {
        var info = BrunoYaml.GetOrCreateMapping(Node, "info");
        var tags = BrunoYaml.GetOrCreateSequence(info, "tags");
        var replacement = new YamlScalarNode(SentinelTagPrefix + stableKey);
        var replaced = false;
        for (var i = 0; i < tags.Children.Count; i++)
        {
            if (tags.Children[i] is YamlScalarNode { Value: not null } scalar &&
                scalar.Value.StartsWith(SentinelTagPrefix, StringComparison.Ordinal))
            {
                tags.Children[i] = replacement;
                replaced = true;
                break;
            }
        }

        if (!replaced)
        {
            tags.Children.Add(replacement);
        }
    }

    public void UnbindSentinelTag()
    {
        if (BrunoYaml.SequenceOf(InfoOrNull ?? new YamlMappingNode(), "tags") is not { } tags)
        {
            return;
        }

        for (var i = tags.Children.Count - 1; i >= 0; i--)
        {
            if (tags.Children[i] is YamlScalarNode { Value: not null } scalar &&
                scalar.Value.StartsWith(SentinelTagPrefix, StringComparison.Ordinal))
            {
                tags.Children.RemoveAt(i);
            }
        }
    }

    /// <summary>True when this item carries a Sentinel binding tag.</summary>
    public bool IsSentinelManaged => SentinelTagKey is not null;
}
