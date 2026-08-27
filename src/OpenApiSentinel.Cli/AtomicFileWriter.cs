namespace OpenApiSentinel.Cli;

/// <summary>Atomic, cross-platform replacement for raw comparison snapshots. A final-path
/// symlink is followed so the link itself is preserved.</summary>
internal static class AtomicFileWriter
{
    internal static void Write(string path, string contents)
    {
        var targetPath = Path.GetFullPath(path);
        var info = new FileInfo(targetPath);
        if (info.Exists && info.ResolveLinkTarget(returnFinalTarget: true) is { } target)
        {
            targetPath = target.FullName;
        }

        var directory = Path.GetDirectoryName(targetPath) ?? Directory.GetCurrentDirectory();
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(targetPath)}.tmp-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(tempPath, contents);
            if (File.Exists(targetPath))
            {
                File.Replace(tempPath, targetPath, null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, targetPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Cannot atomically update snapshot '{targetPath}': {ex.Message}", ex);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch (IOException)
            {
                // Best-effort cleanup: an existing snapshot was never modified on failure.
            }
        }
    }
}
