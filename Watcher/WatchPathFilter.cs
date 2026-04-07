using System.IO;

namespace IndigoMovieManager.Watcher
{
    /// <summary>
    /// watch走査で拾ってはいけない特殊パスを、入口でまとめて弾く門番。
    /// </summary>
    internal static class WatchPathFilter
    {
        private const string RecycleBinDirectoryName = "$RECYCLE.BIN";

        /// <summary>
        /// ゴミ箱配下のような運用対象外パスは、検出・登録・キュー投入の前段で除外する。
        /// </summary>
        internal static bool ShouldExcludeFromWatchScan(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                return false;
            }

            string[] segments = fullPath.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
            foreach (string segment in segments)
            {
                if (string.Equals(segment, RecycleBinDirectoryName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
