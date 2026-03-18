using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.QueueDb;
using IndigoMovieManager.Thumbnail.QueuePipeline;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        internal enum ThumbnailQueueClearScope
        {
            DebounceOnly,
            FullReset,
        }

        // 同一ジョブの短時間連打を抑止するデバウンス窓（ミリ秒）。
        private const int ThumbnailQueueDebounceWindowMs = 800;
        // 直近投入時刻をキー単位で保持し、FileSystemWatcher連打の膨張を抑える。
        private static readonly ConcurrentDictionary<string, DateTime> recentEnqueueByKeyUtc = new();
        // Consumerが使うQueueDBサービスを現在MainDBに追従させるためのキャッシュ。
        private readonly object queueDbServiceLock = new();
        private readonly object queueDbMaintenanceLock = new();
        private QueueDbService currentQueueDbService;
        private string currentQueueDbMainDbFullPath = "";
        private DateTime doneCleanupLastLocalDate = DateTime.MinValue;
        private string doneCleanupMainDbFullPath = "";
        // DB切り替え成功ごとに進める印。古い印のQueueRequestはpersisterで破棄する。
        private long currentMainDbQueueRequestSessionStamp = 1;
        // 終了シーケンスで入力停止するためのフラグ。
        private volatile bool isThumbnailQueueInputEnabled = true;
        // DBリース所有者。アプリ起動中は固定し、UpdateStatusの所有者一致判定に使う。
        private readonly string thumbnailQueueOwnerInstanceId =
            $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

        // サムネイルジョブのユニークキーを生成する。

        private static string GetThumbnailJobKey(QueueObj queueObj)
        {
            string moviePathKey = QueueDbPathResolver.CreateMoviePathKey(queueObj?.MovieFullPath ?? "");
            return $"{moviePathKey}:{queueObj?.Tabindex}";
        }

        // 上側タブの通常サムネは 0..4 を正式な対象として扱う。
        private static bool IsUpperThumbnailTabIndex(int tabIndex)
        {
            return tabIndex >= 0 && tabIndex <= 4;
        }

        // QueueObjを安全に再投入できるよう、必要な値だけコピーしたインスタンスを作る。
        private static QueueObj CloneQueueObj(QueueObj source)
        {
            return new QueueObj
            {
                MovieId = source.MovieId,
                MovieFullPath = source.MovieFullPath,
                Hash = source.Hash,
                MovieSizeBytes = source.MovieSizeBytes,
                Tabindex = source.Tabindex,
                ThumbPanelPos = source.ThumbPanelPos,
                ThumbTimePos = source.ThumbTimePos,
                Priority = source.Priority,
            };
        }

        // キューへジョブを追加する。重複抑止はQueueDBの一意制約に委譲する。
        private bool TryEnqueueThumbnailJob(QueueObj queueObj, bool bypassDebounce = false)
        {
            if (queueObj == null) { return false; }
            if (!isThumbnailQueueInputEnabled)
            {
                DebugRuntimeLog.Write("queue", "enqueue skipped: input disabled.");
                return false;
            }
            if (!ShouldAcceptThumbnailQueueRequest(queueObj))
            {
                DebugRuntimeLog.Write(
                    "queue",
                    $"enqueue skipped by tab gate: path='{queueObj.MovieFullPath}' tab={queueObj.Tabindex} current_tab={MainVM?.DbInfo?.CurrentTabIndex ?? -1}"
                );
                return false;
            }
            bool hasFileLength = TryGetMovieFileLength(queueObj.MovieFullPath, out long fileLength);
            if (hasFileLength)
            {
                // 投入時点でサイズが取れる場合はQueueObjへ保持して、後段処理で再利用する。
                queueObj.MovieSizeBytes = fileLength;
            }
            else if (queueObj.MovieSizeBytes < 0)
            {
                queueObj.MovieSizeBytes = 0;
            }

            if (hasFileLength && fileLength <= 0)
            {
                // 0KBを除外した時点でエラーマーカーを置き、次回スキャンで無限再投入されるのを防ぐ。
                TryCreateErrorMarkerForSkippedMovie(
                    queueObj.MovieFullPath,
                    queueObj.Tabindex,
                    "zero-byte movie"
                );
                DebugRuntimeLog.Write(
                    "queue",
                    $"enqueue skipped zero-byte movie: path='{queueObj.MovieFullPath}' size={fileLength}"
                );
                return false;
            }

            string key = GetThumbnailJobKey(queueObj);
            if (!bypassDebounce && !TryReserveDebounceWindow(queueObj, key))
            {
                DebugRuntimeLog.Write(
                    "queue",
                    $"enqueue skipped debounced: key={key} priority={queueObj.Priority}"
                );
                return false;
            }

            // Producerとして、まずQueueDB永続化要求をChannelへ渡す。
            // 重複抑止はQueueDBの一意制約に寄せ、メモリ内予約は持たない。
            if (!TryWriteQueueRequest(queueObj))
            {
                return false;
            }

            RequestThumbnailProgressSnapshotRefresh();

            long enqueueTotal = ThumbnailQueueMetrics.RecordEnqueueAccepted();
            if (enqueueTotal <= 20 || enqueueTotal % 100 == 0)
            {
                DebugRuntimeLog.Write(
                    "queue",
                    $"enqueue accepted: path='{queueObj.MovieFullPath}' tab={queueObj.Tabindex} priority={queueObj.Priority} total={enqueueTotal}");
            }
            return true;
        }

        // 監視イベントの重複連打を短時間で吸収し、Channel膨張を抑える。
        // 手動キャプチャ（パネル/秒位置あり）は意図した更新なので抑止しない。
        private static bool TryReserveDebounceWindow(QueueObj queueObj, string key)
        {
            if (queueObj?.ThumbPanelPos.HasValue == true || queueObj?.ThumbTimePos.HasValue == true)
            {
                return true;
            }

            DateTime nowUtc = DateTime.UtcNow;
            if (queueObj != null && ThumbnailQueuePriorityHelper.IsPreferred(queueObj.Priority))
            {
                // 優先要求は昇格要求として扱い、debounceでは落とさず時刻だけ更新する。
                recentEnqueueByKeyUtc.AddOrUpdate(key, nowUtc, (_, _) => nowUtc);
                return true;
            }

            while (true)
            {
                if (!recentEnqueueByKeyUtc.TryGetValue(key, out DateTime lastUtc))
                {
                    if (recentEnqueueByKeyUtc.TryAdd(key, nowUtc))
                    {
                        return true;
                    }
                    continue;
                }

                if ((nowUtc - lastUtc).TotalMilliseconds < ThumbnailQueueDebounceWindowMs)
                {
                    return false;
                }

                if (recentEnqueueByKeyUtc.TryUpdate(key, nowUtc, lastUtc))
                {
                    return true;
                }
            }
        }

        // QueueObjをQueueRequestへ変換し、Persisterへ非同期引き渡しする。
        // Watcher/D&Dなどの呼び出し側は、このTryWriteだけで即リターンできる。
        private bool TryWriteQueueRequest(QueueObj queueObj)
        {
            string mainDbFullPath = MainVM?.DbInfo?.DBFullPath ?? "";
            if (string.IsNullOrWhiteSpace(mainDbFullPath))
            {
                // 永続化先が未確定な状態では投入を許可しない。
                // 「実行だけ成功して再起動復元できない」状態を避けるため失敗扱いにする。
                DebugRuntimeLog.Write("queue-db", "enqueue rejected: main db is empty.");
                return false;
            }

            QueueRequest request = QueueRequest.FromQueueObj(
                mainDbFullPath,
                ReadCurrentMainDbQueueRequestSessionStamp(),
                queueObj
            );
            bool accepted = queueRequestChannel.Writer.TryWrite(request);
            if (!accepted)
            {
                DebugRuntimeLog.Write("queue-db", $"channel write failed: path='{queueObj.MovieFullPath}' tab={queueObj.Tabindex}");
            }
            return accepted;
        }

        // 現在有効なMainDBセッション印を返す。
        private long ReadCurrentMainDbQueueRequestSessionStamp()
        {
            return Interlocked.Read(ref currentMainDbQueueRequestSessionStamp);
        }

        // DB切り替え成功後だけ印を進め、切替前のQueueRequestをstale扱いにする。
        private long AdvanceCurrentMainDbQueueRequestSessionStamp()
        {
            return Interlocked.Increment(ref currentMainDbQueueRequestSessionStamp);
        }

        // persister側が古いセッション印を捨てるための判定。
        internal static bool IsQueueRequestAcceptedForSession(
            QueueRequest request,
            long currentSessionStamp
        )
        {
            if (request == null || currentSessionStamp < 1)
            {
                return false;
            }

            return request.MainDbSessionStamp == currentSessionStamp;
        }

        // ファイル実体が取れるときだけサイズを返す。存在しない・読めない場合は false を返す。
        private static bool TryGetMovieFileLength(string movieFullPath, out long fileLength)
        {
            fileLength = -1;
            if (string.IsNullOrWhiteSpace(movieFullPath))
            {
                return false;
            }

            try
            {
                // Exists -> Length の二度呼びを避け、属性取得は1回だけに寄せる。
                fileLength = new FileInfo(movieFullPath).Length;
                return true;
            }
            catch
            {
                return false;
            }
        }

        // 0KB動画はサムネイル生成しても失敗確率が高いため、キュー投入前に除外する。
        private static bool IsZeroByteMovieFile(string movieFullPath, out long fileLength)
        {
            return TryGetMovieFileLength(movieFullPath, out fileLength) && fileLength <= 0;
        }

        // 既に正常jpgがある個体へERRORマーカーを再付与すると、Gridが古い失敗画像を拾うため抑止する。
        internal static bool ShouldCreateErrorMarkerForSkippedMovie(
            string thumbOutPath,
            string movieFullPath,
            out string existingSuccessThumbnailPath
        )
        {
            existingSuccessThumbnailPath = "";
            if (string.IsNullOrWhiteSpace(thumbOutPath) || string.IsNullOrWhiteSpace(movieFullPath))
            {
                return true;
            }

            return !ThumbnailPathResolver.TryFindExistingSuccessThumbnailPath(
                thumbOutPath,
                movieFullPath,
                out existingSuccessThumbnailPath
            );
        }

        // 除外対象の動画に対して、再スキャン無限ループ防止用のERRORマーカーを作成する。
        private void TryCreateErrorMarkerForSkippedMovie(
            string movieFullPath,
            int tabIndex,
            string reason
        )
        {
            if (string.IsNullOrWhiteSpace(movieFullPath))
            {
                return;
            }

            try
            {
                string thumbFolder = MainVM?.DbInfo?.ThumbFolder ?? "";
                string dbName = MainVM?.DbInfo?.DBName ?? "";
                if (string.IsNullOrWhiteSpace(thumbFolder))
                {
                    return;
                }

                string thumbOutPath = ResolveThumbnailOutPath(tabIndex, dbName, thumbFolder);
                if (string.IsNullOrWhiteSpace(thumbOutPath))
                {
                    return;
                }

                Directory.CreateDirectory(thumbOutPath);
                string errorMarkerPath = ThumbnailPathResolver.BuildErrorMarkerPath(
                    thumbOutPath,
                    movieFullPath
                );
                if (
                    !ShouldCreateErrorMarkerForSkippedMovie(
                        thumbOutPath,
                        movieFullPath,
                        out string existingSuccessThumbnailPath
                    )
                )
                {
                    if (Path.Exists(errorMarkerPath))
                    {
                        File.Delete(errorMarkerPath);
                        DebugRuntimeLog.Write(
                            "thumbnail",
                            $"error marker deleted by precheck: '{errorMarkerPath}', success='{existingSuccessThumbnailPath}'"
                        );
                    }

                    DebugRuntimeLog.Write(
                        "thumbnail",
                        $"error marker skipped by precheck: movie='{movieFullPath}', reason='{reason}', success='{existingSuccessThumbnailPath}'"
                    );
                    return;
                }

                if (!Path.Exists(errorMarkerPath))
                {
                    File.WriteAllBytes(errorMarkerPath, []);
                    DebugRuntimeLog.Write(
                        "thumbnail",
                        $"error marker created by precheck: '{errorMarkerPath}', reason='{reason}'"
                    );
                }
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "thumbnail",
                    $"error marker write failed by precheck: movie='{movieFullPath}', reason='{reason}', err='{ex.Message}'"
                );
            }
        }

        // 通常Queueは「今見ている上側タブ」だけを受け付け、他タブ分の自動投入を止める。
        private bool ShouldAcceptThumbnailQueueRequest(QueueObj queueObj)
        {
            if (queueObj == null)
            {
                return false;
            }

            if (queueObj.Tabindex == ExtensionDetailThumbnailTabIndex)
            {
                return true;
            }

            if (!IsUpperThumbnailTabIndex(queueObj.Tabindex))
            {
                return false;
            }

            int currentTabIndex = MainVM?.DbInfo?.CurrentTabIndex ?? -1;
            return currentTabIndex == queueObj.Tabindex;
        }

        // タブ切替時は未選択上側タブの pending を落とし、古い見た目の残ジョブを掃除する。
        private void TryDeletePendingUpperTabJobsForUnselectedTabs(int currentTabIndex)
        {
            QueueDbService queueDbService = ResolveCurrentQueueDbService();
            if (queueDbService == null)
            {
                return;
            }

            int? selectedUpperTabIndex = IsUpperThumbnailTabIndex(currentTabIndex)
                ? currentTabIndex
                : null;
            int deleted = queueDbService.DeletePendingUpperTabsExcept(selectedUpperTabIndex);
            if (deleted > 0)
            {
                DebugRuntimeLog.Write(
                    "queue-ops",
                    $"pending upper-tab cleanup: current_tab={currentTabIndex} deleted={deleted}"
                );
            }
        }

        // 終了時は先に入力を止めて、キャンセル中の新規投入を抑止する。
        private void SetThumbnailQueueInputEnabled(bool enabled)
        {
            isThumbnailQueueInputEnabled = enabled;
            DebugRuntimeLog.Write("queue", $"input enabled={enabled}");
        }

        // 現在開いているMainDBに対応するQueueDbServiceを返す。
        // DB未選択時はnullを返し、Consumer側で待機させる。
        private QueueDbService ResolveCurrentQueueDbService()
        {
            string mainDbFullPath = MainVM?.DbInfo?.DBFullPath ?? "";
            if (string.IsNullOrWhiteSpace(mainDbFullPath))
            {
                return null;
            }

            QueueDbService queueDbService;
            lock (queueDbServiceLock)
            {
                if (currentQueueDbService != null &&
                    string.Equals(
                        currentQueueDbMainDbFullPath,
                        mainDbFullPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    queueDbService = currentQueueDbService;
                }
                else
                {
                currentQueueDbService = new QueueDbService(mainDbFullPath);
                currentQueueDbMainDbFullPath = mainDbFullPath;
                DebugRuntimeLog.Write("queue-db", $"consumer db switched: main_db='{mainDbFullPath}'");
                    queueDbService = currentQueueDbService;
            }
        }

            TryCleanupOldDoneQueueItems(queueDbService, mainDbFullPath);
            return queueDbService;
        }

        // 現在ユーザーが見ているタブ番号を返す。未選択時はnull。
        private int? ResolvePreferredThumbnailTabIndex()
        {
            int tabIndex = MainVM?.DbInfo?.CurrentTabIndex ?? -1;
            return tabIndex >= 0 ? tabIndex : null;
        }

        // 手動再試行用に、現在DBのFailedジョブをPendingへ戻す。
        // UIメニュー未接続のため、現時点では運用手順書に従って呼び出す前提。
        internal int ResetFailedThumbnailJobsForCurrentDb()
        {
            QueueDbService queueDbService = ResolveCurrentQueueDbService();
            if (queueDbService == null)
            {
                DebugRuntimeLog.Write("queue-ops", "manual retry skipped: current db is empty.");
                return 0;
            }

            int resetCount = queueDbService.ResetFailedToPending(DateTime.UtcNow);
            DebugRuntimeLog.Write("queue-ops", $"manual retry: reset_failed_to_pending={resetCount}");
            return resetCount;
        }

        // タブ切替ではデバウンスだけ消し、DB切替や再起動時だけ進捗Runtimeまで落とす。
        private void ClearThumbnailQueue(
            ThumbnailQueueClearScope scope = ThumbnailQueueClearScope.FullReset
        )
        {
            recentEnqueueByKeyUtc.Clear();

            if (!ShouldResetThumbnailProgressOnQueueClear(scope))
            {
                return;
            }

            _thumbnailProgressRuntime.Reset();
            ThumbnailPreviewCache.Shared.Clear();
            ThumbnailPreviewLatencyTracker.Reset();
            RequestThumbnailProgressSnapshotRefresh();
        }

        internal static bool ShouldResetThumbnailProgressOnQueueClear(
            ThumbnailQueueClearScope scope
        )
        {
            return scope == ThumbnailQueueClearScope.FullReset;
        }

        // QueueDBのDone履歴は当日分のみ保持し、前日以前を日次で削除する。
        private void TryCleanupOldDoneQueueItems(
            QueueDbService queueDbService,
            string mainDbFullPath
        )
        {
            if (queueDbService == null || string.IsNullOrWhiteSpace(mainDbFullPath))
            {
                return;
            }

            DateTime todayLocal = DateTime.Now.Date;
            lock (queueDbMaintenanceLock)
            {
                bool sameDb = string.Equals(
                    doneCleanupMainDbFullPath,
                    mainDbFullPath,
                    StringComparison.OrdinalIgnoreCase
                );
                if (sameDb && doneCleanupLastLocalDate == todayLocal)
                {
                    return;
                }

                try
                {
                    int deleted = queueDbService.DeleteDoneOlderThan(todayLocal);
                    DebugRuntimeLog.Write(
                        "queue-ops",
                        $"done retention cleanup: deleted={deleted} cutoff_local='{todayLocal:yyyy-MM-dd}' main_db='{mainDbFullPath}'"
                    );
                }
                catch (Exception ex)
                {
                    DebugRuntimeLog.Write(
                        "queue-ops",
                        $"done retention cleanup failed: cutoff_local='{todayLocal:yyyy-MM-dd}' main_db='{mainDbFullPath}' reason='{ex.Message}'"
                    );
                }

                doneCleanupMainDbFullPath = mainDbFullPath;
                doneCleanupLastLocalDate = todayLocal;
            }
        }
        // ユーザー確認の結果を明確化するための3状態。
        private enum DeferredLargeCopyDecision
        {
            Deny = 0,
            AllowOnce = 1,
            AllowAlways = 2,
        }
    }
}
