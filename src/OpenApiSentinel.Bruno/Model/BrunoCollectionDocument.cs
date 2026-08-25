using YamlDotNet.RepresentationModel;
using OpenApiSentinel.Bruno.Model;

namespace OpenApiSentinel.Bruno.Model;

/// <summary>
/// A Bruno collection in single-file OpenCollection YAML form ("bundled").
/// The full representation tree is retained; sync operates on typed façades so
/// unknown keys, ordering and styles survive round-trips.
/// </summary>
public sealed class BrunoCollectionDocument
{
    public const string FormatMarker = "opencollection";

    private BrunoCollectionDocument(YamlMappingNode root)
    {
        Root = root;
    }

    public YamlMappingNode Root { get; }

    public static BrunoCollectionDocument Wrap(YamlMappingNode root) => new(root);

    /// <summary>Parses and validates an OpenCollection YAML file body.</summary>
    public static BrunoCollectionDocument Parse(string yaml, string sourceName)
    {
        var root = BrunoYaml.ParseDocument(yaml, sourceName);
        var marker = BrunoYaml.AsString(BrunoYaml.Get(root, FormatMarker));
        if (marker is null)
        {
            throw new BrunoFormatException(
                $"'{sourceName}' is not a Bruno OpenCollection YAML file (missing top-level '{FormatMarker}:' marker). " +
                "OpenAPI Sentinel syncs single-file OpenCollection YAML collections. " +
                "Directory-based (.bru) collections are not writable by this version — see docs/bruno-sync.md.");
        }

        return new BrunoCollectionDocument(root);
    }

    /// <summary>Creates a minimal new bundled collection document.</summary>
    public static BrunoCollectionDocument CreateNew(string name)
    {
        var root = new YamlMappingNode();
        root.Children.Add(BrunoYaml.Scalar(FormatMarker), BrunoYaml.Scalar("1.0.0"));
        var info = new YamlMappingNode();
        info.Children.Add(BrunoYaml.Scalar("name"), BrunoYaml.Scalar(name));
        root.Children.Add(BrunoYaml.Scalar("info"), info);
        var generated = new YamlMappingNode();
        generated.Children.Add(BrunoYaml.Scalar("generator"), BrunoYaml.Scalar("openapi-sentinel"));
        var extensions = new YamlMappingNode();
        extensions.Children.Add(BrunoYaml.Scalar("x-sentinel"), generated);
        root.Children.Add(BrunoYaml.Scalar("extensions"), extensions);
        return new BrunoCollectionDocument(root);
    }

    public string? CollectionName
    {
        get
        {
            if (BrunoYaml.MappingOf(Root, "info") is not { } info)
            {
                return null;
            }

            return BrunoYaml.AsString(BrunoYaml.Get(info, "name"));
        }
    }

    /// <summary>Top-level items sequence; created on demand.</summary>
    public YamlSequenceNode ItemsSequence()
    {
        if (BrunoYaml.SequenceOf(Root, "items") is { } existing)
        {
            return existing;
        }

        if (BrunoYaml.HasKey(Root, "items"))
        {
            BrunoYaml.RemoveKey(Root, "items");
        }

        var created = new YamlSequenceNode();
        // Keep items after info/extensions but before trailing blocks: append at end is fine
        // because OpenCollection files are mappings whose key order carries no semantics.
        Root.Children.Add(BrunoYaml.Scalar("items"), created);
        return created;
    }

    public IEnumerable<BrunoItem> RootItems =>
        BrunoYaml.SequenceOf(Root, "items") is { } seq
            ? BrunoYaml.MappingItems(seq).Select(node => new BrunoItem(node))
            : [];

    /// <summary>Every request item in the collection, depth-first in stored order.</summary>
    public IEnumerable<BrunoItem> AllRequests()
    {
        foreach (var item in Enumerate(RootItems))
        {
            yield return item;
        }
    }

    private static IEnumerable<BrunoItem> Enumerate(IEnumerable<BrunoItem> items)
    {
        foreach (var item in items)
        {
            if (item.IsFolder)
            {
                yield return item;
                foreach (var nested in Enumerate(item.Children))
                {
                    yield return nested;
                }
            }
            else
            {
                yield return item;
            }
        }
    }

    /// <summary>Returns the folder item matching <paramref name="segments"/> (e.g. "訂單" + "查詢"), creating folders as needed.</summary>
    public BrunoItem GetOrCreateFolderPath(IReadOnlyList<string> segments)
    {
        var seq = ItemsSequence();
        YamlMappingNode parentItem = null!;
        BrunoItem? folder = null;
        var currentSeq = seq;

        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            folder = null;
            foreach (var candidate in BrunoYaml.MappingItems(currentSeq))
            {
                var item = new BrunoItem(candidate);
                if (item.IsFolder && string.Equals(item.Name, segment, StringComparison.Ordinal))
                {
                    folder = item;
                    break;
                }
            }

            if (folder is null)
            {
                var map = new YamlMappingNode();
                var info = new YamlMappingNode();
                info.Children.Add(BrunoYaml.Scalar("name"), BrunoYaml.Scalar(segment));
                info.Children.Add(BrunoYaml.Scalar("type"), BrunoYaml.Scalar("folder"));
                map.Children.Add(BrunoYaml.Scalar("info"), info);
                currentSeq.Children.Add(map);
                folder = new BrunoItem(map);
            }

            parentItem = folder.Node;
            currentSeq = BrunoYaml.GetOrCreateSequence(parentItem, "items");
        }

        _ = parentItem;
        return folder!;
    }

    /// <summary>Serializes the document back to YAML.</summary>
    public string ToYaml() => BrunoYaml.ToYaml(Root);
}
