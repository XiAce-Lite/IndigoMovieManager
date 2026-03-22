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
                // 既存登録側も末尾セパレータ差を潰した形へ揃えてから比較に載せる。
                string normalizedExistingDirectory = NormalizeDirectoryPath(existingDirectory);
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

                // 実在確認まで通った正規化済みパスを、そのまま比較と返却へ使う。
                if (!knownDirectories.Add(normalizedDroppedDirectory))
                {
                    duplicateCount++;
                    continue;
                }

                directoriesToAdd.Add(normalizedDroppedDirectory);
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
                // 比較と返却がぶれないよう、ここで末尾セパレータ差を吸収する。
                return Path.TrimEndingDirectorySeparator(Path.GetFullPath(directoryPath.Trim()));
            }
            catch (Exception)
            {
                return string.Empty;
            }
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
