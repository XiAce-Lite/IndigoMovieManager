using System.IO;
using EverythingSearchClient;

namespace IndigoMovieManager.Watcher
{
    /// <summary>
    /// Everything IPCを駆使して「監視フォルダ内のファイル列挙」を圧倒的爆速でこなす特攻隊長だ！⚡
    /// もしコイツが不発でも、呼び出し側が従来のファイルシステム走査へシームレスに切り替える安心設計！
    /// </summary>
    internal sealed class EverythingFolderSyncService
    {
        private const uint SearchLimit = 1_000_000;
        private const uint ReceiveTimeoutMs = 1500;
        private const int IntegrationModeOff = 0;
        private const int IntegrationModeAuto = 1;
        private const int IntegrationModeOn = 2;

        /// <summary>
        /// UIからの設定値を安全圏（0/1/2）へ優しく丸め込むオカン処理だ！🤱
        /// </summary>
        private static int GetIntegrationMode()
        {
            int mode = Properties.Settings.Default.EverythingIntegrationMode;
            return mode switch
            {
                IntegrationModeOff => IntegrationModeOff,
                IntegrationModeOn => IntegrationModeOn,
                _ => IntegrationModeAuto,
            };
        }

        /// <summary>
        /// そもそもEverything連携のスイッチが入っているか（OFF以外か）をズバッと判定するぜ！🔘
        /// </summary>
        public bool IsIntegrationConfigured()
        {
            return GetIntegrationMode() != IntegrationModeOff;
        }

