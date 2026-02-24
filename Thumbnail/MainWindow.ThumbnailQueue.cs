using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.QueueDb;
using IndigoMovieManager.Thumbnail.QueuePipeline;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // 同一ジョブの短時間連打を抑止するデバウンス窓（ミリ秒）。
        private const int ThumbnailQueueDebounceWindowMs = 800;
        // 3GB超コピー確認が必要なジョブを、通常キューとは分離して後回し管理する。
        private static readonly ConcurrentDictionary<string, DeferredLargeCopyJob> deferredLargeCopyJobs = new();
        // 直近投入時刻をキー単位で保持し、FileSystemWatcher連打の膨張を抑える。
        private static readonly ConcurrentDictionary<string, DateTime> recentEnqueueByKeyUtc = new();
        // Consumerが使うQueueDBサービスを現在MainDBに追従させるためのキャッシュ。
        private readonly object queueDbServiceLock = new();
        private QueueDbService currentQueueDbService;
        private string currentQueueDbMainDbFullPath = "";
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

        // QueueObjを安全に再投入できるよう、必要な値だけコピーしたインスタンスを作る。
        private static QueueObj CloneQueueObj(QueueObj source)
        {
            return new QueueObj
            {
                MovieId = source.MovieId,
                MovieFullPath = source.MovieFullPath,
                Tabindex = source.Tabindex,
                ThumbPanelPos = source.ThumbPanelPos,
                ThumbTimePos = source.ThumbTimePos
            };
        }

        // キューへジョブを追加する。重複抑止はQueueDBの一意制約に委譲する。
        private bool TryEnqueueThumbnailJob(QueueObj queueObj)
        {
            if (queueObj == null) { return false; }
            if (!isThumbnailQueueInputEnabled)
            {
                DebugRuntimeLog.Write("queue", "enqueue skipped: input disabled.");
                return false;
            }

            string key = GetThumbnailJobKey(queueObj);
            if (!TryReserveDebounceWindow(queueObj, key))
            {
                DebugRuntimeLog.Write("queue", $"enqueue skipped debounced: key={key}");
                return false;
            }

            // Producerとして、まずQueueDB永続化要求をChannelへ渡す。
            // 重複抑止はQueueDBの一意制約に寄せ、メモリ内予約は持たない。
            if (!TryWriteQueueRequest(queueObj))
            {
                return false;
            }

            long enqueueTotal = ThumbnailQueueMetrics.RecordEnqueueAccepted();
            if (enqueueTotal <= 20 || enqueueTotal % 100 == 0)
            {
                DebugRuntimeLog.Write(
                    "queue",
                    $"enqueue accepted: path='{queueObj.MovieFullPath}' tab={queueObj.Tabindex} total={enqueueTotal}");
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

            QueueRequest request = QueueRequest.FromQueueObj(mainDbFullPath, queueObj);
            bool accepted = queueRequestChannel.Writer.TryWrite(request);
            if (!accepted)
            {
                DebugRuntimeLog.Write("queue-db", $"channel write failed: path='{queueObj.MovieFullPath}' tab={queueObj.Tabindex}");
            }
            return accepted;
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

            lock (queueDbServiceLock)
            {
                if (currentQueueDbService != null &&
                    string.Equals(
                        currentQueueDbMainDbFullPath,
                        mainDbFullPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return currentQueueDbService;
                }

                currentQueueDbService = new QueueDbService(mainDbFullPath);
                currentQueueDbMainDbFullPath = mainDbFullPath;
                DebugRuntimeLog.Write("queue-db", $"consumer db switched: main_db='{mainDbFullPath}'");
                return currentQueueDbService;
            }
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

        // 3GB超コピーが必要なジョブを後回し登録する。
        // キューが空いたタイミングでまとめて確認ダイアログを出す。
        private void RegisterDeferredLargeCopyJob(QueueObj queueObj, long? copySizeBytes)
        {
            if (queueObj == null) { return; }
            string key = GetThumbnailJobKey(queueObj);
            deferredLargeCopyJobs[key] = new DeferredLargeCopyJob(CloneQueueObj(queueObj), copySizeBytes ?? 0);
        }

        // 通常ジョブが尽きたタイミングで、後回しジョブの実行確認を出す。
        // 承認されたものだけ再投入し、拒否されたものは今回見送る。
        private async Task ProcessDeferredLargeCopyJobsAsync(CancellationToken cts)
        {
            if (deferredLargeCopyJobs.IsEmpty) { return; }
            if (!isThumbnailQueueInputEnabled) { return; }

            foreach (var pair in deferredLargeCopyJobs)
            {
                cts.ThrowIfCancellationRequested();
                if (!deferredLargeCopyJobs.TryRemove(pair.Key, out DeferredLargeCopyJob job))
                {
                    continue;
                }

                DeferredLargeCopyDecision approved = await Dispatcher.InvokeAsync(
                    () => ConfirmDeferredLargeCopyJob(job),
                    DispatcherPriority.Normal,
                    cts);

                if (approved == DeferredLargeCopyDecision.Deny)
                {
                    Debug.WriteLine($"thumb deferred large-copy skipped by user: '{job.QueueObj.MovieFullPath}'");
                    continue;
                }

                // 「常に許可」のときだけ同一パスを次回以降も確認なしで通す。
                if (approved == DeferredLargeCopyDecision.AllowAlways)
                {
                    ThumbnailCreationService.SetLargeCopyApproval(job.QueueObj.MovieFullPath, true);
                }
                TryEnqueueThumbnailJob(job.QueueObj);
            }
        }

        // 後回しジョブの実行可否を確認するダイアログ。
        private DeferredLargeCopyDecision ConfirmDeferredLargeCopyJob(DeferredLargeCopyJob job)
        {
            double sizeGb = job.CopySizeBytes / (1024d * 1024d * 1024d);
            string message =
                "絵文字パス回避のため一時コピーが必要です。" + Environment.NewLine +
                $"対象: {job.QueueObj.MovieFullPath}" + Environment.NewLine +
                $"推定コピーサイズ: {sizeGb:F2} GB" + Environment.NewLine +
                Environment.NewLine +
                "通常ジョブは先に完了しています。実行方法を選んでください。";

            // 既存のMessageBoxExを使い、OK時はラジオ選択で「今回のみ/常に」を分岐する。
            var dialogWindow = new MessageBoxEx(this)
            {
                DlogTitle = "大容量コピー確認",
                DlogMessage = message,
                PackIconKind = MaterialDesignThemes.Wpf.PackIconKind.HelpCircleOutline,
                UseRadioButton = true,
                Radio1Content = "この回だけ許可",
                Radio2Content = "常に許可",
                Radio1IsChecked = true,
                Radio2IsChecked = false
            };

            dialogWindow.ShowDialog();
            if (dialogWindow.CloseStatus() == MessageBoxResult.Cancel)
            {
                return DeferredLargeCopyDecision.Deny;
            }

            return dialogWindow.Radio2IsChecked
                ? DeferredLargeCopyDecision.AllowAlways
                : DeferredLargeCopyDecision.AllowOnce;
        }

        // 後回しジョブの管理状態を初期化する。
        private void ClearThumbnailQueue()
        {
            deferredLargeCopyJobs.Clear();
            recentEnqueueByKeyUtc.Clear();
        }

        private sealed class DeferredLargeCopyJob
        {
            public DeferredLargeCopyJob(QueueObj queueObj, long copySizeBytes)
            {
                QueueObj = queueObj;
                CopySizeBytes = copySizeBytes;
            }

            public QueueObj QueueObj { get; }
            public long CopySizeBytes { get; }
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
