using System.IO;
using IndigoMovieManager.Thumbnail.QueueDb;
using IndigoMovieManager.Thumbnail.QueuePipeline;

namespace IndigoMovieManager.Thumbnail
{
    // サムネイル進捗タブ向けに、実行中の状態を軽量に保持する。
    public sealed class ThumbnailProgressRuntime
    {
        private const int MaxEnqueueLogCount = 10;
        private const int MovieNameHeadLength = 17;
        private const int MaxRetainedWorkerPanelCount = 48;

        private readonly object stateLock = new();
        private readonly Queue<string> enqueueLogs = new();
        private readonly Dictionary<string, WorkerState> activeWorkers = new(
            StringComparer.OrdinalIgnoreCase
        );

        private long workerSequence;
        private int sessionCompletedCount;
        private int sessionTotalCount;
        private int currentParallelism;
        private int configuredParallelism;

        public void Reset()
        {
            lock (stateLock)
            {
                enqueueLogs.Clear();
                activeWorkers.Clear();
                workerSequence = 0;
                sessionCompletedCount = 0;
                sessionTotalCount = 0;
                currentParallelism = 0;
                configuredParallelism = 0;
            }
        }

        // キュー投入ログは「動画名のみ」を最新N件で保持する。
        public void RecordEnqueue(QueueObj queueObj)
        {
            string movieName = Path.GetFileName(queueObj?.MovieFullPath ?? "");
            if (string.IsNullOrWhiteSpace(movieName))
            {
                return;
            }

            lock (stateLock)
            {
                enqueueLogs.Enqueue(movieName);
                while (enqueueLogs.Count > MaxEnqueueLogCount)
                {
                    _ = enqueueLogs.Dequeue();
                }
            }
        }

        public void UpdateSessionProgress(
            int completedCount,
            int totalCount,
            int currentParallel,
            int configuredParallel
        )
        {
            lock (stateLock)
            {
                sessionCompletedCount = Math.Max(0, completedCount);
                sessionTotalCount = Math.Max(sessionCompletedCount, Math.Max(0, totalCount));
                currentParallelism = Math.Max(0, currentParallel);
                configuredParallelism = Math.Max(0, configuredParallel);
            }
        }

        // ジョブ開始時に右サイド表示の作業パネルを追加/更新する。
        public void MarkJobStarted(QueueObj queueObj)
        {
            if (queueObj == null || string.IsNullOrWhiteSpace(queueObj.MovieFullPath))
            {
                return;
            }

            string key = CreateJobKey(queueObj);
            lock (stateLock)
            {
                WorkerState worker = AcquireWorkerForJob(key);

                worker.MoviePath = queueObj.MovieFullPath;
                worker.DisplayMovieName = ToDisplayMovieName(queueObj.MovieFullPath);
                worker.IsActive = true;
                worker.CompletedAtUtc = DateTime.MinValue;
            }
        }

        // サムネイル保存直後の画像パスを作業パネルへ反映する。
        public void MarkThumbnailSaved(QueueObj queueObj, string previewImagePath)
        {
            if (queueObj == null || string.IsNullOrWhiteSpace(previewImagePath))
            {
                return;
            }

            string key = CreateJobKey(queueObj);
            lock (stateLock)
            {
                WorkerState worker = AcquireWorkerForJob(key);

                worker.MoviePath = queueObj.MovieFullPath ?? "";
                worker.DisplayMovieName = ToDisplayMovieName(queueObj.MovieFullPath);
                worker.PreviewImagePath = previewImagePath;
                worker.IsActive = true;
                worker.CompletedAtUtc = DateTime.MinValue;
            }
        }

        // ジョブ完了時は即削除せず、完了状態で残して履歴として見えるようにする。
        public void MarkJobCompleted(QueueObj queueObj)
        {
            if (queueObj == null || string.IsNullOrWhiteSpace(queueObj.MovieFullPath))
            {
                return;
            }

            string key = CreateJobKey(queueObj);
            lock (stateLock)
            {
                if (!activeWorkers.TryGetValue(key, out WorkerState worker))
                {
                    return;
                }

                worker.IsActive = false;
                worker.CompletedAtUtc = DateTime.UtcNow;
                TrimCompletedWorkersIfNeeded();
            }
        }