        /// <summary>
        /// Everything連携がONで、かつIPC通信の準備が万端かを見極める死活監視だ！🩺
        /// </summary>
        public bool CanUseEverything(out string reason)
        {
            reason = "";
            int mode = GetIntegrationMode();
            if (mode == IntegrationModeOff)
            {
                reason = "setting_disabled";
                return false;
            }

            try
            {
                if (!SearchClient.IsEverythingAvailable())
                {
                    reason =
                        mode == IntegrationModeAuto
                            ? "auto_not_available"
                            : "everything_not_available";
                    return false;
                }
            }
            catch (Exception ex)
            {
                reason = $"availability_error:{ex.GetType().Name}";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 監視フォルダに眠る珠玉の動画パス候補をEverything先生から一気に引っ張り出すぜ！🎣
        /// </summary>
        public bool TryCollectMoviePaths(
            string watchFolder,
            bool includeSubdirectories,
            string checkExt,
            DateTime? changedSinceUtc,
            out List<string> moviePaths,
            out DateTime? maxObservedChangedUtc,
            out string reason
        )
        {
            moviePaths = [];
            maxObservedChangedUtc = null;
            if (!CanUseEverything(out reason))
            {
                return false;
            }

            try
            {
                // SearchClientの共有は避け、問い合わせごとに新規生成して競合を防ぐ。
                SearchClient searchClient = new();
                string normalizedRootWithSlash = NormalizeDirectoryPathWithTrailingSlash(
                    watchFolder
                );
                string normalizedRootWithoutSlash = NormalizeDirectoryPathWithoutTrailingSlash(
                    watchFolder
                );
                HashSet<string> targetExtensions = ParseTargetExtensions(checkExt);
                List<string> queryList = BuildEverythingQueries(
                    normalizedRootWithSlash,
                    targetExtensions
                );
                HashSet<string> dedupe = new(StringComparer.OrdinalIgnoreCase);

                // 拡張子ごとのクエリに分割し、動画以外のヒットを先に減らして取りこぼしを防ぐ。
                foreach (string query in queryList)
                {
                    Result result = searchClient.Search(
                        query,
                        SearchClient.SearchFlags.MatchPath,
                        SearchLimit,
                        0,
                        SearchClient.BehaviorWhenBusy.WaitOrContinue,
                        ReceiveTimeoutMs,
                        SearchClient.SortBy.Path,
                        SearchClient.SortDirection.Ascending
                    );

                    // 上限件数で打ち切られた場合は、不完全結果を採用せず既存走査へフォールバックする。
                    if (result.TotalItems > result.NumItems)
                    {
                        reason =
                            $"everything_result_truncated:{result.NumItems}/{result.TotalItems}";
                        moviePaths = [];
                        return false;
                    }

                    foreach (Result.Item item in result.Items ?? [])
                    {
                        if (IsContainer(item.Flags))
                        {
                            continue;
                        }

                        string fullPath = BuildFullPath(item);
                        if (string.IsNullOrWhiteSpace(fullPath))
                        {
                            continue;
                        }

                        // ゴミ箱配下はwatch候補に混ぜず、通常の動画検出対象から除外する。
                        if (WatchPathFilter.ShouldExcludeFromWatchScan(fullPath))
                        {
                            continue;
                        }

                        if (!IsUnderRoot(fullPath, normalizedRootWithSlash))
                        {
                            continue;
                        }

                        if (
                            !includeSubdirectories
                            && !IsDirectChild(fullPath, normalizedRootWithoutSlash)
                        )
                        {
                            continue;
                        }

                        if (!IsTargetExtension(fullPath, targetExtensions))
                        {
                            continue;
                        }

                        if (!IsChangedSince(item, changedSinceUtc))
                        {
                            continue;
                        }

                        if (TryGetItemChangedUtc(item, out DateTime itemChangedUtc))
                        {
                            if (
                                !maxObservedChangedUtc.HasValue
                                || itemChangedUtc > maxObservedChangedUtc.Value
                            )
                            {
                                maxObservedChangedUtc = itemChangedUtc;
                            }
                        }

                        if (dedupe.Add(fullPath))
                        {
                            moviePaths.Add(fullPath);
                        }
                    }
                }

                reason = changedSinceUtc.HasValue
                    ? $"ok:query_count={queryList.Count} since={changedSinceUtc.Value:O}"
                    : $"ok:query_count={queryList.Count}";
                return true;
            }
            catch (Exception ex)
            {
                reason = $"everything_query_error:{ex.GetType().Name}";
                moviePaths = [];
                maxObservedChangedUtc = null;
                return false;
            }
        }

        /// <summary>
        /// サムネイルフォルダからすべてのjpgをかき集め、ファイル名本体（Body）だけを抽出してHashSetで返すぜ！🗃️
        /// 動画一覧との突き合わせ（Everything to Everything検証）を光の速さで終わらせるための最強メソッドだ！💨
        /// </summary>
        public bool TryCollectThumbnailBodies(
            string thumbFolder,
            out HashSet<string> existingThumbBodies,
            out string reason
        )
        {
            existingThumbBodies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!CanUseEverything(out reason))
            {
                return false;
            }

            try
            {
                SearchClient searchClient = new();
                string normalizedRootWithSlash = NormalizeDirectoryPathWithTrailingSlash(
                    thumbFolder
                );
                string quotedRoot = QuoteForEverything(normalizedRootWithSlash);

                // サムネイルは jpg のみ出力される前提
                string query = $"{quotedRoot} ext:jpg";

                Result result = searchClient.Search(
                    query,
                    SearchClient.SearchFlags.MatchPath,
                    SearchLimit,
                    0,
                    SearchClient.BehaviorWhenBusy.WaitOrContinue,
                    ReceiveTimeoutMs,
                    SearchClient.SortBy.Path,
                    SearchClient.SortDirection.Ascending
                );

                if (result.TotalItems > result.NumItems)
                {
                    reason = $"everything_result_truncated:{result.NumItems}/{result.TotalItems}";
                    existingThumbBodies.Clear();
                    return false;
                }

                foreach (Result.Item item in result.Items ?? [])
                {
                    if (IsContainer(item.Flags))
                    {
                        continue;
                    }

                    string fileName = item.Name ?? "";
                    if (string.IsNullOrWhiteSpace(fileName))
                    {
                        continue;
                    }

                    // サムネのファイル名規則（"{body}.#{hash}.jpg" や "{body}.#ERROR.jpg"）から "{body}" を抽出する。
                    string body = ExtractThumbnailBody(fileName);
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        existingThumbBodies.Add(body);
                    }
                }

                reason = "ok";
                return true;
            }
            catch (Exception ex)
            {
                reason = $"everything_thumb_query_error:{ex.GetType().Name}";
                existingThumbBodies.Clear();
                return false;
            }
        }

        /// <summary>
        /// "{body}.#{hash}.jpg" のような複雑なサムネ名から、純粋な "{body}" だけを抽出する職人技だ！🔪
        /// </summary>
        private static string ExtractThumbnailBody(string fileName)
        {
            // まず拡張子( .jpg )を取り除く
            string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrWhiteSpace(nameWithoutExt))
            {
                return "";
            }

            // ".#" を目印にして、その後ろのハッシュ部分をごっそり落とす。
            int hashMarkerIndex = nameWithoutExt.LastIndexOf(
                ".#",
                StringComparison.OrdinalIgnoreCase
            );
            if (hashMarkerIndex >= 0)
            {
                return nameWithoutExt[..hashMarkerIndex];
            }

            // 何らかの理由でハッシュマーカーが無い場合はそのまま返す（イレギュラーケース）
            return nameWithoutExt;
        }

        private static bool IsContainer(Result.ItemFlags flags)
        {
            return (flags & Result.ItemFlags.Folder) == Result.ItemFlags.Folder
                || (flags & Result.ItemFlags.Drive) == Result.ItemFlags.Drive;
        }

