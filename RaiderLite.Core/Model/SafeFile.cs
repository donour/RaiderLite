using System.Text;

namespace RaiderLite.Core.Model;

/// <summary>
/// Writes files so that a failed write, a full disk or a power cut can't leave them truncated or
/// half written: the data goes to a temporary file next to the target, flushed to disk, which then
/// replaces the target in one step.
/// </summary>
public static class SafeFile
{
    public static void WriteAllBytes(string path, ReadOnlySpan<byte> bytes)
    {
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            Replace(temp, path);
        }
        catch
        {
            try
            {
                File.Delete(temp);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                // Leave it; the target is untouched either way.
            }
            throw;
        }
    }

    /// <summary>Writes UTF-8 text without a byte order mark.</summary>
    public static void WriteAllText(string path, string text) =>
        WriteAllBytes(path, new UTF8Encoding(false).GetBytes(text));

    /// <summary>Moves <paramref name="source"/> over <paramref name="destination"/> in one step.</summary>
    private static void Replace(string source, string destination)
    {
        if (!File.Exists(destination))
        {
            File.Move(source, destination);
            return;
        }
        try
        {
            // Atomic, and keeps the target's attributes and permissions.
            File.Replace(source, destination, destinationBackupFileName: null);
        }
        catch (IOException)
        {
            // Some file systems (e.g. network shares) can't replace; a move still never truncates.
            File.Move(source, destination, overwrite: true);
        }
    }
}
