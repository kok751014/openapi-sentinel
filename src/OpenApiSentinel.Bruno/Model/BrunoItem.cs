using YamlDotNet.RepresentationModel;

namespace OpenApiSentinel.Bruno.Model;

/// <summary>
/// Typed façade over one OpenCollection item mapping (a folder or an HTTP request).
/// All reads/writes go through the wrapped <see cref="YamlMappingNode"/> so keys the
/// tool does not model are preserved verbatim across a sync.
/// </summary>
public sealed class BrunoItem
{
    public const string SentinelKey = "x-sentinel";

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

    /// <summary>The sentinel-managed metadata block (top-level <c>x-sentinel:</c>), created on demand.</summary>
    public YamlMappingNode Sentinel
    {
        get
        {
            if (BrunoYaml.MappingOf(Node, SentinelKey) is { } existing)
            {
                return existing;
            }

            // A non-mapping x-sentinel value cannot be upgraded in place; replace it.
            if (BrunoYaml.HasKey(Node, SentinelKey))
            {
                BrunoYaml.RemoveKey(Node, SentinelKey);
            }

            var created = new YamlMappingNode();
            Node.Children.Add(BrunoYaml.Scalar(SentinelKey), created);
            return created;
        }
    }

    /// <summary>The sentinel block when present AND a mapping; null otherwise.</summary>
    public YamlMappingNode? SentinelOrNull => BrunoYaml.MappingOf(Node, SentinelKey);

    /// <summary>True when this item was created/managed by OpenAPI Sentinel at some point.</summary>
    public bool IsSentinelManaged => SentinelOrNull is not null;
}