        private static string BuildFullPath(Result.Item item)
        {
            string name = item.Name ?? "";
            string path = item.Path ?? "";
            if (string.IsNullOrWhiteSpace(path))
            {
                return name;
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                return path;
            }
            return Path.Combine(path, name);
        }

        private static bool IsUnderRoot(string candidatePath, string rootWithSlash)
        {
            try
            {
                string normalizedCandidate = Path.GetFullPath(candidatePath);
                return normalizedCandidate.StartsWith(
                    rootWithSlash,
                    StringComparison.OrdinalIgnoreCase
                );
            }
            catch
            {
                return false;
            }
        }

        private static bool IsDirectChild(string candidatePath, string rootWithoutSlash)
        {
            try
            {
                string normalizedCandidate = Path.GetFullPath(candidatePath);
                string parent = Path.GetDirectoryName(normalizedCandidate) ?? "";
                string normalizedParent = NormalizeDirectoryPathWithoutTrailingSlash(parent);
                return string.Equals(
                    normalizedParent,
                    rootWithoutSlash,
                    StringComparison.OrdinalIgnoreCase
                );
            }
            catch
            {
                return false;
            }
        }

        private static bool IsTargetExtension(string fullPath, HashSet<string> targetExtensions)
        {
            if (targetExtensions.Count < 1)
            {
                return true;
            }

            string ext = Path.GetExtension(fullPath);
            return targetExtensions.Contains(ext);
        }

        private static bool IsChangedSince(Result.Item item, DateTime? changedSinceUtc)
        {
            if (!changedSinceUtc.HasValue)
            {
                return true;
            }

            DateTime baselineUtc = NormalizeToUtc(changedSinceUtc.Value);
            if (item.LastWriteTime.HasValue)
            {
                return NormalizeToUtc(item.LastWriteTime.Value) >= baselineUtc;
            }

            if (item.CreationTime.HasValue)
            {
                return NormalizeToUtc(item.CreationTime.Value) >= baselineUtc;
            }

            // タイムスタンプが取れない項目は取りこぼしを避けるため含める。
            return true;
        }

        private static bool TryGetItemChangedUtc(Result.Item item, out DateTime changedUtc)
        {
            if (item.LastWriteTime.HasValue)
            {
                changedUtc = NormalizeToUtc(item.LastWriteTime.Value);
                return true;
            }

            if (item.CreationTime.HasValue)
            {
                changedUtc = NormalizeToUtc(item.CreationTime.Value);
                return true;
            }

            changedUtc = default;
            return false;
        }

        private static HashSet<string> ParseTargetExtensions(string checkExt)
        {
            HashSet<string> targetExtensions = new(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(checkExt))
            {
                return targetExtensions;
            }

            string[] parts = checkExt.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (string raw in parts)
            {
                string ext = raw.Trim().Replace("*", "");
                if (string.IsNullOrWhiteSpace(ext))
                {
                    continue;
                }
                if (!ext.StartsWith('.'))
                {
                    ext = "." + ext;
                }
                targetExtensions.Add(ext);
            }

            return targetExtensions;
        }

        /// <summary>
        /// 監視フォルダ ＋ 動画拡張子のコンボでEverything先生に投げるクエリを錬成するぜ！🧙‍♂️
        /// 拡張子指定が無ければ従来通りのフォルダ単独クエリを返す、気の利くヤツだ！
        /// </summary>
        private static List<string> BuildEverythingQueries(
            string normalizedRootWithSlash,
            HashSet<string> targetExtensions
        )
        {
            string quotedRoot = QuoteForEverything(normalizedRootWithSlash);
            if (targetExtensions.Count < 1)
            {
                return [quotedRoot];
            }

            List<string> queries = [];
            foreach (string ext in targetExtensions)
            {
                string extWithoutDot = ext.TrimStart('.');
                if (string.IsNullOrWhiteSpace(extWithoutDot))
                {
                    continue;
                }

                // ext: でEverything側を先に絞る。C#側判定は保険として残す。
                queries.Add($"{quotedRoot} ext:{extWithoutDot}");
            }

            if (queries.Count < 1)
            {
                queries.Add(quotedRoot);
            }

            return queries;
        }

        private static string QuoteForEverything(string value)
        {
            string escaped = value.Replace("\"", "\"\"");
            return $"\"{escaped}\"";
        }

        private static DateTime NormalizeToUtc(DateTime value)
        {
            return value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime(),
            };
        }

        private static string NormalizeDirectoryPathWithTrailingSlash(string path)
        {
            string normalized = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return normalized + Path.DirectorySeparatorChar;
        }

        private static string NormalizeDirectoryPathWithoutTrailingSlash(string path)
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
