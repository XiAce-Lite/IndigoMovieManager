using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.FailureDb;
using static IndigoMovieManager.DB.SQLite;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // 欠損サムネ救済は重い全件確認になるため、DB+タブ単位で最小間隔を設ける。
        private static readonly TimeSpan MissingThumbnailRescueMinInterval = TimeSpan.FromSeconds(60);
        // DB+タブ単位で、欠損サムネ救済を直近いつ実行したかを記録する。
        private readonly object _missingThumbnailRescueSync = new();
        private readonly Dictionary<string, DateTime> _missingThumbnailRescueLastRunUtcByScope =
            new(StringComparer.OrdinalIgnoreCase);

        // Watch差分では「動画更新なし + サムネ削除」の取りこぼしが起き得るため、低頻度で欠損救済を実行する。
        private async Task TryRunMissingThumbnailRescueAsync(
            CheckMode mode,
            string snapshotDbFullPath,
            string snapshotDbName,
            string snapshotThumbFolder,
            int snapshotTabIndex,
            long requestScopeStamp
        )
        {
            if (mode != CheckMode.Watch && mode != CheckMode.Manual)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(snapshotDbFullPath))
            {
                return;
            }

            if (snapshotTabIndex < 0)
            {
                return;
            }

            MissingThumbnailRescueGuardAction guardAction = GetMissingThumbnailRescueGuardAction(
                mode == CheckMode.Watch,
                snapshotDbFullPath,
                requestScopeStamp
            );
            if (guardAction == MissingThumbnailRescueGuardAction.DropStaleScope)
            {
                return;
            }

            if (guardAction == MissingThumbnailRescueGuardAction.DeferByUiSuppression)
            {
                MarkWatchWorkDeferredWhileSuppressed($"missing-thumb-rescue:{mode}");
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"skip missing-thumb rescue by suppression: mode={mode} db='{snapshotDbFullPath}'"
                );
                return;
            }

            // キュー高負荷中は救済スキャンを見送り、通常処理を優先する。
            if (TryGetCurrentQueueActiveCount(out int activeCount))
            {
                int rescueBusyThreshold = ResolveMissingThumbnailRescueBusyThreshold(
                    mode == CheckMode.Watch,
                    EverythingWatchPollBusyThreshold
                );
                if (
                    ShouldSkipMissingThumbnailRescueForBusyQueue(
                        mode == CheckMode.Manual,
                        activeCount,
                        rescueBusyThreshold
                    )
                )
                {
                    DebugRuntimeLog.Write(
                        "watch-check",
                        $"missing-thumb rescue skipped: queue busy active={activeCount} threshold={rescueBusyThreshold}"
                    );
                    return;
                }
            }

            DateTime nowUtc = DateTime.UtcNow;
            string scopeKey = BuildMissingThumbnailRescueScopeKey(snapshotDbFullPath, snapshotTabIndex);
            if (!TryReserveMissingThumbnailRescueWindow(scopeKey, nowUtc, out TimeSpan nextIn))
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"missing-thumb rescue throttled: tab={snapshotTabIndex} next_in_sec={Math.Ceiling(nextIn.TotalSeconds)}"
                );
                return;
            }

            Stopwatch rescueStopwatch = Stopwatch.StartNew();
            try
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"missing-thumb rescue start: mode={mode} tab={snapshotTabIndex} db='{snapshotDbFullPath}'"
                );
                await EnqueueMissingThumbnailsAsync(
                    snapshotTabIndex,
                    snapshotDbFullPath,
                    snapshotDbName,
                    snapshotThumbFolder,
                    mode == CheckMode.Watch,
                    requestScopeStamp
                );
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"missing-thumb rescue failed: mode={mode} tab={snapshotTabIndex} reason='{ex.Message}'"
                );
            }
            finally
            {
                rescueStopwatch.Stop();
                DebugRuntimeLog.Write(
                    "watch-check",
                    $"missing-thumb rescue end: mode={mode} tab={snapshotTabIndex} elapsed_ms={rescueStopwatch.ElapsedMilliseconds}"
                );
            }
        }

        // 同一スコープで短時間に救済処理を連打しないよう、最小実行間隔を適用する。
        private bool TryReserveMissingThumbnailRescueWindow(
            string scopeKey,
            DateTime nowUtc,
            out TimeSpan nextIn
        )
        {
            lock (_missingThumbnailRescueSync)
            {
                if (_missingThumbnailRescueLastRunUtcByScope.TryGetValue(scopeKey, out DateTime lastRunUtc))
                {
                    TimeSpan elapsed = nowUtc - lastRunUtc;
                    if (elapsed < MissingThumbnailRescueMinInterval)
                    {
                        nextIn = MissingThumbnailRescueMinInterval - elapsed;
                        return false;
                    }
                }

                _missingThumbnailRescueLastRunUtcByScope[scopeKey] = nowUtc;
                nextIn = TimeSpan.Zero;

                // スコープキーの肥大化を防ぐため、古い記録は定期的に掃除する。
                if (_missingThumbnailRescueLastRunUtcByScope.Count > 128)
                {
                    DateTime cutoff = nowUtc - TimeSpan.FromHours(24);
                    List<string> staleKeys = _missingThumbnailRescueLastRunUtcByScope
                        .Where(x => x.Value < cutoff)
                        .Select(x => x.Key)
                        .ToList();
                    foreach (string staleKey in staleKeys)
                    {
                        _missingThumbnailRescueLastRunUtcByScope.Remove(staleKey);
                    }
                }

                return true;
            }
        }

        /// <summary>
        /// 既存のDBに存在する動画のうち、指定タブのサムネイル欠損を探し、再生成キューへ投入する。
        /// </summary>
        public async Task EnqueueMissingThumbnailsAsync(
            int targetTabIndex,
            string snapshotDbFullPath,
            string snapshotDbName,
            string snapshotThumbFolder
        )
        {
            await EnqueueMissingThumbnailsAsync(
                targetTabIndex,
                snapshotDbFullPath,
                snapshotDbName,
                snapshotThumbFolder,
                isWatchMode: false,
                requestScopeStamp: 0
            );
        }

        private async Task EnqueueMissingThumbnailsAsync(
            int targetTabIndex,
            string snapshotDbFullPath,
            string snapshotDbName,
            string snapshotThumbFolder,
            bool isWatchMode,
            long requestScopeStamp
        )
        {
            if (string.IsNullOrWhiteSpace(snapshotDbFullPath))
            {
                return;
            }

            MissingThumbnailRescueGuardAction guardAction = GetMissingThumbnailRescueGuardAction(
                isWatchMode,
                snapshotDbFullPath,
                requestScopeStamp
            );
            if (guardAction == MissingThumbnailRescueGuardAction.DropStaleScope)
            {
                return;
            }

            if (guardAction == MissingThumbnailRescueGuardAction.DeferByUiSuppression)
            {
                MarkWatchWorkDeferredWhileSuppressed("missing-thumb-rescue:enqueue");
                return;
            }

            string thumbnailOutPath = ResolveThumbnailOutPath(
                targetTabIndex,
                snapshotDbName,
                snapshotThumbFolder
            );
            if (string.IsNullOrWhiteSpace(thumbnailOutPath))
            {
                return;
            }

            DataTable dt = GetData(
                snapshotDbFullPath,
                "SELECT movie_id, movie_path, hash FROM movie ORDER BY movie_id DESC"
            );
            if (dt == null || dt.Rows.Count == 0)
            {
                return;
            }

            int enqueuedCount = 0;
            List<QueueObj> batch = [];
            HashSet<string> existingThumbnailFileNames = await Task.Run(() =>
                BuildThumbnailFileNameLookup(thumbnailOutPath)
            );
            ThumbnailFailureDbService failureDbService = ResolveCurrentThumbnailFailureDbService();
            HashSet<string> openRescueRequestKeys =
                failureDbService?.GetOpenRescueRequestKeys()
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            DebugRuntimeLog.Write(
                "rescue-thumb",
                $"start rescue missing thumbs for tab={targetTabIndex}. total docs={dt.Rows.Count}"
            );

            foreach (DataRow row in dt.Rows)
            {
                long.TryParse(row["movie_id"]?.ToString(), out long movieId);
                string path = row["movie_path"]?.ToString() ?? "";
                string hash = row["hash"]?.ToString() ?? "";

                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                string fileBody = Path.GetFileNameWithoutExtension(path);
                if (string.IsNullOrWhiteSpace(fileBody))
                {
                    continue;
                }

                string expectedThumbFileName = ThumbnailPathResolver.BuildThumbnailFileName(path, hash);
                if (!existingThumbnailFileNames.Contains(expectedThumbFileName))
                {
                    MissingThumbnailAutoEnqueueBlockReason blockReason =
                        ResolveMissingThumbnailAutoEnqueueBlockReason(
                            path,
                            targetTabIndex,
                            existingThumbnailFileNames,
                            openRescueRequestKeys
                        );
                    if (blockReason != MissingThumbnailAutoEnqueueBlockReason.None)
                    {
                        DebugRuntimeLog.Write(
                            "rescue-thumb",
                            $"skip enqueue by failure-state: tab={targetTabIndex}, movie='{path}', reason={DescribeMissingThumbnailAutoEnqueueBlockReason(blockReason)}"
                        );
                        continue;
                    }

                    if (TryGetMovieFileLength(path, out _))
                    {
                        batch.Add(
                            new QueueObj
                            {
                                MovieId = movieId,
                                MovieFullPath = path,
                                Hash = hash,
                                Tabindex = targetTabIndex,
                                Priority = ThumbnailQueuePriority.Normal,
                            }
                        );

                        enqueuedCount++;
                        existingThumbnailFileNames.Add(expectedThumbFileName);
                        DebugRuntimeLog.Write(
                            "rescue-thumb",
                            $"enqueue by rescue: tab={targetTabIndex}, movie='{path}'"
                        );

                        if (batch.Count >= FolderScanEnqueueBatchSize)
                        {
                            guardAction = GetMissingThumbnailRescueGuardAction(
                                isWatchMode,
                                snapshotDbFullPath,
                                requestScopeStamp
                            );
                            if (guardAction == MissingThumbnailRescueGuardAction.DropStaleScope)
                            {
                                return;
                            }

                            if (
                                guardAction
                                == MissingThumbnailRescueGuardAction.DeferByUiSuppression
                            )
                            {
                                MarkWatchWorkDeferredWhileSuppressed(
                                    "missing-thumb-rescue:flush"
                                );
                                return;
                            }

                            FlushPendingQueueItems(batch, "RescueMissingThumbnails");
                            await Task.Delay(50);
                        }
                    }
                }
            }

            if (batch.Count > 0)
            {
                guardAction = GetMissingThumbnailRescueGuardAction(
                    isWatchMode,
                    snapshotDbFullPath,
                    requestScopeStamp
                );
                if (guardAction == MissingThumbnailRescueGuardAction.DropStaleScope)
                {
                    return;
                }

                if (guardAction == MissingThumbnailRescueGuardAction.DeferByUiSuppression)
                {
                    MarkWatchWorkDeferredWhileSuppressed("missing-thumb-rescue:flush-final");
                    return;
                }

                FlushPendingQueueItems(batch, "RescueMissingThumbnails");
            }

            DebugRuntimeLog.Write(
                "rescue-thumb",
                $"finished rescue missing thumbs for tab={targetTabIndex}. enqueued={enqueuedCount}"
            );
        }
    }
}
