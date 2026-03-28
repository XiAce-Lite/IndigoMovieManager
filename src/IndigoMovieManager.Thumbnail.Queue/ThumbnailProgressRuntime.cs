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
        private readonly object stateLock = new();
        private readonly Queue<string> enqueueLogs = new();
        private readonly Dictionary<string, WorkerState> activeWorkers = new(
            StringComparer.OrdinalIgnoreCase
        );

        private long workerSequence;
        private int sessionCompletedCount;
        private int sessionTotalCount;
        private long totalCreatedCount;
        private int currentParallelism;
        private int configuredParallelism;
        private long stateVersion;
        private ThumbnailProgressRuntimeSnapshot cachedSnapshot;

        public void Reset(long initialTotalCreatedCount = 0)
        {
            lock (stateLock)
            {
                long normalizedInitialTotalCreatedCount = Math.Max(0, initialTotalCreatedCount);
                bool hasAnyState =
                    enqueueLogs.Count > 0
                    || activeWorkers.Count > 0
                    || workerSequence != 0
                    || sessionCompletedCount != 0
                    || sessionTotalCount != 0
                    || totalCreatedCount != normalizedInitialTotalCreatedCount
                    || currentParallelism != 0
                    || configuredParallelism != 0;

                enqueueLogs.Clear();
                activeWorkers.Clear();
                workerSequence = 0;
                sessionCompletedCount = 0;
                sessionTotalCount = 0;
                totalCreatedCount = normalizedInitialTotalCreatedCount;
                currentParallelism = 0;
                configuredParallelism = 0;
                if (hasAnyState)
                {
                    MarkStateDirty();
                }
            }
        }

        // キュー投入ログは「動画名のみ」を最新N件で保持する。
        public void RecordEnqueue(QueueObj queueObj)
        {
            RecordEnqueue(queueObj?.ToThumbnailRequest());
        }

        public void RecordEnqueue(ThumbnailRequest request)
        {
            string movieName = Path.GetFileName(request?.MovieFullPath ?? "");
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

                MarkStateDirty();
            }
        }

        // 起動後に成功保存したサムネイル総数を積み上げる。
        public void RecordThumbnailCreated(int count = 1)
        {
            if (count <= 0)
            {
                return;
            }

            lock (stateLock)
            {
                totalCreatedCount += count;
                MarkStateDirty();
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
                int nextCompletedCount = Math.Max(0, completedCount);
                int nextTotalCount = Math.Max(nextCompletedCount, Math.Max(0, totalCount));
                int nextCurrentParallelism = Math.Max(0, currentParallel);
                int nextConfiguredParallelism = Math.Max(0, configuredParallel);

                if (
                    sessionCompletedCount == nextCompletedCount
                    && sessionTotalCount == nextTotalCount
                    && currentParallelism == nextCurrentParallelism
                    && configuredParallelism == nextConfiguredParallelism
                )
                {
                    return;
                }

                sessionCompletedCount = nextCompletedCount;
                sessionTotalCount = nextTotalCount;
                currentParallelism = nextCurrentParallelism;
                configuredParallelism = nextConfiguredParallelism;
                MarkStateDirty();
            }
        }

        // ジョブ開始時に右サイド表示の作業パネルを追加/更新する。
        public void MarkJobStarted(QueueObj queueObj)
        {
            MarkJobStarted(queueObj?.ToThumbnailRequest());
        }

        public void MarkJobStarted(ThumbnailRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.MovieFullPath))
            {
                return;
            }

            string key = CreateWorkerKey(request);
            lock (stateLock)
            {
                WorkerState worker = AcquireWorkerForJob(key, request);
                string nextMoviePath = request.MovieFullPath ?? "";
                string nextDisplayMovieName = ToDisplayMovieName(request.MovieFullPath);
                bool isChanged = false;

                if (!string.Equals(worker.MoviePath, nextMoviePath, StringComparison.OrdinalIgnoreCase))
                {
                    worker.MoviePath = nextMoviePath;
                    isChanged = true;
                }

                if (!string.Equals(worker.DisplayMovieName, nextDisplayMovieName, StringComparison.Ordinal))
                {
                    worker.DisplayMovieName = nextDisplayMovieName;
                    isChanged = true;
                }

                if (!worker.IsActive)
                {
                    worker.IsActive = true;
                    isChanged = true;
                }

                if (worker.CompletedAtUtc != DateTime.MinValue)
                {
                    worker.CompletedAtUtc = DateTime.MinValue;
                    isChanged = true;
                }

                string nextWorkerLabel = ResolveWorkerLabel(worker.WorkerId, request);
                if (!string.Equals(worker.WorkerLabel, nextWorkerLabel, StringComparison.Ordinal))
                {
                    worker.WorkerLabel = nextWorkerLabel;
                    isChanged = true;
                }

                if (isChanged)
                {
                    MarkStateDirty();
                }
            }
        }

        // サムネイル保存直後の画像パスを作業パネルへ反映する。
        // メモリプレビュー情報がある場合は、ファイル表示より優先できるよう同時に保持する。
        public void MarkThumbnailSaved(
            QueueObj queueObj,
            string previewImagePath,
            string previewCacheKey = "",
            long previewRevision = 0
        )
        {
            MarkThumbnailSaved(
                queueObj?.ToThumbnailRequest(),
                previewImagePath,
                previewCacheKey,
                previewRevision
            );
        }

        public void MarkThumbnailSaved(
            ThumbnailRequest request,
            string previewImagePath,
            string previewCacheKey = "",
            long previewRevision = 0
        )
        {
            if (request == null || string.IsNullOrWhiteSpace(previewImagePath))
            {
                return;
            }

            string key = CreateWorkerKey(request);
            lock (stateLock)
            {
                WorkerState worker = AcquireWorkerForJob(key, request);
                // 同一パネルに連続して同一動画キーが来た場合は、完了画像を再代入しない。
                if (string.Equals(worker.LastAppliedPreviewJobKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                worker.MoviePath = request.MovieFullPath ?? "";
                worker.DisplayMovieName = ToDisplayMovieName(request.MovieFullPath);
                worker.PreviewImagePath = previewImagePath;
                if (!string.IsNullOrWhiteSpace(previewCacheKey) && previewRevision > 0)
                {
                    worker.PreviewCacheKey = previewCacheKey;
                    worker.PreviewRevision = previewRevision;
                }
                else
                {
                    worker.PreviewCacheKey = "";
                    worker.PreviewRevision = previewRevision > 0
                        ? previewRevision
                        : DateTime.UtcNow.Ticks;
                }
                worker.IsActive = true;
                worker.CompletedAtUtc = DateTime.MinValue;
                worker.LastAppliedPreviewJobKey = key;
                MarkStateDirty();
            }
        }

        // ジョブ完了時は即削除せず、完了状態で残して履歴として見えるようにする。
        public void MarkJobCompleted(QueueObj queueObj)
        {
            MarkJobCompleted(queueObj?.ToThumbnailRequest());
        }

        public void MarkJobCompleted(ThumbnailRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.MovieFullPath))
            {
                return;
            }

            string key = CreateWorkerKey(request);
            lock (stateLock)
            {
                if (!activeWorkers.TryGetValue(key, out WorkerState worker))
                {
                    return;
                }

                if (!worker.IsActive)
                {
                    return;
                }

                worker.IsActive = false;
                worker.CompletedAtUtc = DateTime.UtcNow;
                TrimCompletedWorkersIfNeeded();
                MarkStateDirty();
            }
        }

        public ThumbnailProgressRuntimeSnapshot CreateSnapshot()
        {
            lock (stateLock)
            {
                if (cachedSnapshot is not null && cachedSnapshot.Version == stateVersion)
                {
                    return cachedSnapshot;
                }

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
                                WorkerLabel = string.IsNullOrWhiteSpace(x.WorkerLabel)
                                    ? ResolveWorkerLabel(x.WorkerId)
                                    : x.WorkerLabel,
                                MoviePath = x.MoviePath,
                                DisplayMovieName = x.DisplayMovieName,
                                PreviewImagePath = x.PreviewImagePath,
                                PreviewCacheKey = x.PreviewCacheKey,
                                PreviewRevision = x.PreviewRevision,
                                IsActive = x.IsActive,
                            }
                    ),
                ];

                ThumbnailProgressRuntimeSnapshot snapshot = new()
                {
                    Version = stateVersion,
                    SessionCompletedCount = sessionCompletedCount,
                    SessionTotalCount = sessionTotalCount,
                    TotalCreatedCount = totalCreatedCount,
                    CurrentParallelism = currentParallelism,
                    ConfiguredParallelism = configuredParallelism,
                    EnqueueLogs = logSnapshot,
                    ActiveWorkers = workerSnapshot,
                };

                cachedSnapshot = snapshot;
                return snapshot;
            }
        }

        public static string CreateWorkerKey(QueueObj queueObj)
        {
            return CreateWorkerKey(queueObj?.ToThumbnailRequest());
        }

        public static string CreateWorkerKey(ThumbnailRequest request)
        {
            string moviePathKey = QueueDbPathResolver.CreateMoviePathKey(
                request?.MovieFullPath ?? ""
            );
            return $"{moviePathKey}:{request?.TabIndex ?? -1}";
        }

        // まず同一ジョブキーを探し、なければ完了済みパネルを1つ再利用する。
        // 再利用時はPreviewImagePathを維持し、次ジョブのサムネが来るまで画像を見せ続ける。
        private WorkerState AcquireWorkerForJob(string key, ThumbnailRequest request)
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

            long nextWorkerId = FindNextAvailableWorkerId();
            WorkerState created = new() { WorkerId = nextWorkerId };
            workerSequence = nextWorkerId;
            activeWorkers[key] = created;
            return created;
        }

        // WorkerId は 1..上限 の固定スロットを再利用し、肥大化を防ぐ。
        private long FindNextAvailableWorkerId()
        {
            int maxWorkerCount = GetMaxRetainedWorkerPanelCount();
            HashSet<long> usedWorkerIds = [.. activeWorkers.Values.Select(x => x.WorkerId)];
            for (long workerId = 1; workerId <= maxWorkerCount; workerId++)
            {
                if (!usedWorkerIds.Contains(workerId))
                {
                    return workerId;
                }
            }

            return Math.Max(workerSequence + 1, 1);
        }

        // 通常は Thread n、巨大動画だけ専用ラベルで見えるようにする。
        private static string ResolveWorkerLabel(long workerId, ThumbnailRequest request = null)
        {
            ThumbnailExecutionLane lane =
                request == null
                    ? ThumbnailExecutionLane.Normal
                    : ThumbnailLaneClassifier.ResolveLane(request);
            return lane switch
            {
                ThumbnailExecutionLane.Slow => "BigMovie",
                _ => $"Thread {workerId}",
            };
        }

        // パネル総数が上限を超えたら、古い完了済みだけ間引く。
        private void TrimCompletedWorkersIfNeeded()
        {
            int overflowCount = activeWorkers.Count - GetMaxRetainedWorkerPanelCount();
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

        // 保持するWorker枠も UI と同じ上限に揃える。
        private static int GetMaxRetainedWorkerPanelCount()
        {
            return Math.Max(1, ThumbnailEnvConfig.GetThumbnailParallelismUpperBound());
        }

        private void MarkStateDirty()
        {
            stateVersion++;
            cachedSnapshot = null;
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
            public string WorkerLabel { get; set; } = "";
            public string MoviePath { get; set; } = "";
            public string DisplayMovieName { get; set; } = "(不明)";
            public string PreviewImagePath { get; set; } = "";
            public string PreviewCacheKey { get; set; } = "";
            public long PreviewRevision { get; set; }
            public string LastAppliedPreviewJobKey { get; set; } = "";
            public bool IsActive { get; set; } = true;
            public DateTime CompletedAtUtc { get; set; } = DateTime.MinValue;
        }
    }

    public sealed class ThumbnailProgressRuntimeSnapshot
    {
        public long Version { get; init; }
        public int SessionCompletedCount { get; init; }
        public int SessionTotalCount { get; init; }
        public long TotalCreatedCount { get; init; }
        public int CurrentParallelism { get; init; }
        public int ConfiguredParallelism { get; init; }
        public IReadOnlyList<string> EnqueueLogs { get; init; } = [];
        public IReadOnlyList<ThumbnailProgressWorkerSnapshot> ActiveWorkers { get; init; } = [];
        public ThumbnailProgressWorkerSnapshot RescueWorker { get; init; }
    }

    public sealed class ThumbnailProgressWorkerSnapshot
    {
        public long WorkerId { get; init; }
        public string WorkerLabel { get; init; } = "";
        public string MoviePath { get; init; } = "";
        public string DisplayMovieName { get; init; } = "";
        public string PreviewImagePath { get; init; } = "";
        public string PreviewCacheKey { get; init; } = "";
        public long PreviewRevision { get; init; }
        public bool IsActive { get; init; } = true;
        public string StatusTextOverride { get; init; } = "";
        public string DetailText { get; init; } = "";
    }
}
