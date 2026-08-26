using System.Text;
using System.Text.Json;
using OpenApiSentinel.Bruno.Model;
using YamlDotNet.RepresentationModel;

namespace OpenApiSentinel.Bruno;

/// <summary>
/// The Bruno-native filesystem collection format ("Open Collection"): a directory whose
/// root carries the <c>bruno.json</c> manifest, folders are plain directories (optionally
/// with a <c>folder.bru</c>), and each HTTP request lives in one <c>.bru</c> file. This is
/// the format Bruno Desktop opens DIRECTLY — no Import-Collection conversion needed.
/// </summary>
public static class BruWorkspaceNames
{
    public const string ManifestFileName = "bruno.json";
    public const string FolderFileName = "folder.bru";

    /// <summary>Directories that never contain collection items.</summary>
    public static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    { "environments", "node_modules", ".git", ".bruno", ".svn", ".hg" };

    /// <summary>.bru block names without a canonical OpenCollection field mapping; their
    /// source lines are preserved verbatim across syncs via the raw-preservation registry.</summary>
    private static readonly HashSet<string> RawPreservedBlocks = new(StringComparer.Ordinal)
    {
        "example", "app", "grpc", "ws", "body:file", "body:graphql", "body:graphql:vars",
        "auth:oauth1", "auth:oauth2", "auth:akamai-edgegrid",
    };

    public static bool IsRawPreserved(string blockName) =>
        RawPreservedBlocks.Contains(blockName) ||
        blockName.StartsWith("vars:", StringComparison.Ordinal);
}

/// <summary>In-memory store of .bru constructs that have no canonical field mapping,
/// keyed by stable identity key so they re-attach to the right request after a sync.</summary>
public sealed class BruRawRegistry
{
    private readonly Dictionary<string, List<BruBlock>> byKey = new(StringComparer.Ordinal);

    public void Add(string stableKey, BruBlock block)
    {
        if (!byKey.TryGetValue(stableKey, out var list))
        {
            list = [];
            byKey[stableKey] = list;
        }

        list.Add(block);
    }

    public IReadOnlyList<BruBlock>? For(string stableKey) =>
        stableKey is not null && byKey.TryGetValue(stableKey, out var list) ? list : null;
}

public sealed class BruWorkspaceReadResult
{
    public required BrunoCollectionDocument Document { get; init; }
    public required BruRawRegistry RawRegistry { get; init; }
}

public static class BruWorkspaceReader
{
    public static bool IsBruWorkspace(string directory) =>
        Directory.Exists(directory) && File.Exists(Path.Combine(directory, BruWorkspaceNames.ManifestFileName));

    public static BruWorkspaceReadResult Read(string directory, string manifestJson)
    {
        var manifest = BruManifestSerializer.Parse(manifestJson, Path.Combine(directory, BruWorkspaceNames.ManifestFileName));
        var collection = BrunoCollectionDocument.CreateNew(
            string.IsNullOrWhiteSpace(manifest.Name) ? new DirectoryInfo(directory).Name : manifest.Name);

        var registry = new BruRawRegistry();
        var store = SentinelMetaStore.For(collection);
        ReadDirectory(directory, collection.ItemsSequence(), registry, store);

        // Assign seq numbers to any request lacking one, in deterministic walk order.
        var nextSeq = NextSeq(collection);
        foreach (var item in collection.AllRequests())
        {
            if (!item.Seq.HasValue && !item.IsFolder)
            {
                var view = new Model.BrunoRequestView(item.Node);
                view.SetSeq(nextSeq++);
            }
        }

        return new BruWorkspaceReadResult { Document = collection, RawRegistry = registry };
    }

    private static int NextSeq(BrunoCollectionDocument collection)
    {
        var max = 0;
        foreach (var item in collection.AllRequests())
        {
            if (item.Seq is { } seq && seq > max)
            {
                max = seq;
            }
        }

        return max + 1;
    }

