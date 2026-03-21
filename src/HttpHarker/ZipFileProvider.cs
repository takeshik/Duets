using System.IO.Compression;

namespace HttpHarker;

/// <summary>
/// Provides file bytes from a zip archive. The archive is read into memory once at construction;
/// individual entries are decompressed on demand per request, making concurrent access safe.
/// </summary>
public sealed class ZipFileProvider : IFileProvider
{
    public ZipFileProvider(Stream zipStream)
    {
        using var ms = new MemoryStream();
        zipStream.CopyTo(ms);
        this._zipBytes = ms.ToArray();

        using var archive = new ZipArchive(new MemoryStream(this._zipBytes), ZipArchiveMode.Read, false);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            // Skip directory entries (they end with '/')
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                continue;
            }

            map[NormalizeEntryPath(entry.FullName)] = entry.FullName;
        }

        this._entryMap = map;
    }

    private readonly byte[] _zipBytes;
    private readonly IReadOnlyDictionary<string, string> _entryMap;

    public byte[]? GetFileContent(string relativePath)
    {
        if (!this._entryMap.TryGetValue(relativePath, out var entryName))
        {
            return null;
        }

        // Each call opens its own MemoryStream over the immutable _zipBytes, so concurrent reads are safe.
        using var archive = new ZipArchive(new MemoryStream(this._zipBytes), ZipArchiveMode.Read, false);
        var entry = archive.GetEntry(entryName);
        if (entry is null)
        {
            return null;
        }

        using var entryStream = entry.Open();
        using var result = new MemoryStream();
        entryStream.CopyTo(result);
        return result.ToArray();
    }

    private static string NormalizeEntryPath(string fullName)
    {
        return fullName.Replace('\\', '/').TrimStart('/');
    }
}
