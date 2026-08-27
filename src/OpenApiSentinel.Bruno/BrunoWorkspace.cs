using OpenApiSentinel.Bruno.Model;
using YamlDotNet.RepresentationModel;

namespace OpenApiSentinel.Bruno;

/// <summary>
/// Fixed names and filesystem-safe naming rules of the official OpenCollection
/// folder-based workspace layout (as read/written by Bruno Desktop):
///
///   opencollection.yml            collection root (info/extensions/config/docs, bundled:false)
///   &lt;folder&gt;/folder.yml          folder metadata (info + docs + request defaults)
///   &lt;request&gt;.yml                 one self-contained request item per file
///   environments/                 Bruno-owned, never touched by Sentinel
/// </summary>
public static class BrunoWorkspaceNames
{
    public const string RootFileName = "opencollection.yml";
    public const string FolderFileName = "folder.yml";
    public const string EnvironmentsDir = "environments";

    /// <summary>Directories that are never OpenCollection item folders.</summary>
    public static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    { EnvironmentsDir, "node_modules", ".git" };

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
      "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };

    /// <summary>
    /// Maps an item name to a safe single path component on Windows/Linux/macOS:
    /// invalid characters become '-', leading spaces/hyphens are trimmed, trailing
    /// dots/spaces removed, Windows reserved device names are prefixed. The result can
    /// never be empty, "." or "..", so no traversal is possible.
    /// </summary>
    public static string Sanitize(string name)
    {
        var chars = name.Select(c => c is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*' || char.IsControl(c)
            ? '-' : c).ToArray();
        var sanitized = new string(chars).TrimStart(' ', '-').TrimEnd(' ', '.');

        if (sanitized.Length == 0)
        {
            sanitized = "untitled";
        }

        if (ReservedDeviceNames.Contains(sanitized))
        {
            sanitized = "-" + sanitized;
        }

        return sanitized;
    }
}

/// <summary>A computed workspace update: nothing has touched the disk yet.</summary>
public sealed class WorkspacePlan
{
    /// <summary>Directories that must exist before files are written (in order).</summary>
    public List<string> DirectoriesToEnsure { get; } = [];

    /// <summary>Files to write (content already finalized).</summary>
    public List<(string Path, string Content)> FilesToWrite { get; } = [];

    /// <summary>Files that carry a Sentinel binding tag whose index entry no longer exists —
    /// the operation was explicitly removed; the orphaned file is cleaned up.</summary>
    public List<string> OrphanedFiles { get; } = [];

    /// <summary>Relative display paths of everything planned, for console/dry-run output.</summary>
    public IEnumerable<string> Describe()
    {
        foreach (var dir in DirectoriesToEnsure)
        {
            yield return $"[預計建立資料夾] {dir}";
        }

        foreach (var file in FilesToWrite)
        {
            yield return $"[預計建立檔案] {file.Path}";
        }

        foreach (var file in OrphanedFiles)
        {
            yield return $"[預計刪除] {file}";
        }
    }
}

/// <summary>
/// Reads a folder-based OpenCollection workspace back into ONE canonical
/// <see cref="BrunoCollectionDocument"/> so the incremental sync engine can treat the
/// workspace exactly like a bundled collection file. Unknown keys in every file survive.
/// </summary>
public static class BrunoWorkspaceReader
{
    public static bool IsWorkspace(string directory) =>
        Directory.Exists(directory) && File.Exists(Path.Combine(directory, BrunoWorkspaceNames.RootFileName));

    public static BrunoCollectionDocument Read(string directory)
    {
        var rootPath = Path.Combine(directory, BrunoWorkspaceNames.RootFileName);
        var root = BrunoYaml.ParseDocument(File.ReadAllText(rootPath), rootPath);

        if (BrunoYaml.HasKey(root, "items"))
        {
            throw new BrunoFormatException(
                $"workspace '{rootPath}' 意外包含 items 區塊；資料夾式工作區的項目應存放於個別檔案。");
        }

        root.Children.Add(BrunoYaml.Scalar("items"), new YamlSequenceNode());
        ReadDirectory(directory, root);
        return BrunoCollectionDocument.Wrap(root);
    }