    private static void ReadDirectory(string directory, YamlSequenceNode targetItems, BruRawRegistry registry, SentinelMetaStore store)
    {
        foreach (var dir in Directory.GetDirectories(directory).Order(StringComparer.Ordinal))
        {
            var dirName = Path.GetFileName(dir);
            if (BruWorkspaceNames.SkippedDirectories.Contains(dirName))
            {
                continue;
            }

            // A directory counts as an item folder when it holds requests or a folder.bru.
            var folderFilePath = Path.Combine(dir, BruWorkspaceNames.FolderFileName);
            var hasFolderMeta = File.Exists(folderFilePath);
            var childBru = Directory.GetFiles(dir, "*.bru").Order(StringComparer.Ordinal).ToArray();
            var childDirs = Directory.GetDirectories(dir)
                .Where(d => !BruWorkspaceNames.SkippedDirectories.Contains(Path.GetFileName(d)))
                .ToArray();
            if (!hasFolderMeta && childBru.Length == 0 && childDirs.Length == 0)
            {
                continue; // plain scratch directory — not an item folder
            }

            BruBlock metaBlock;
            if (hasFolderMeta)
            {
                var folderFile = BruMarkup.Parse(File.ReadAllText(folderFilePath), folderFilePath);
                metaBlock = folderFile.First("meta") ?? new BruBlock
                {
                    Name = "meta",
                    Kind = BruBlockKind.Dictionary,
                    Pairs = [],
                };
            }
            else
            {
                metaBlock = new BruBlock { Name = "meta", Kind = BruBlockKind.Dictionary, Pairs = [] };
            }

            var parsedMeta = BruItemMapper.ParseMeta(metaBlock);
            var folderItemNode = new YamlMappingNode();
            var info = new YamlMappingNode();
            info.Children.Add(BrunoYaml.Scalar("name"), BrunoYaml.Scalar(parsedMeta.Name.Length > 0 ? parsedMeta.Name : dirName));
            info.Children.Add(BrunoYaml.Scalar("type"), BrunoYaml.Scalar("folder"));
            folderItemNode.Children.Add(BrunoYaml.Scalar("info"), info);
            targetItems.Children.Add(folderItemNode);

            ReadDirectory(dir, BrunoYaml.GetOrCreateSequence(folderItemNode, "items"), registry, store);
        }

        foreach (var file in Directory.GetFiles(directory).Order(StringComparer.Ordinal))
        {
            var fileName = Path.GetFileName(file);
            if (!BruMarkup.IsBruFileName(fileName) ||
                string.Equals(fileName, BruWorkspaceNames.FolderFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var sourceName = fileName;
            var bruFile = BruMarkup.Parse(File.ReadAllText(file), sourceName);

            var mappedBlocks = new List<BruBlock>();
            var parsedExamples = new List<YamlMappingNode>();
            var stableKey = ExtractStableKey(bruFile);
            foreach (var block in bruFile.Blocks)
            {
                // Sentinel-authored example blocks parse back into canonical examples so
                // incremental example management works; anything else without a canonical
                // field mapping is preserved raw. The metadata block flows through the
                // mapper into the index entry.
                if (block.Name == "example" && stableKey is not null &&
                    BruExampleParser.TryParse(block, out var parsed) && parsed is not null)
                {
                    parsedExamples.Add(parsed);
                    continue;
                }

                if (!BruWorkspaceNames.IsRawPreserved(block.Name))
                {
                    mappedBlocks.Add(block);
                    continue;
                }

                if (stableKey is null)
                {
                    throw new BrunoFormatException(
                        $"'{sourceName}': file contains preserved blocks ({block.Name}) but no Sentinel metadata identity; " +
                        "cannot attribute user content.");
                }

                registry.Add(stableKey, block);
            }

            var (item, entry) = BruItemMapper.ToCanonical(new BruFile { Blocks = mappedBlocks }, sourceName);

            // Rebuild the Sentinel binding: tag on the item + index entry at the collection.
            if (stableKey is not null)
            {
                item.BindSentinelTag(stableKey);
                if (entry is { } entryNode)
                {
                    var storeEntry = store.GetOrCreateEntry(stableKey);
                    foreach (var field in entryNode.Children)
                    {
                        storeEntry.Children.Add(BrunoYaml.DeepClone(field.Key), BrunoYaml.DeepClone(field.Value));
                    }
                }
            }

            if (parsedExamples.Count > 0)
            {
                var examplesSeq = BrunoYaml.GetOrCreateSequence(item.Node, "examples");
                foreach (var parsedExample in parsedExamples)
                {
                    examplesSeq.Children.Add(parsedExample);
                }
            }

            targetItems.Children.Add(item.Node);
        }
    }

    internal static string? ExtractStableKey(BruFile file) =>
        file.First("metadata")?.Pairs.FirstOrDefault(p => p.RawKey == BruItemMapper.MetaKeyKey)?.Value;
}

public sealed record BruManifest(string Version, string Name, string Type);

public static class BruManifestSerializer
{
    private static readonly JsonSerializerOptions ManifestOptions = new() { WriteIndented = true };
    /// <summary>Deterministic minimal manifest matching the official bruno.json schema.</summary>
    public static string Render(string name)
    {
        var safeName = (name ?? "").Trim() is { Length: > 0 } trimmed ? trimmed : "API Collection";
        return JsonSerializer.Serialize(new SortedDictionary<string, string>
        {
            ["version"] = "1",
            ["name"] = safeName.Length > 256 ? safeName[..256] : safeName,
            ["type"] = "collection",
        }, ManifestOptions);
    }

    public static BruManifest Parse(string json, string sourceName)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var version = root.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "1" : "1";
            var name = root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";
            var type = root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "";
            return new BruManifest(version, name, type);
        }
        catch (JsonException ex)
        {
            throw new BrunoFormatException($"'{sourceName}' is not valid JSON: {ex.Message}", ex);
        }
    }
}

/// <summary>Materializes the canonical collection as a directly-openable .bru collection.</summary>
public static class BruWorkspaceWriter
{
    public static string Manifest(BrunoCollectionDocument collection) =>
        BruManifestSerializer.Render(collection.CollectionName ?? "API Collection");

