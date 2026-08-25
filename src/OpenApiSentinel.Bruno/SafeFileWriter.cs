namespace OpenApiSentinel.Bruno;

/// <summary>
/// Atomic, cross-platform file writes: content goes to a temp file in the target
/// directory first, then replaces the target in a single move. A failed write never
/// corrupts an existing collection.
/// </summary>
public static class SafeFileWriter
{
    public static void Write(string path, string contents)
    {
        var fullPath = Path.GetFullPath(path);

        // If the final path component is a symlink (common when collections live in
        // dotfiles-managed trees), write THROUGH it: replace the resolved target
        // atomically so the user's link survives. Replacing the link itself would
        // silently destroy their setup.
        var info = new FileInfo(fullPath);
        if (info.Exists)
        {
            var final = info.ResolveLinkTarget(returnFinalTarget: true);
            if (final is not null)
            {
                fullPath = final.FullName;
            }
        }

        var dir = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(dir))
        {
            dir = Directory.GetCurrentDirectory();
        }

        Directory.CreateDirectory(dir);

        var temp = Path.Combine(dir, $".{Path.GetFileName(fullPath)}.tmp-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(temp, contents);
            // Linux/macOS: atomic rename; Windows: Replace keeps backup semantics.
            if (File.Exists(fullPath))
            {
                File.Replace(temp, fullPath, null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temp, fullPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new BrunoSyncException($"無法寫入檔案 '{fullPath}'：{ex.Message}", ex.InnerException is null ? ex : ex.InnerException);
        }
        finally
        {
            try
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
            catch (IOException)
            {
                // best effort cleanup only
            }
        }
    }
}