    private static void ReadDirectory(string directory, YamlMappingNode parent)
    {
        var items = BrunoYaml.GetOrCreateSequence(parent, "items");
        foreach (var dir in Directory.GetDirectories(directory).Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(dir);
            if (BrunoWorkspaceNames.SkippedDirectories.Contains(name))
            {
                continue;
            }

            var folderFile = Path.Combine(dir, BrunoWorkspaceNames.FolderFileName);
            if (!File.Exists(folderFile))
            {
                continue; // not an item folder (user scratch space) — leave untouched
            }

            var folderItem = BrunoYaml.ParseDocument(File.ReadAllText(folderFile), folderFile);
            if (!BrunoYaml.HasKey(folderItem, "info"))
            {
                folderItem.Children.Insert(0, BrunoYaml.Scalar("info"), new YamlMappingNode());
            }

            folderItem.Children.Add(BrunoYaml.Scalar("items"), new YamlSequenceNode());
            items.Children.Add(folderItem);
            ReadDirectory(dir, folderItem);
        }

        foreach (var file in Directory.GetFiles(directory).Order(StringComparer.Ordinal))
        {
            var ext = Path.GetExtension(file);
            if (ext is not (".yml" or ".yaml"))
            {
                continue;
            }

            var fileName = Path.GetFileName(file);
            if (fileName is BrunoWorkspaceNames.RootFileName or BrunoWorkspaceNames.FolderFileName)
            {
                continue;
            }

            items.Children.Add(BrunoYaml.ParseDocument(File.ReadAllText(file), file));
        }

        SortBySeqThenName(items);
    }

    internal static void SortBySeqThenName(YamlSequenceNode items)
    {
        var ordered = items.Children
            .OfType<YamlMappingNode>()
            .OrderBy(ItemSeq)
            .ThenBy(item => BrunoYaml.AsString(BrunoYaml.Get(BrunoYaml.MappingOf(item, "info") ?? [], "name")) ?? "",
                StringComparer.Ordinal)
            .ToList();

        if (ordered.Count != items.Children.Count)
        {
            return; // non-mapping children present — leave untouched rather than drop data
        }

        items.Children.Clear();
        foreach (var child in ordered)
        {
            items.Children.Add(child);
        }
    }

    private static int ItemSeq(YamlMappingNode item) =>
        BrunoYaml.AsInt(BrunoYaml.Get(BrunoYaml.MappingOf(item, "info") ?? [], "seq")) ?? int.MaxValue;
}

/// <summary>
/// Materializes the canonical collection model as a folder-based OpenCollection workspace.
/// The SAME model also produces the bundled collection.yml, so the two outputs cannot drift.
/// Writes are content-compared: unchanged files keep their bytes (clean git status).
/// </summary>
public static class BrunoWorkspaceWriter
{
    public static WorkspacePlan Plan(BrunoCollectionDocument collection, string workspaceDir)
    {
        var plan = new WorkspacePlan();
        plan.DirectoriesToEnsure.Add(Path.GetFullPath(workspaceDir));

        // Root file: everything except items, with bundled:false (items live on disk).
        var root = (YamlMappingNode)BrunoYaml.DeepClone(collection.Root);
        BrunoYaml.RemoveKey(root, "items");
        BrunoYaml.Set(root, "bundled", BrunoYaml.Scalar(false));
        plan.FilesToWrite.Add((Path.Combine(workspaceDir, BrunoWorkspaceNames.RootFileName), BrunoYaml.ToYaml(root)));

        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        WalkItems(collection.RootItems.ToList(), workspaceDir, plan, expected);
        CollectOrphans(workspaceDir, expected, collection, plan);
        return plan;
    }

