using System.Data;
using System.Diagnostics;
using System.IO;
using IndigoMovieManager.Watcher;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        /// <summary>
        /// FileSystemWatcherから「新入りが来たぞ！」と報告が上がった時の出迎え処理だぜ！🎉
        /// </summary>
        private void FileChanged(object sender, FileSystemEventArgs e)
        {
            try
            {
                var ext = Path.GetExtension(e.FullPath);
                string checkExt = Properties.Settings.Default.CheckExt.Replace("*", "");
                string[] checkExts = checkExt.Split(",", StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < checkExts.Length; i++)
                {
                    checkExts[i] = checkExts[i].Trim();
                }

                // Created 以外は即 return し、watch event queue へ流す対象だけに絞る。
                if (e.ChangeType != WatcherChangeTypes.Created
                    || !checkExts.Contains(ext, StringComparer.OrdinalIgnoreCase))
                {
                    return;
                }

                _ = QueueWatchEventAsync(
                    new WatchEventRequest(WatchEventKind.Created, e.FullPath, ""),
                    "watch-created"
                );
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine($"FileChangedで例外発生: {ex.Message}");
#endif
                DebugRuntimeLog.Write(
                    "watch",
                    $"watch event enqueue failed(created): {ex.GetType().Name}: {ex.Message}"
                );
            }
        }

        /// <summary>
        /// 「ファイル名が変わった！」と報告が入ったら、DBもサムネイルも全員まとめて追従改名させる怒涛の連鎖処理！🏃‍♂️💨
        /// </summary>
        private void FileRenamed(object sender, RenamedEventArgs e)
        {
            var ext = Path.GetExtension(e.FullPath);
            string checkExt = Properties.Settings.Default.CheckExt.Replace("*", "");
            string[] checkExts = checkExt.Split(",", StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < checkExts.Length; i++)
            {
                checkExts[i] = checkExts[i].Trim();
            }
            var eFullPath = e.FullPath;
            var oldFullPath = e.OldFullPath;

            if (checkExts.Contains(ext, StringComparer.OrdinalIgnoreCase))
            {
#if DEBUG
                string s = string.Format($"{DateTime.Now:yyyy/MM/dd HH:mm:ss} :");
                s += $"【{e.ChangeType}】{e.OldName} → {e.FullPath}";
                Debug.WriteLine(s);
#endif
                _ = QueueWatchEventAsync(
                    new WatchEventRequest(WatchEventKind.Renamed, eFullPath, oldFullPath),
                    "watch-renamed"
                );
            }
        }

        /// <summary>
        /// 指定されたフォルダにFileSystemWatcher（監視カメラ）をガッチリ仕掛ける番人の儀式！👁️
        /// </summary>
        private void RunWatcher(string watchFolder, bool sub)
        {
            if (!Path.Exists(watchFolder))
            {
                DebugRuntimeLog.Write("watch", $"skip watcher: folder not found '{watchFolder}'");
                return;
            }

            FileSystemWatcher item = new()
            {
                Path = watchFolder,
                Filter = "",
                NotifyFilter =
                    NotifyFilters.LastAccess
                    | NotifyFilters.LastWrite
                    | NotifyFilters.FileName
                    | NotifyFilters.DirectoryName,
                IncludeSubdirectories = sub,
                InternalBufferSize = 1024 * 32,
            };

            item.Changed += new FileSystemEventHandler(FileChanged);
            item.Created += new FileSystemEventHandler(FileChanged);
            item.Renamed += new RenamedEventHandler(FileRenamed);
            item.EnableRaisingEvents = true;

            fileWatchers.Add(item);
            DebugRuntimeLog.Write("watch", $"watcher started: folder='{watchFolder}' sub={sub}");
        }

        /// <summary>
        /// DBに眠るすべての監視フォルダ設定を呼び覚まし、各地にFileSystemWatcher部隊を一斉配備する開幕の合図だ！📢
        /// </summary>
        private void CreateWatcher()
        {
            Stopwatch sw = Stopwatch.StartNew();
            int watcherCount = 0;
            int skippedByEverythingOnlyCount = 0;
            DebugRuntimeLog.TaskStart(nameof(CreateWatcher), $"db='{MainVM.DbInfo.DBFullPath}'");
            IntegrationMode integrationMode = GetEverythingIntegrationMode();
            AvailabilityResult availability = _indexProviderFacade.CheckAvailability(integrationMode);
            string availabilityCategory = FileIndexReasonTable.ToCategory(availability.Reason);
            string availabilityAxis = FileIndexReasonTable.ToLogAxis(availability.Reason);

            string sql = $"SELECT * FROM watch where watch = 1";
            GetWatchTable(MainVM.DbInfo.DBFullPath, sql);
            if (watchData == null)
            {
                DebugRuntimeLog.Write(
                    "watch",
                    $"watcher create canceled: watch table load failed. db='{MainVM.DbInfo.DBFullPath}'"
                );
                return;
            }

            foreach (DataRow row in watchData.Rows)
            {
                if (!Path.Exists(row["dir"].ToString()))
                {
                    continue;
                }
                string checkFolder = row["dir"].ToString();
                bool sub = (long)row["sub"] == 1;

                string watcherDecisionReason;
                if (
                    ShouldSkipFileSystemWatcherByEverything(
                        checkFolder,
                        integrationMode,
                        availability,
                        out watcherDecisionReason
                    )
                )
                {
                    skippedByEverythingOnlyCount++;
                    DebugRuntimeLog.Write(
                        "watch",
                        $"watcher skipped by everything-only: category={availabilityAxis} folder='{checkFolder}' reason_category={availabilityCategory} reason={watcherDecisionReason}"
                    );
                    continue;
                }

                if (integrationMode == IntegrationMode.On)
                {
                    DebugRuntimeLog.Write(
                        "watch",
                        $"watcher keep: category={availabilityAxis} folder='{checkFolder}' reason_category={availabilityCategory} reason={watcherDecisionReason}"
                    );
                }
                RunWatcher(checkFolder, sub);
                watcherCount++;
            }

            sw.Stop();
            DebugRuntimeLog.TaskEnd(
                nameof(CreateWatcher),
                $"count={watcherCount} skipped={skippedByEverythingOnlyCount} mode={integrationMode} availability_axis={availabilityAxis} availability_category={availabilityCategory} availability={availability.Reason} elapsed_ms={sw.ElapsedMilliseconds}"
            );
        }

        // Everything専用監視を有効にできる条件を満たす場合、FileSystemWatcher作成をスキップする。
        private static bool ShouldSkipFileSystemWatcherByEverything(
            string watchFolder,
            IntegrationMode mode,
            AvailabilityResult availability,
            out string reason
        )
        {
            if (mode != IntegrationMode.On)
            {
                reason = "mode_not_on";
                return false;
            }

            if (!availability.CanUse)
            {
                reason = $"everything_unavailable:{availability.Reason}";
                return false;
            }

            if (!IsEverythingEligiblePath(watchFolder, out string eligibilityReason))
            {
                reason = $"{EverythingReasonCodes.PathNotEligiblePrefix}{eligibilityReason}";
                return false;
            }

            reason = "everything_only_enabled";
            return true;
        }
    }
}
