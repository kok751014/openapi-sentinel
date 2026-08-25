namespace OpenApiSentinel.Bruno;

/// <summary>The Bruno collection file could not be parsed or violates the supported format.</summary>
public sealed class BrunoFormatException : Exception
{
    public BrunoFormatException(string message) : base(message) { }

    public BrunoFormatException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>The requested Bruno operation cannot be performed safely (e.g. unsupported layout).</summary>
public sealed class BrunoSyncException : Exception
{
    public BrunoSyncException(string message) : base(message) { }

    public BrunoSyncException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Path resolution / safety guards for the bruno configuration.</summary>
public static class BrunoPaths
{
    /// <summary>
    /// Resolves <paramref name="path"/> against <paramref name="baseDirectory"/> (the config
    /// file's directory when known, otherwise the process working directory).
    /// </summary>
    public static string Resolve(string path, string? baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new BrunoSyncException("路徑不可為空。");
        }

        var root = string.IsNullOrEmpty(baseDirectory) ? "." : baseDirectory;
        return Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(root, path));
    }

    /// <summary>The collection target must be an OpenCollection YAML file — never a directory.</summary>
    public static void ValidateCollectionTarget(string collectionFile)
    {
        var full = Path.GetFullPath(collectionFile);
        if (Directory.Exists(full))
        {
            throw new BrunoSyncException(
                $"Bruno collection 目標 '{full}' 是一個目錄。OpenAPI Sentinel 目前支援單檔 OpenCollection YAML：" +
                "請將 collectionPath 指向 .yml 檔案（例如 ./api-contract/bruno/collection.yml）。" +
                "目錄式 (.bru) 集合無法被安全地增量同步；為避免破壞人工內容，此版本明確拒絕。詳見 docs/bruno-sync.md。");
        }

        var ext = Path.GetExtension(full);
        if (!string.Equals(ext, ".yml", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(ext, ".yaml", StringComparison.OrdinalIgnoreCase))
        {
            throw new BrunoSyncException(
                $"Bruno collection 目標 '{full}' 必須是 .yml/.yaml（OpenCollection YAML 格式）。");
        }
    }

    /// <summary>
    /// Verifies a directory we are about to create/write can actually be created:
    /// walks the un-created tail of <paramref name="directoryPath"/> down to the nearest
    /// EXISTING ancestor and fails clearly when that ancestor is a regular file
    /// (otherwise users get an opaque exit-5 IOException deep inside the run).
    /// </summary>
    public static void EnsureDirectoryAvailable(string directoryPath)
    {
        // Walk from the target path upward until we find an existing ancestor.
        // Directory.Exists is FALSE for a file path and vice versa, which makes both
        // checks explicit; a regular file anywhere in the un-created tail is an error.
        var current = Path.GetFullPath(directoryPath);
        while (!Directory.Exists(current))
        {
            if (File.Exists(current))
            {
                throw new BrunoSyncException(
                    $"無法建立目錄 '{Path.GetFullPath(directoryPath)}'：路徑上的 '{current}' 已存在且是檔案。請修正設定中的對應路徑。");
            }

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.Ordinal))
            {
                return; // reached the filesystem root without finding a blocking file
            }

            current = parent;
        }
    }

    /// <summary>Refuses layouts where reports could clobber the collection file.</summary>
    public static void ValidateReportAndCollectionDisjoint(string reportDir, string collectionFile)
    {
        var dir = Path.GetFullPath(reportDir);
        var col = Path.GetFullPath(collectionFile);
        if (string.Equals(dir, col, StringComparison.Ordinal))
        {
            throw new BrunoSyncException(
                $"reportPath 與 collectionPath 指向同一位置：'{dir}'。兩者必須不同以避免互相覆寫。");
        }
    }
}
