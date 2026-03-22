using System.IO;

namespace IndigoMovieManager
{
    /// <summary>
    /// 監視フォルダ編集画面へドロップされたパス群を、
    /// 追加候補とスキップ理由へ整理するポリシークラス。
    /// </summary>
    internal static class WatchFolderDropRegistrationPolicy
    {
        // ドロップされたパスの中に、登録可能なフォルダが1件でも含まれるかを返す。
        internal static bool CanAccept(IEnumerable<string> droppedPaths)
        {
            foreach (string droppedPath in droppedPaths ?? Array.Empty<string>())
            {
                string normalizedDirectoryPath = NormalizeDirectoryPath(droppedPath);
                if (!string.IsNullOrEmpty(normalizedDirectoryPath) && Directory.Exists(normalizedDirectoryPath))
                {
                    return true;
                }
            }

            return false;
        }

        // 既存登録と照合しながら、追加対象とスキップ件数をまとめる。
        internal static WatchFolderDropResult Build(
            IEnumerable<string> droppedPaths,
            IEnumerable<string> existingDirectories
        )
        {
            var knownDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string existingDirectory in existingDirectories ?? Array.Empty<string>())
            {
                // 比較キーは既存登録側もドロップ側も同じ正規化へ通す。
                string normalizedExistingDirectory = NormalizeDirectoryComparisonKey(existingDirectory);
                if (!string.IsNullOrEmpty(normalizedExistingDirectory))
                {
                    knownDirectories.Add(normalizedExistingDirectory);
                }
            }

            var directoriesToAdd = new List<string>();
            int duplicateCount = 0;
            int invalidCount = 0;

            foreach (string droppedPath in droppedPaths ?? Array.Empty<string>())
            {
                string normalizedDroppedDirectory = NormalizeDirectoryPath(droppedPath);
                if (string.IsNullOrEmpty(normalizedDroppedDirectory) || !Directory.Exists(normalizedDroppedDirectory))
                {
                    invalidCount++;
                    continue;
                }

                // 実在確認後に返却値も比較キーも同じ canonical 形へ揃える。
                string canonicalDroppedDirectory = Path.TrimEndingDirectorySeparator(
                    normalizedDroppedDirectory
                );
                if (!knownDirectories.Add(canonicalDroppedDirectory))
                {
                    duplicateCount++;
                    continue;
                }

                directoriesToAdd.Add(canonicalDroppedDirectory);
            }

            return new WatchFolderDropResult(directoriesToAdd, duplicateCount, invalidCount);
        }

        // 比較用にパス表記を正規化し、壊れた入力は空扱いへ落とす。
        internal static string NormalizeDirectoryPath(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(directoryPath.Trim());
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        // 比較キーでは末尾セパレータ差異を吸収し、同じフォルダを同一視する。
        private static string NormalizeDirectoryComparisonKey(string directoryPath)
        {
            string normalizedDirectoryPath = NormalizeDirectoryPath(directoryPath);
            return string.IsNullOrEmpty(normalizedDirectoryPath)
                ? string.Empty
                : Path.TrimEndingDirectorySeparator(normalizedDirectoryPath);
        }
    }

    /// <summary>
    /// フォルダドロップの判定結果を保持する。
    /// </summary>
    internal sealed class WatchFolderDropResult
    {
        internal WatchFolderDropResult(
            IReadOnlyList<string> directoriesToAdd,
            int duplicateCount,
            int invalidCount
        )
        {
            DirectoriesToAdd = directoriesToAdd;
            DuplicateCount = duplicateCount;
            InvalidCount = invalidCount;
        }

        internal IReadOnlyList<string> DirectoriesToAdd { get; }

        internal int DuplicateCount { get; }

        internal int InvalidCount { get; }
    }
}