        public ThumbnailProgressRuntimeSnapshot CreateSnapshot()
        {
            lock (stateLock)
            {
                IReadOnlyList<string> logSnapshot = [.. enqueueLogs];
                IEnumerable<WorkerState> active =
                    activeWorkers.Values.Where(x => x.IsActive).OrderBy(x => x.WorkerId);
                IEnumerable<WorkerState> completed =
                    activeWorkers
                        .Values.Where(x => !x.IsActive)
                        .OrderByDescending(x => x.CompletedAtUtc)
                        .ThenByDescending(x => x.WorkerId);
                IReadOnlyList<ThumbnailProgressWorkerSnapshot> workerSnapshot =
                [
                    .. active.Concat(completed).Select(
                        x =>
                            new ThumbnailProgressWorkerSnapshot
                            {
                                WorkerId = x.WorkerId,
                                WorkerLabel = $"Thread {x.WorkerId}",
                                DisplayMovieName = x.DisplayMovieName,
                                PreviewImagePath = x.PreviewImagePath,
                                IsActive = x.IsActive,
                            }
                    ),
                ];

                return new ThumbnailProgressRuntimeSnapshot
                {
                    SessionCompletedCount = sessionCompletedCount,
                    SessionTotalCount = sessionTotalCount,
                    CurrentParallelism = currentParallelism,
                    ConfiguredParallelism = configuredParallelism,
                    EnqueueLogs = logSnapshot,
                    ActiveWorkers = workerSnapshot,
                };
            }
        }

        private static string CreateJobKey(QueueObj queueObj)
        {
            string moviePathKey = QueueDbPathResolver.CreateMoviePathKey(
                queueObj?.MovieFullPath ?? ""
            );
            return $"{moviePathKey}:{queueObj?.Tabindex ?? -1}";
        }

        // まず同一ジョブキーを探し、なければ完了済みパネルを1つ再利用する。
        // 再利用時はPreviewImagePathを維持し、次ジョブのサムネが来るまで画像を見せ続ける。
        private WorkerState AcquireWorkerForJob(string key)
        {
            if (activeWorkers.TryGetValue(key, out WorkerState existing))
            {
                return existing;
            }

            string reusableKey =
                activeWorkers
                    .Where(x => !x.Value.IsActive)
                    .OrderByDescending(x => x.Value.CompletedAtUtc)
                    .ThenByDescending(x => x.Value.WorkerId)
                    .Select(x => x.Key)
                    .FirstOrDefault() ?? "";
            if (!string.IsNullOrWhiteSpace(reusableKey))
            {
                WorkerState reused = activeWorkers[reusableKey];
                _ = activeWorkers.Remove(reusableKey);
                activeWorkers[key] = reused;
                return reused;
            }

            WorkerState created = new() { WorkerId = ++workerSequence };
            activeWorkers[key] = created;
            return created;
        }

        // パネル総数が上限を超えたら、古い完了済みだけ間引く。
        private void TrimCompletedWorkersIfNeeded()
        {
            int overflowCount = activeWorkers.Count - MaxRetainedWorkerPanelCount;
            if (overflowCount <= 0)
            {
                return;
            }

            string[] staleKeys =
            [
                .. activeWorkers
                    .Where(x => !x.Value.IsActive)
                    .OrderBy(x => x.Value.CompletedAtUtc)
                    .ThenBy(x => x.Value.WorkerId)
                    .Take(overflowCount)
                    .Select(x => x.Key),
            ];
            foreach (string staleKey in staleKeys)
            {
                _ = activeWorkers.Remove(staleKey);
            }
        }

        // 長いファイル名は拡張子を残して中間省略する。
        private static string ToDisplayMovieName(string moviePath)
        {
            string fileName = Path.GetFileName(moviePath ?? "");
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return "(不明)";
            }

            string extension = Path.GetExtension(fileName);
            string body = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrWhiteSpace(extension))
            {
                return fileName.Length <= (MovieNameHeadLength + 3)
                    ? fileName
                    : $"{fileName[..MovieNameHeadLength]}...";
            }

            if (body.Length <= MovieNameHeadLength)
            {
                return fileName;
            }

            string extNoDot = extension.TrimStart('.');
            return $"{body[..MovieNameHeadLength]}...{extNoDot}";
        }

        private sealed class WorkerState
        {
            public long WorkerId { get; set; }
            public string MoviePath { get; set; } = "";
            public string DisplayMovieName { get; set; } = "(不明)";
            public string PreviewImagePath { get; set; } = "";
            public bool IsActive { get; set; } = true;
            public DateTime CompletedAtUtc { get; set; } = DateTime.MinValue;
        }
    }

    public sealed class ThumbnailProgressRuntimeSnapshot
    {
        public int SessionCompletedCount { get; init; }
        public int SessionTotalCount { get; init; }
        public int CurrentParallelism { get; init; }
        public int ConfiguredParallelism { get; init; }
        public IReadOnlyList<string> EnqueueLogs { get; init; } = [];
        public IReadOnlyList<ThumbnailProgressWorkerSnapshot> ActiveWorkers { get; init; } = [];
    }

    public sealed class ThumbnailProgressWorkerSnapshot
    {
        public long WorkerId { get; init; }
        public string WorkerLabel { get; init; } = "";
        public string DisplayMovieName { get; init; } = "";
        public string PreviewImagePath { get; init; } = "";
        public bool IsActive { get; init; } = true;
    }
}