    public static WorkspacePlan Plan(
        BrunoCollectionDocument collection,
        string workspaceDir,
        BruRawRegistry? rawRegistry)
    {
        var plan = new WorkspacePlan();
        plan.DirectoriesToEnsure.Add(Path.GetFullPath(workspaceDir));
        plan.FilesToWrite.Add((Path.Combine(workspaceDir, BruWorkspaceNames.ManifestFileName), Manifest(collection)));

        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        WalkItems(collection.RootItems.ToList(), workspaceDir, plan, expected, rawRegistry, new HashSet<string>(StringComparer.OrdinalIgnoreCase), SentinelMetaStore.For(collection));

        CollectOrphans(collection, workspaceDir, expected, plan);
        return plan;
    }

    private static void WalkItems(
        IReadOnlyList<BrunoItem> items,
        string currentDir,
        WorkspacePlan plan,
        HashSet<string> expected,
        BruRawRegistry? rawRegistry,
        HashSet<string> usedDirs,
        SentinelMetaStore store)
    {
        var usedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items.OrderBy(i => i.Seq ?? int.MaxValue))
        {
            if (item.IsFolder)
            {
                var dirName = UniqueName(BrunoWorkspaceNames.Sanitize(item.Name ?? "untitled"), usedDirs);
                var targetDir = Path.Combine(currentDir, dirName);
                plan.DirectoriesToEnsure.Add(targetDir);

                // Existing folder.bru files are user data — never regenerated.
                if (!File.Exists(Path.Combine(targetDir, BruWorkspaceNames.FolderFileName)))
                {
                    var metaBlock = new BruBlock
                    {
                        Name = "meta",
                        Kind = BruBlockKind.Dictionary,
                        Pairs =
                        [
                            new BruPair("name", true, item.Name ?? "untitled"),
                            new BruPair("type", true, "folder"),
                        ],
                    };
                    plan.FilesToWrite.Add((
                        Path.Combine(targetDir, BruWorkspaceNames.FolderFileName),
                        BruMarkup.Serialize([metaBlock])));
                }

                WalkItems(item.Children.ToList(), targetDir, plan, expected, rawRegistry, usedDirs, store);
                continue;
            }

            var baseName = BrunoWorkspaceNames.Sanitize(item.Name ?? "untitled");
            var fileName = UniqueName(baseName, usedFiles) + ".bru";
            var filePath = Path.Combine(currentDir, fileName);
            var entry = item.SentinelTagKey is { } key ? store.EntryOrNull(key) : null;
            var blocks = BruItemWriter.Build(item, entry);
            if (item.SentinelTagKey is { } boundKey && rawRegistry?.For(boundKey) is { Count: > 0 } rawBlocks)
            {
                blocks.AddRange(rawBlocks);
            }

            AppendExampleBlocks(blocks, item);
            plan.FilesToWrite.Add((filePath, BruMarkup.Serialize(blocks)));
            expected.Add(Path.GetFullPath(filePath));
        }
    }

    /// <summary>Serializes canonical examples (generated AND hand-authored YAML) into
    /// official `example {}` blocks appended after all other content.</summary>
    private static void AppendExampleBlocks(List<BruBlock> blocks, BrunoItem item)
    {
        if (BrunoYaml.SequenceOf(item.Node, "examples") is not { } examplesSeq)
        {
            return;
        }

        foreach (var exampleMap in BrunoYaml.MappingItems(examplesSeq))
        {
            blocks.Add(BruExampleWriter.FromCanonical(exampleMap));
        }
    }

    private static void CollectOrphans(
        BrunoCollectionDocument collection,
        string workspaceDir,
        HashSet<string> expected,
        WorkspacePlan plan)
    {
        if (!Directory.Exists(workspaceDir))
        {
            return;
        }

        var store = SentinelMetaStore.For(collection);
        foreach (var file in Directory.EnumerateFiles(workspaceDir, "*.bru", SearchOption.AllDirectories))
        {
            var full = Path.GetFullPath(file);
            if (expected.Contains(full))
            {
                continue;
            }

            try
            {
                var parsed = BruMarkup.Parse(File.ReadAllText(full), full);
                if (BruWorkspaceReader.ExtractStableKey(parsed) is { } key && store.EntryOrNull(key) is not null)
                {
                    // Bound file whose operation vanished this run — cleaned up by Execute.
                    // Entries still referenced by OTHER files are checked implicitly: an
                    // entry belongs to exactly one file because keys are unique per operation.
                    plan.OrphanedFiles.Add(full);
                }
            }
            catch (BrunoFormatException)
            {
                // unreadable/unrelated .bru-like file — user data, never deleted
            }
        }
    }

    private static string UniqueName(string baseName, HashSet<string> usedNames)
    {
        if (usedNames.Add(baseName))
        {
            return baseName;
        }

        for (var i = 2; ; i++)
        {
            var candidate = $"{baseName}-{i}";
            if (usedNames.Add(candidate))
            {
                return candidate;
            }
        }
    }

}
