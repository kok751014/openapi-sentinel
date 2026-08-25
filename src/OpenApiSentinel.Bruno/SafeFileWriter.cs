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
