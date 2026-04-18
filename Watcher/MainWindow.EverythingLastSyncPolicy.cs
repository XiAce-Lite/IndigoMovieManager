using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace IndigoMovieManager;

public partial class MainWindow
{
    private const string EverythingLastSyncAttrPrefix = "everything_last_sync_utc_";

    private static string BuildEverythingLastSyncAttr(string watchFolder, bool sub)
    {
        string normalized = Path.GetFullPath(watchFolder).Trim().ToLowerInvariant();
        string material = $"{normalized}|sub={(sub ? 1 : 0)}";
        byte[] bytes = Encoding.UTF8.GetBytes(material);
        byte[] hash = SHA256.HashData(bytes);
        string hex = Convert.ToHexString(hash).ToLowerInvariant();
        return $"{EverythingLastSyncAttrPrefix}{hex[..16]}";
    }
}
