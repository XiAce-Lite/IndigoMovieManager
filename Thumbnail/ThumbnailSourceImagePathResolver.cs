using System.IO;

namespace IndigoMovieManager;

/// <summary>
/// 動画の隣にある同名画像を、UI fallback や事前判定で安全に拾う。
/// </summary>
internal static class ThumbnailSourceImagePathResolver
{
    private static readonly string[] SupportedImageExtensions = [".jpg", ".jpeg", ".png"];

    internal static bool HasSameNameThumbnailSourceImage(string movieFullPath)
    {
        return TryResolveSameNameThumbnailSourceImagePath(movieFullPath, out _);
    }

    internal static bool TryResolveSameNameThumbnailSourceImagePath(
        string movieFullPath,
        out string sourceImagePath
    )
    {
        sourceImagePath = "";
        if (string.IsNullOrWhiteSpace(movieFullPath))
        {
            return false;
        }

        for (int i = 0; i < SupportedImageExtensions.Length; i++)
        {
            string candidatePath = Path.ChangeExtension(
                movieFullPath,
                SupportedImageExtensions[i]
            );
            if (!HasUsableFile(candidatePath))
            {
                continue;
            }

            sourceImagePath = candidatePath;
            return true;
        }

        return false;
    }

    // UI fallback では「存在して開ける可能性が高い画像」だけを返したいので 0 byte は除外する。
    private static bool HasUsableFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            FileInfo fi = new(path);
            return fi.Exists && fi.Length > 0;
        }
        catch
        {
            return false;
        }
    }
}
