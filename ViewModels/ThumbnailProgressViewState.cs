using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager.ViewModels
{
    // サムネイル進捗タブの表示値を束ねるViewState。
    public sealed class ThumbnailProgressViewState : INotifyPropertyChanged
    {
        private string createdQueueText = "0 / 0 / 0";
        private string threadText = "0 / 0 / 0";
        private double cpuMeterValue;
        private string cpuMeterText = "0%";
        private double gpuMeterValue;
        private string gpuMeterText = "N/A";
        private double hddMeterValue;
        private string hddMeterText = "N/A";

        public string CreatedQueueText
        {
            get => createdQueueText;
            private set => SetField(ref createdQueueText, value);
        }

        public string ThreadText
        {
            get => threadText;
            private set => SetField(ref threadText, value);
        }

        public double CpuMeterValue
        {
            get => cpuMeterValue;
            private set => SetField(ref cpuMeterValue, value);
        }

        public string CpuMeterText
        {
            get => cpuMeterText;
            private set => SetField(ref cpuMeterText, value);
        }

        public double GpuMeterValue
        {
            get => gpuMeterValue;
            private set => SetField(ref gpuMeterValue, value);
        }

        public string GpuMeterText
        {
            get => gpuMeterText;
            private set => SetField(ref gpuMeterText, value);
        }

        public double HddMeterValue
        {
            get => hddMeterValue;
            private set => SetField(ref hddMeterValue, value);
        }

        public string HddMeterText
        {
            get => hddMeterText;
            private set => SetField(ref hddMeterText, value);
        }

        public ObservableCollection<string> QueueLogs { get; } = [];
        public ObservableCollection<ThumbnailProgressWorkerPanelViewState> WorkerPanels { get; } = [];
        public ThumbnailProgressWorkerPanelViewState RescueWorkerPanel { get; } =
            new(0) { WorkerLabel = "救済Worker" };

        public ThumbnailProgressViewState()
        {
            // 初回レイアウト時点で右側が空にならないよう、設定並列数ぶんの枠を先に作る。
            EnsureWorkerPanelSlots(ResolveInitialConfiguredParallelism());
        }

        public void Apply(
            ThumbnailProgressRuntimeSnapshot runtimeSnapshot,
            int logicalCoreCount,
            double cpuPercent,
            double? gpuPercent,
            double? hddPercent
        )
        {
            ApplySnapshot(runtimeSnapshot, logicalCoreCount);
            ApplyMeters(cpuPercent, gpuPercent, hddPercent);
        }

        // 進捗スナップショット（キュー数/スレッド情報/パネル一覧）だけを反映する。
        public void ApplySnapshot(ThumbnailProgressRuntimeSnapshot runtimeSnapshot, int logicalCoreCount)
        {
            int activeWorkerCount = CountActiveWorkers(runtimeSnapshot?.ActiveWorkers ?? []);
            int configuredParallelism = runtimeSnapshot?.ConfiguredParallelism ?? 0;
            int queueCount = Math.Max(0, runtimeSnapshot?.SessionTotalCount ?? 0);
            int createdCount = Math.Max(0, runtimeSnapshot?.SessionCompletedCount ?? 0);
            long totalCreatedCount = Math.Max(0L, runtimeSnapshot?.TotalCreatedCount ?? 0L);

            CreatedQueueText = $"{queueCount} / {createdCount} / {totalCreatedCount}";
            ThreadText = $"{activeWorkerCount} / {configuredParallelism} / {Math.Max(0, logicalCoreCount)}";

            SyncQueueLogs(runtimeSnapshot?.EnqueueLogs ?? []);
            SyncWorkerPanels(runtimeSnapshot?.ActiveWorkers ?? [], configuredParallelism);
            SyncRescueWorkerPanel(runtimeSnapshot?.RescueWorker);
        }

        // メーター（CPU/GPU/HDD）だけを更新する。
        public void ApplyMeters(double cpuPercent, double? gpuPercent, double? hddPercent)
        {
            CpuMeterValue = ClampPercent(cpuPercent);
            CpuMeterText = $"{CpuMeterValue:0.0}%";

            if (gpuPercent.HasValue)
            {
                GpuMeterValue = ClampPercent(gpuPercent.Value);
                GpuMeterText = $"{GpuMeterValue:0.0}%";
            }
            else
            {
                GpuMeterValue = 0;
                GpuMeterText = "N/A";
            }

            if (hddPercent.HasValue)
            {
                HddMeterValue = ClampPercent(hddPercent.Value);
                HddMeterText = $"{HddMeterValue:0.0}%";
            }
            else
            {
                HddMeterValue = 0;
                HddMeterText = "N/A";
            }
        }

        // 一時停止中はメーター値を表示しない。
        public void ApplyMetersPaused()
        {
            CpuMeterValue = 0;
            CpuMeterText = "一時停止中";
            GpuMeterValue = 0;
            GpuMeterText = "一時停止中";
            HddMeterValue = 0;
            HddMeterText = "一時停止中";
        }

        private void SyncQueueLogs(IReadOnlyList<string> logs)
        {
            if (logs == null)
            {
                logs = [];
            }

            // 内容が同一ならCollection更新をスキップし、UI再描画を抑える。
            if (QueueLogs.Count == logs.Count)
            {
                bool same = true;
                for (int i = 0; i < logs.Count; i++)
                {
                    if (!string.Equals(QueueLogs[i], logs[i], StringComparison.Ordinal))
                    {
                        same = false;
                        break;
                    }
                }

                if (same)
                {
                    return;
                }
            }

            int commonPrefix = 0;
            int sharedCount = Math.Min(QueueLogs.Count, logs.Count);
            while (
                commonPrefix < sharedCount
                && string.Equals(QueueLogs[commonPrefix], logs[commonPrefix], StringComparison.Ordinal)
            )
            {
                commonPrefix++;
            }

            for (int index = QueueLogs.Count - 1; index >= commonPrefix; index--)
            {
                QueueLogs.RemoveAt(index);
            }

            for (int index = commonPrefix; index < logs.Count; index++)
            {
                QueueLogs.Add(logs[index] ?? "");
            }
        }

        private void SyncWorkerPanels(
            IReadOnlyList<ThumbnailProgressWorkerSnapshot> workers,
            int configuredParallelism
        )
        {
            try
            {
                int maxIncomingWorkerId = workers.Count > 0
                    ? workers.Max(x => (int)Math.Max(0, x.WorkerId))
                    : 0;
                int desiredSlotCount = Math.Max(
                    WorkerPanels.Count,
                    Math.Max(Math.Max(0, configuredParallelism), maxIncomingWorkerId)
                );
                EnsureWorkerPanelSlots(desiredSlotCount);

                Dictionary<long, ThumbnailProgressWorkerSnapshot> workerById = new();
                foreach (ThumbnailProgressWorkerSnapshot worker in workers)
                {
                    if (worker.WorkerId < 1)
                    {
                        continue;
                    }

                    workerById[worker.WorkerId] = worker;
                }

                // 固定スロットは全件走査し、未割り当てスロットは「待機」へ戻す。
                for (int slotIndex = 0; slotIndex < WorkerPanels.Count; slotIndex++)
                {
                    long workerId = slotIndex + 1;
                    ThumbnailProgressWorkerPanelViewState panel = WorkerPanels[slotIndex];
                    if (workerById.TryGetValue(workerId, out ThumbnailProgressWorkerSnapshot worker))
                    {
                        ApplyWorkerSnapshot(panel, worker, workerId);
                    }
                    else
                    {
                        ApplyWaitingSnapshot(panel, workerId);
                    }
                }
            }
            catch
            {
                // 例外時も固定並びを維持しつつ、受信分だけ復旧する。
                int maxIncomingWorkerId = workers.Count > 0
                    ? workers.Max(x => (int)Math.Max(0, x.WorkerId))
                    : 0;
                int desiredSlotCount = Math.Max(
                    WorkerPanels.Count,
                    Math.Max(Math.Max(0, configuredParallelism), maxIncomingWorkerId)
                );
                EnsureWorkerPanelSlots(desiredSlotCount);
                foreach (ThumbnailProgressWorkerPanelViewState panel in WorkerPanels)
                {
                    ApplyWaitingSnapshot(panel, panel.WorkerId);
                }
            }
        }

        // 救済Workerは通常Thread群と別カードで描画し、外部exeの状態だけを載せる。
        private void SyncRescueWorkerPanel(ThumbnailProgressWorkerSnapshot rescueWorker)
        {
            if (rescueWorker == null)
            {
                ApplyWaitingSnapshot(RescueWorkerPanel, 0, "救済Worker");
                return;
            }

            ApplyWorkerSnapshot(RescueWorkerPanel, rescueWorker, 0, "救済Worker");
        }

        // スナップショット1件をスロットへ反映する。
        private static void ApplyWorkerSnapshot(
            ThumbnailProgressWorkerPanelViewState panel,
            ThumbnailProgressWorkerSnapshot worker,
            long workerId,
            string fallbackLabel = ""
        )
        {
            panel.WorkerLabel = ResolveWorkerPanelLabel(workerId, worker.WorkerLabel, fallbackLabel);
            panel.MoviePath = worker.MoviePath ?? "";
            panel.MovieName = worker.DisplayMovieName ?? "";
            panel.DetailText = worker.DetailText ?? "";
            panel.StatusTextOverride = worker.StatusTextOverride ?? "";
            panel.PreviewImagePath = worker.PreviewImagePath ?? "";
            panel.PreviewCacheKey = worker.PreviewCacheKey ?? "";
            panel.PreviewRevision = worker.PreviewRevision;
            panel.IsActive = worker.IsActive;
        }

        // 未割り当てスロットは待機状態へ戻し、古い表示を残さない。
        private static void ApplyWaitingSnapshot(
            ThumbnailProgressWorkerPanelViewState panel,
            long workerId,
            string fallbackLabel = ""
        )
        {
            panel.WorkerLabel = ResolveWorkerPanelLabel(workerId, fallbackLabel: fallbackLabel);
            panel.MoviePath = "";
            panel.MovieName = "";
            panel.DetailText = "";
            panel.StatusTextOverride = "";
            panel.PreviewImagePath = "";
            panel.PreviewCacheKey = "";
            panel.PreviewRevision = 0;
            panel.IsActive = false;
        }

        // 起動時に 1..最大並列数の順で固定スロットを確保し、以後は削除しない。
        private void EnsureWorkerPanelSlots(int slotCount)
        {
            if (slotCount <= WorkerPanels.Count)
            {
                return;
            }

            for (int workerId = WorkerPanels.Count + 1; workerId <= slotCount; workerId++)
            {
                WorkerPanels.Add(
                    new ThumbnailProgressWorkerPanelViewState(workerId)
                    {
                        WorkerLabel = ResolveWorkerPanelLabel(workerId),
                    }
                );
            }
        }

        private static int ResolveInitialConfiguredParallelism()
        {
            int configuredParallelism = 1;
            try
            {
                configuredParallelism = Properties.Settings.Default.ThumbnailParallelism;
            }
            catch
            {
                configuredParallelism = 1;
            }

            if (configuredParallelism < 1)
            {
                return 1;
            }

            if (configuredParallelism > 24)
            {
                return 24;
            }

            return configuredParallelism;
        }

        // Snapshot側のラベルを優先し、未設定時だけ通常Thread番号へ戻す。
        private static string ResolveWorkerPanelLabel(
            long workerId,
            string snapshotLabel = "",
            string fallbackLabel = ""
        )
        {
            if (!string.IsNullOrWhiteSpace(snapshotLabel))
            {
                return snapshotLabel;
            }

            if (!string.IsNullOrWhiteSpace(fallbackLabel))
            {
                return fallbackLabel;
            }

            return $"Thread {workerId}";
        }

        private static int CountActiveWorkers(IReadOnlyList<ThumbnailProgressWorkerSnapshot> workers)
        {
            int activeCount = 0;
            foreach (ThumbnailProgressWorkerSnapshot worker in workers)
            {
                if (worker.IsActive)
                {
                    activeCount++;
                }
            }

            return activeCount;
        }

        private static double ClampPercent(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return 0;
            }

            if (value < 0)
            {
                return 0;
            }

            if (value > 100)
            {
                return 100;
            }

            return value;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // 右サイドのスレッドパネル1件分。
    public sealed class ThumbnailProgressWorkerPanelViewState : INotifyPropertyChanged
    {
        private string workerLabel = "";
        private string moviePath = "";
        private string movieName = "";
        private string detailText = "";
        private string previewImagePath = "";
        private string previewCacheKey = "";
        private string statusTextOverride = "";
        private long previewRevision;
        private bool isActive;

        public ThumbnailProgressWorkerPanelViewState(long workerId)
        {
            WorkerId = workerId;
        }

        public long WorkerId { get; }

        public string WorkerLabel
        {
            get => workerLabel;
            set => SetField(ref workerLabel, value ?? "");
        }

        public string MoviePath
        {
            get => moviePath;
            set => SetField(ref moviePath, value ?? "");
        }

        public string MovieName
        {
            get => movieName;
            set
            {
                if (EqualityComparer<string>.Default.Equals(movieName, value ?? ""))
                {
                    return;
                }

                movieName = value ?? "";
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MovieName)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
            }
        }

        public string DetailText
        {
            get => detailText;
            set
            {
                if (EqualityComparer<string>.Default.Equals(detailText, value ?? ""))
                {
                    return;
                }

                detailText = value ?? "";
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DetailText)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasDetailText)));
            }
        }

        public string PreviewImagePath
        {
            get => previewImagePath;
            set => SetField(ref previewImagePath, value ?? "");
        }

        public string PreviewCacheKey
        {
            get => previewCacheKey;
            set => SetField(ref previewCacheKey, value ?? "");
        }

        public long PreviewRevision
        {
            get => previewRevision;
            set => SetField(ref previewRevision, value);
        }

        public string StatusTextOverride
        {
            get => statusTextOverride;
            set
            {
                if (EqualityComparer<string>.Default.Equals(statusTextOverride, value ?? ""))
                {
                    return;
                }

                statusTextOverride = value ?? "";
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusTextOverride)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
            }
        }

        public bool IsActive
        {
            get => isActive;
            set
            {
                if (EqualityComparer<bool>.Default.Equals(isActive, value))
                {
                    return;
                }

                isActive = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
            }
        }

        public bool HasDetailText => !string.IsNullOrWhiteSpace(DetailText);

        public string StatusText =>
            !string.IsNullOrWhiteSpace(StatusTextOverride)
                ? StatusTextOverride
                : IsActive
                    ? "処理中"
                    : string.IsNullOrWhiteSpace(MovieName)
                        ? "待機"
                        : "完了";

        public event PropertyChangedEventHandler PropertyChanged;

        private void SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
