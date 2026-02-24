using IndigoMovieManager.Thumbnail;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // (MovieId, Tabindex) 単位で重複投入を抑止するキー管理。
        private static readonly ConcurrentDictionary<string, byte> queuedThumbnailKeys = new();
        // 3GB超コピー確認が必要なジョブを、通常キューとは分離して後回し管理する。
        private static readonly ConcurrentDictionary<string, DeferredLargeCopyJob> deferredLargeCopyJobs = new();

        // サムネイルジョブのユニークキーを生成する。
        private static string GetThumbnailJobKey(QueueObj queueObj)
        {
            return $"{queueObj.MovieId}:{queueObj.Tabindex}";
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

        // キューへジョブを追加する。既に同一キーがある場合は追加しない。
        private bool TryEnqueueThumbnailJob(QueueObj queueObj)
        {
            if (queueObj == null) { return false; }

            string key = GetThumbnailJobKey(queueObj);
            if (!queuedThumbnailKeys.TryAdd(key, 0))
            {
                return false;
            }

            queueThumb.Enqueue(queueObj);
            return true;
        }

        // 処理終了したジョブのキーを解放する。
        private void ReleaseThumbnailJob(QueueObj queueObj)
        {
            if (queueObj == null) { return; }
            string key = GetThumbnailJobKey(queueObj);
            queuedThumbnailKeys.TryRemove(key, out _);
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
            if (!queueThumb.IsEmpty) { return; }

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

        // キューと重複管理キーをまとめて初期化する。
        private void ClearThumbnailQueue()
        {
            while (queueThumb.TryDequeue(out _)) { }
            queuedThumbnailKeys.Clear();
            deferredLargeCopyJobs.Clear();
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