    private static void WalkItems(
        IReadOnlyList<OpenApiSentinel.Bruno.Model.BrunoItem> items,
        string currentDir,
        WorkspacePlan plan,
        HashSet<string> expected,
        HashSet<string>? usedNames = null)
    {
        usedNames ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            if (item.IsFolder)
            {
                var dirName = UniqueName(BrunoWorkspaceNames.Sanitize(item.Name ?? "untitled"), usedNames);
                var targetDir = Path.Combine(currentDir, dirName);
                plan.DirectoriesToEnsure.Add(targetDir);

                // Folder file: the item node minus its children sequence.
                var folderNode = (YamlMappingNode)BrunoYaml.DeepClone(item.Node);
                BrunoYaml.RemoveKey(folderNode, "items");
                var folderFilePath = Path.Combine(targetDir, BrunoWorkspaceNames.FolderFileName);
                plan.FilesToWrite.Add((folderFilePath, BrunoYaml.ToYaml(folderNode)));

                WalkItems(item.Children.ToList(), targetDir, plan, expected);
            }
            else
            {
                var baseName = BrunoWorkspaceNames.Sanitize(item.Name ?? "untitled");
                var fileName = UniqueName(baseName, usedNames) + ".yml";
                var filePath = Path.Combine(currentDir, fileName);
                plan.FilesToWrite.Add((filePath, BrunoYaml.ToYaml(item.Node)));
                expected.Add(Path.GetFullPath(filePath));
            }
        }
    }

    private static void CollectOrphans(string workspaceDir, HashSet<string> expected, BrunoCollectionDocument collection, WorkspacePlan plan)
    {
        if (!Directory.Exists(workspaceDir))
        {
            return;
        }

        var store = SentinelMetaStore.For(collection);
        foreach (var file in Directory.EnumerateFiles(workspaceDir, "*", SearchOption.AllDirectories))
        {
            var full = Path.GetFullPath(file);
            var fileName = Path.GetFileName(full);
            if (fileName is BrunoWorkspaceNames.RootFileName or BrunoWorkspaceNames.FolderFileName)
            {
                continue;
            }

            if (ext(fileName) is not (".yml" or ".yaml"))
            {
                continue;
            }

            if (expected.Contains(full))
            {
                continue;
            }

            try
            {
                var node = BrunoYaml.ParseDocument(File.ReadAllText(full), full);
                if (new Model.BrunoItem(node).SentinelTagKey is { } key && store.EntryOrNull(key) is null)
                {
                    plan.OrphanedFiles.Add(full);
                }
            }
            catch (BrunoFormatException)
            {
                // Unreadable/unrelated YAML — user data; never deleted by us.
            }
        }

        static string? ext(string name) => Path.GetExtension(name);
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

    /// <summary>Executes a plan. Returns user-facing "+/-" lines for what actually changed.</summary>
    public static List<string> Execute(WorkspacePlan plan)
    {
        var changes = new List<string>();

        foreach (var dir in plan.DirectoriesToEnsure.Order(StringComparer.Ordinal))
        {
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                changes.Add($"+ {dir}");
            }
        }

        foreach (var (path, content) in plan.FilesToWrite)
        {
            if (File.Exists(path) && string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
            {
                continue;
            }

            SafeFileWriter.Write(path, content);
            changes.Add($"+ {path}");
        }

        foreach (var orphan in plan.OrphanedFiles)
        {
            File.Delete(orphan);
            changes.Add($"- {orphan}");
        }

        PruneEmptyDirectories(plan.DirectoriesToEnsure.Count > 0 ? plan.DirectoriesToEnsure[0] : string.Empty);
        return changes;
    }

    private static void PruneEmptyDirectories(string root)
    {
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            return;
        }

        foreach (var dir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories)
                     .Order(StringComparer.Ordinal).Reverse())
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                }
            }
            catch (IOException)
            {
                // best effort only — a busy directory simply survives this sync
            }
        }
    }
}
