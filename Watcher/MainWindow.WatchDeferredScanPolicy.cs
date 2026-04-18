using System;
using System.IO;

namespace IndigoMovieManager;

public partial class MainWindow
{
    // deferred を持ったまま再収集する間は、最後に保存すべき cursor だけ新しい方へ寄せる。
    internal static DateTime? MergeDeferredWatchScanCursorUtc(
        DateTime? existingDeferredCursorUtc,
        DateTime? observedCursorUtc
    )
    {
        if (!existingDeferredCursorUtc.HasValue)
        {
            return observedCursorUtc;
        }

        if (!observedCursorUtc.HasValue)
        {
            return existingDeferredCursorUtc;
        }

        return existingDeferredCursorUtc.Value >= observedCursorUtc.Value
            ? existingDeferredCursorUtc
            : observedCursorUtc;
    }

    // watch差分の繰り延べ状態を、フォルダ+sub単位のキーへ正規化する。
    internal static string BuildDeferredWatchScanScopeKey(
        string dbFullPath,
        string watchFolder,
        bool includeSubfolders
    )
    {
        string normalizedDb = dbFullPath ?? "";
        string normalizedFolder = watchFolder ?? "";
        try
        {
            if (!string.IsNullOrWhiteSpace(normalizedDb))
            {
                normalizedDb = Path.GetFullPath(normalizedDb);
            }
        }
        catch
        {
            // 正規化失敗時も元文字列で継続する。
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(normalizedFolder))
            {
                normalizedFolder = Path.GetFullPath(normalizedFolder);
            }
        }
        catch
        {
            // 正規化失敗時も元文字列で継続する。
        }

        return
            $"{normalizedDb.Trim().ToLowerInvariant()}|{normalizedFolder.Trim().ToLowerInvariant()}|sub={(includeSubfolders ? 1 : 0)}";
    }
}
