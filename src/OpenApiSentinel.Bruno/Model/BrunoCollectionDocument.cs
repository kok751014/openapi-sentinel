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

        ValidateItems(root, sourceName, "$");
        return new BrunoCollectionDocument(root);
    }

    /// <summary>
    /// Structural validation of the parts sync will touch: where OpenCollection expects a
    /// LIST (items, params, headers, examples) a non-list value would otherwise be
    /// silently REPLACED during a merge — a §6 corruption vector. Fail clearly instead.
    /// Unknown keys are still allowed everywhere; this only guards known list slots.
    /// </summary>
    private static void ValidateItems(YamlMappingNode container, string sourceName, string path)
    {
        if (BrunoYaml.Get(container, "items") is { } items && items is not YamlSequenceNode)
        {
            throw new BrunoFormatException(
                $"'{sourceName}': '{path}.items' must be a list (found {items.GetType().Name}). " +
                "Refusing to sync: a merge would silently discard the malformed value.");
        }

        foreach (var rawItem in BrunoYaml.SequenceOf(container, "items")?.Children ?? [])
        {
            if (rawItem is not YamlMappingNode item)
            {
                continue;
            }

            var name = BrunoYaml.AsString(BrunoYaml.Get(
                BrunoYaml.MappingOf(item, "info") ?? new YamlMappingNode(), "name")) ?? "(unnamed)";
            var itemPath = $"{path}.items[{name}]";

            foreach (var key in (ReadOnlySpan<string>)["params", "headers", "examples"])
            {
                if (BrunoYaml.Get(item, key) is { } value && value is not YamlSequenceNode)
                {
                    throw new BrunoFormatException(
                        $"'{sourceName}': request '{itemPath}.{key}' must be a list (found {value.GetType().Name}). " +
                        "Refusing to sync: a merge would silently discard the malformed value.");
                }
            }

            // Recurse into folders.
            if (BrunoYaml.SequenceOf(item, "items") is { } children)
            {
                ValidateItems(WrapAsContainer(children), sourceName, itemPath);
            }
        }
    }

    private static YamlMappingNode WrapAsContainer(YamlSequenceNode children)
    {
        // A lightweight pseudo-container so ValidateItems can read .items uniformly.
        var wrapper = new YamlMappingNode();
        wrapper.Children.Add(BrunoYaml.Scalar("items"), children);
        return wrapper;
    }

    /// <summary>Creates a minimal new bundled collection document.</summary>
    public static BrunoCollectionDocument CreateNew(string name)
    {
        var root = new YamlMappingNode();
        root.Children.Add(BrunoYaml.Scalar(FormatMarker), BrunoYaml.Scalar("1.0.0"));
        root.Children.Add(BrunoYaml.Scalar("bundled"), BrunoYaml.Scalar(true));
        var info = new YamlMappingNode();
        info.Children.Add(BrunoYaml.Scalar("name"), BrunoYaml.Scalar(name));
        root.Children.Add(BrunoYaml.Scalar("info"), info);
        // Sentinel metadata lives in the schema-legal extensions block, never on request items.
        root.Children.Add(BrunoYaml.Scalar("extensions"), SentinelExtensionsBlock());
        return new BrunoCollectionDocument(root);
    }

    private static YamlMappingNode SentinelExtensionsBlock()
    {
        var sentinel = new YamlMappingNode();
        sentinel.Children.Add(BrunoYaml.Scalar("version"), BrunoYaml.Scalar(1));
        sentinel.Children.Add(BrunoYaml.Scalar("operations"), new YamlMappingNode());
        var generated = new YamlMappingNode();
        generated.Children.Add(BrunoYaml.Scalar("generator"), BrunoYaml.Scalar("openapi-sentinel"));
        sentinel.Children.Add(BrunoYaml.Scalar("generator"), generated);
        var extensions = new YamlMappingNode();
        extensions.Children.Add(BrunoYaml.Scalar("x-sentinel"), sentinel);
        return extensions;
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
