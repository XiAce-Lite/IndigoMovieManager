using System.Data.SQLite;
using System.Diagnostics;
using System.Text.Json;
using IndigoMovieManager.Thumbnail.Engines.IndexRepair;
using IndigoMovieManager.Thumbnail.FailureDb;

namespace IndigoMovieManager.Thumbnail.RescueWorker
{
    // 救済exeは1回起動で1本だけ掴み、最後までやり切る。
    internal sealed class RescueWorkerApplication
    {
        private const int LeaseMinutes = 5;
        private const int LeaseHeartbeatSeconds = 60;
        private static readonly string[] RescueEngineOrder =
        [
            "ffmpeg1pass",
            "ffmediatoolkit",
            "autogen",
            "opencv",
        ];
        private static readonly string[] RepairExtensions =
        [
            ".mp4",
            ".mkv",
            ".avi",
            ".wmv",
            ".asf",
            ".divx",
        ];
        private static readonly string[] RepairErrorKeywords =
        [
            "invalid data found",
            "moov atom not found",
            "video stream is missing",
            "no frames decoded",
            "find stream info failed",
            "stream info failed",
            "failed to open input",
            "avformat_open_input failed",
            "avformat_find_stream_info failed",
        ];

        public async Task<int> RunAsync(string[] args)
        {
            if (!TryParseArguments(args, out string mainDbFullPath))
            {
                Console.Error.WriteLine("usage: IndigoMovieManager.Thumbnail.RescueWorker --main-db <path>");
                return 2;
            }

            if (!File.Exists(mainDbFullPath))
            {
                Console.Error.WriteLine($"main db not found: {mainDbFullPath}");
                return 2;
            }

            ThumbnailFailureDbService failureDbService = new(mainDbFullPath);
            string leaseOwner = $"rescue-{Environment.ProcessId}-{Guid.NewGuid():N}";
            DateTime nowUtc = DateTime.UtcNow;
            ThumbnailFailureRecord leasedRecord = failureDbService.GetPendingRescueAndLease(
                leaseOwner,
                TimeSpan.FromMinutes(LeaseMinutes),
                nowUtc
            );
            if (leasedRecord == null)
            {
                Console.WriteLine("rescue queue empty");
                return 0;
            }

            Console.WriteLine(
                $"rescue leased: failure_id={leasedRecord.FailureId} movie='{leasedRecord.MoviePath}'"
            );

            using CancellationTokenSource heartbeatCts = new();
            Task heartbeatTask = RunLeaseHeartbeatAsync(
                failureDbService,
                leasedRecord.FailureId,
                leaseOwner,
                heartbeatCts.Token
            );

            try
            {
                await ProcessLeasedRecordAsync(failureDbService, leasedRecord, leaseOwner)
                    .ConfigureAwait(false);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"rescue worker failed: {ex.Message}");
                _ = failureDbService.UpdateFailureStatus(
                    leasedRecord.FailureId,
                    leaseOwner,
                    "gave_up",
                    DateTime.UtcNow,
                    extraJson: BuildTerminalExtraJson("worker_exception", "", false, ex.Message),
                    clearLease: true,
                    failureKind: ResolveFailureKind(ex, leasedRecord.MoviePath),
                    failureReason: ex.Message
                );
                return 1;
            }
            finally
            {
                heartbeatCts.Cancel();
                try
                {
                    await heartbeatTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // heartbeat停止時のキャンセルは正常系として握る。
                }
            }
        }

        private static async Task ProcessLeasedRecordAsync(
            ThumbnailFailureDbService failureDbService,
            ThumbnailFailureRecord leasedRecord,
            string leaseOwner
        )
        {
            string moviePath = leasedRecord.MoviePath ?? "";
            if (string.IsNullOrWhiteSpace(moviePath) || !File.Exists(moviePath))
            {
                _ = failureDbService.UpdateFailureStatus(
                    leasedRecord.FailureId,
                    leaseOwner,
                    "skipped",
                    DateTime.UtcNow,
                    extraJson: BuildTerminalExtraJson("missing_movie", "", false, "movie file not found"),
                    clearLease: true,
                    failureKind: ThumbnailFailureKind.FileMissing,
                    failureReason: "movie file not found"
                );
                return;
            }

            MainDbContext mainDbContext = ResolveMainDbContext(leasedRecord.MainDbFullPath);
            DeleteStaleErrorMarker(mainDbContext.ThumbFolder, leasedRecord.TabIndex, moviePath);

            QueueObj queueObj = new()
            {
                MovieFullPath = moviePath,
                MovieSizeBytes = TryGetMovieFileLength(moviePath),
                Tabindex = leasedRecord.TabIndex,
                IsRescueRequest = true,
            };

            ThumbnailCreationService thumbnailCreationService = new();
            VideoIndexRepairService repairService = new();
            int nextAttemptNo = Math.Max(leasedRecord.AttemptNo + 1, 2);

            RescueAttemptResult directResult = await RunEngineAttemptsAsync(
                    failureDbService,
                    leasedRecord,
                    leaseOwner,
                    queueObj,
                    thumbnailCreationService,
                    mainDbContext,
                    repairApplied: false,
                    sourceMovieFullPathOverride: null,
                    nextAttemptNo: nextAttemptNo
                )
                .ConfigureAwait(false);
            nextAttemptNo = directResult.NextAttemptNo;
            if (directResult.IsSuccess)
            {
                _ = failureDbService.UpdateFailureStatus(
                    leasedRecord.FailureId,
                    leaseOwner,
                    "rescued",
                    DateTime.UtcNow,
                    outputThumbPath: directResult.OutputThumbPath,
                    resultSignature: $"rescued:{directResult.EngineId}",
                    extraJson: BuildTerminalExtraJson("direct", directResult.EngineId, false, ""),
                    clearLease: true
                );
                return;
            }

            if (!ShouldTryIndexRepair(moviePath, directResult.LastFailureReason))
            {
                _ = failureDbService.UpdateFailureStatus(
                    leasedRecord.FailureId,
                    leaseOwner,
                    "gave_up",
                    DateTime.UtcNow,
                    extraJson: BuildTerminalExtraJson("direct_exhausted", "", false, directResult.LastFailureReason),
                    clearLease: true,
                    failureKind: directResult.LastFailureKind,
                    failureReason: directResult.LastFailureReason
                );
                return;
            }

            string repairedMoviePath = "";
            try
            {
                VideoIndexProbeResult probeResult = await repairService
                    .ProbeAsync(moviePath)
                    .ConfigureAwait(false);
                if (!probeResult.IsIndexCorruptionDetected)
                {
                    _ = failureDbService.UpdateFailureStatus(
                        leasedRecord.FailureId,
                        leaseOwner,
                        "gave_up",
                        DateTime.UtcNow,
                        extraJson: BuildTerminalExtraJson("repair_probe_negative", "", true, probeResult.DetectionReason),
                        clearLease: true,
                        failureKind: directResult.LastFailureKind,
                        failureReason: directResult.LastFailureReason
                    );
                    return;
                }

                repairedMoviePath = BuildRepairOutputPath(moviePath);
                VideoIndexRepairResult repairResult = await repairService
                    .RepairAsync(moviePath, repairedMoviePath)
                    .ConfigureAwait(false);
                if (!repairResult.IsSuccess || !File.Exists(repairResult.OutputPath))
                {
                    _ = failureDbService.UpdateFailureStatus(
                        leasedRecord.FailureId,
                        leaseOwner,
                        "gave_up",
                        DateTime.UtcNow,
                        extraJson: BuildTerminalExtraJson("repair_failed", "", true, repairResult.ErrorMessage),
                        clearLease: true,
                        failureKind: directResult.LastFailureKind,
                        failureReason: repairResult.ErrorMessage
                    );
                    return;
                }

                RescueAttemptResult repairedResult = await RunEngineAttemptsAsync(
                        failureDbService,
                        leasedRecord,
                        leaseOwner,
                        queueObj,
                        thumbnailCreationService,
                        mainDbContext,
                        repairApplied: true,
                        sourceMovieFullPathOverride: repairResult.OutputPath,
                        nextAttemptNo: nextAttemptNo
                    )
                    .ConfigureAwait(false);
                if (repairedResult.IsSuccess)
                {
                    _ = failureDbService.UpdateFailureStatus(
                        leasedRecord.FailureId,
                        leaseOwner,
                        "rescued",
                        DateTime.UtcNow,
                        outputThumbPath: repairedResult.OutputThumbPath,
                        resultSignature: $"rescued:{repairedResult.EngineId}",
                        extraJson: BuildTerminalExtraJson("repair_rescue", repairedResult.EngineId, true, ""),
                        clearLease: true
                    );
                    return;
                }

                _ = failureDbService.UpdateFailureStatus(
                    leasedRecord.FailureId,
                    leaseOwner,
                    "gave_up",
                    DateTime.UtcNow,
                    extraJson: BuildTerminalExtraJson("repair_exhausted", "", true, repairedResult.LastFailureReason),
                    clearLease: true,
                    failureKind: repairedResult.LastFailureKind,
                    failureReason: repairedResult.LastFailureReason
                );
            }
            finally
            {
                TryDeleteFileQuietly(repairedMoviePath);
            }
        }

        // 失敗時も試行をappendし、後から比較できるようにする。
        private static async Task<RescueAttemptResult> RunEngineAttemptsAsync(
            ThumbnailFailureDbService failureDbService,
            ThumbnailFailureRecord leasedRecord,
            string leaseOwner,
            QueueObj queueObj,
            ThumbnailCreationService thumbnailCreationService,
            MainDbContext mainDbContext,
            bool repairApplied,
            string sourceMovieFullPathOverride,
            int nextAttemptNo
        )
        {
            RescueAttemptResult result = new()
            {
                NextAttemptNo = nextAttemptNo,
                LastFailureKind = ThumbnailFailureKind.Unknown,
            };

            for (int i = 0; i < RescueEngineOrder.Length; i++)
            {
                string engineId = RescueEngineOrder[i];
                Stopwatch sw = Stopwatch.StartNew();
                string previousEngineEnv = Environment.GetEnvironmentVariable(
                    ThumbnailEnvConfig.ThumbEngine
                );

                try
                {
                    // エンジン切替はプロセス環境変数を使うため、このworkerは1プロセス1動画前提で動かす。
                    Environment.SetEnvironmentVariable(ThumbnailEnvConfig.ThumbEngine, engineId);
                    ThumbnailCreateResult createResult = await thumbnailCreationService
                        .CreateThumbAsync(
                            queueObj,
                            mainDbContext.DbName,
                            mainDbContext.ThumbFolder,
                            isResizeThumb: false,
                            isManual: false,
                            cts: CancellationToken.None,
                            sourceMovieFullPathOverride: sourceMovieFullPathOverride
                        )
                        .ConfigureAwait(false);
                    sw.Stop();

                    bool isSuccess =
                        createResult != null
                        && createResult.IsSuccess
                        && !string.IsNullOrWhiteSpace(createResult.SaveThumbFileName)
                        && File.Exists(createResult.SaveThumbFileName);
                    if (isSuccess)
                    {
                        result.IsSuccess = true;
                        result.EngineId = engineId;
                        result.OutputThumbPath = createResult.SaveThumbFileName;
                        result.NextAttemptNo = nextAttemptNo;
                        return result;
                    }

                    string failureReason = createResult?.ErrorMessage ?? "thumbnail create failed";
                    result.LastFailureReason = failureReason;
                    result.LastFailureKind = ResolveFailureKind(null, queueObj.MovieFullPath, failureReason);
                    AppendRescueAttemptRecord(
                        failureDbService,
                        leasedRecord,
                        leaseOwner,
                        engineId,
                        result.LastFailureKind,
                        failureReason,
                        sw.ElapsedMilliseconds,
                        repairApplied,
                        sourceMovieFullPathOverride,
                        nextAttemptNo
                    );
                    nextAttemptNo++;
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    result.LastFailureReason = ex.Message;
                    result.LastFailureKind = ResolveFailureKind(ex, queueObj.MovieFullPath);
                    AppendRescueAttemptRecord(
                        failureDbService,
                        leasedRecord,
                        leaseOwner,
                        engineId,
                        result.LastFailureKind,
                        ex.Message,
                        sw.ElapsedMilliseconds,
                        repairApplied,
                        sourceMovieFullPathOverride,
                        nextAttemptNo
                    );
                    nextAttemptNo++;
                }
                finally
                {
                    Environment.SetEnvironmentVariable(ThumbnailEnvConfig.ThumbEngine, previousEngineEnv);
                }
            }

            result.NextAttemptNo = nextAttemptNo;
            return result;
        }

        private static void AppendRescueAttemptRecord(
            ThumbnailFailureDbService failureDbService,
            ThumbnailFailureRecord leasedRecord,
            string leaseOwner,
            string engineId,
            ThumbnailFailureKind failureKind,
            string failureReason,
            long elapsedMs,
            bool repairApplied,
            string sourceMovieFullPathOverride,
            int attemptNo
        )
        {
            DateTime nowUtc = DateTime.UtcNow;
            _ = failureDbService.AppendFailureRecord(
                new ThumbnailFailureRecord
                {
                    MainDbFullPath = leasedRecord.MainDbFullPath,
                    MainDbPathHash = leasedRecord.MainDbPathHash,
                    MoviePath = leasedRecord.MoviePath,
                    MoviePathKey = leasedRecord.MoviePathKey,
                    TabIndex = leasedRecord.TabIndex,
                    Lane = "rescue",
                    AttemptGroupId = leasedRecord.AttemptGroupId,
                    AttemptNo = attemptNo,
                    Status = "processing_rescue",
                    LeaseOwner = leaseOwner,
                    LeaseUntilUtc = leasedRecord.LeaseUntilUtc,
                    Engine = engineId,
                    FailureKind = failureKind,
                    FailureReason = failureReason ?? "",
                    ElapsedMs = Math.Max(0, elapsedMs),
                    SourcePath = string.IsNullOrWhiteSpace(sourceMovieFullPathOverride)
                        ? leasedRecord.MoviePath
                        : sourceMovieFullPathOverride,
                    RepairApplied = repairApplied,
                    ResultSignature = "",
                    ExtraJson = BuildAttemptExtraJson(engineId, repairApplied),
                    CreatedAtUtc = nowUtc,
                    UpdatedAtUtc = nowUtc,
                }
            );
        }

        private static async Task RunLeaseHeartbeatAsync(
            ThumbnailFailureDbService failureDbService,
            long failureId,
            string leaseOwner,
            CancellationToken cts
        )
        {
            using PeriodicTimer timer = new(TimeSpan.FromSeconds(LeaseHeartbeatSeconds));
            while (await timer.WaitForNextTickAsync(cts).ConfigureAwait(false))
            {
                DateTime nowUtc = DateTime.UtcNow;
                failureDbService.ExtendLease(
                    failureId,
                    leaseOwner,
                    nowUtc.AddMinutes(LeaseMinutes),
                    nowUtc
                );
            }
        }

        // MainDBへは書き込まず、systemテーブルの読み取りだけを許容する。
        private static MainDbContext ResolveMainDbContext(string mainDbFullPath)
        {
            string dbName = Path.GetFileNameWithoutExtension(mainDbFullPath) ?? "";
            string thumbFolder = "";

            using SQLiteConnection connection = new($"Data Source={mainDbFullPath}");
            connection.Open();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM system WHERE attr = 'thum' LIMIT 1;";
            object value = command.ExecuteScalar();
            thumbFolder = Convert.ToString(value) ?? "";

            if (string.IsNullOrWhiteSpace(thumbFolder))
            {
                thumbFolder = TabInfo.GetDefaultThumbRoot(dbName);
            }

            return new MainDbContext(dbName, thumbFolder);
        }

        private static void DeleteStaleErrorMarker(string thumbFolder, int tabIndex, string moviePath)
        {
            try
            {
                TabInfo tabInfo = new(tabIndex, "", thumbFolder);
                string errorMarkerPath = ThumbnailPathResolver.BuildErrorMarkerPath(
                    tabInfo.OutPath,
                    moviePath
                );
                if (File.Exists(errorMarkerPath))
                {
                    File.Delete(errorMarkerPath);
                }
            }
            catch
            {
                // エラーマーカー掃除に失敗しても救済本体を優先する。
            }
        }

        private static bool ShouldTryIndexRepair(string moviePath, string failureReason)
        {
            if (string.IsNullOrWhiteSpace(moviePath) || string.IsNullOrWhiteSpace(failureReason))
            {
                return false;
            }

            string extension = Path.GetExtension(moviePath ?? "");
            if (!RepairExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            string normalizedReason = failureReason.Trim().ToLowerInvariant();
            for (int i = 0; i < RepairErrorKeywords.Length; i++)
            {
                if (normalizedReason.Contains(RepairErrorKeywords[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static long TryGetMovieFileLength(string moviePath)
        {
            try
            {
                return new FileInfo(moviePath).Length;
            }
            catch
            {
                return 0;
            }
        }

        private static string BuildRepairOutputPath(string moviePath)
        {
            string repairRoot = Path.Combine(
                Path.GetTempPath(),
                "IndigoMovieManager_fork_workthree",
                "thumbnail-repair"
            );
            Directory.CreateDirectory(repairRoot);

            string extension = Path.GetExtension(moviePath ?? "");
            string normalizedExtension =
                string.Equals(extension, ".mp4", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".mkv", StringComparison.OrdinalIgnoreCase)
                    ? extension
                    : ".mkv";
            return Path.Combine(repairRoot, $"{Guid.NewGuid():N}_repair{normalizedExtension}");
        }

        private static ThumbnailFailureKind ResolveFailureKind(
            Exception ex,
            string moviePath,
            string failureReasonOverride = ""
        )
        {
            if (ex is TimeoutException)
            {
                return ThumbnailFailureKind.HangSuspected;
            }

            if (ex is FileNotFoundException)
            {
                return ThumbnailFailureKind.FileMissing;
            }

            if (!string.IsNullOrWhiteSpace(moviePath))
            {
                try
                {
                    if (File.Exists(moviePath))
                    {
                        if (new FileInfo(moviePath).Length <= 0)
                        {
                            return ThumbnailFailureKind.ZeroByteFile;
                        }
                    }
                    else
                    {
                        return ThumbnailFailureKind.FileMissing;
                    }
                }
                catch
                {
                    // ファイル状態判定失敗時は文言判定へ進む。
                }
            }

            string normalized = string.IsNullOrWhiteSpace(failureReasonOverride)
                ? (ex?.Message ?? "").Trim().ToLowerInvariant()
                : failureReasonOverride.Trim().ToLowerInvariant();
            if (normalized.Contains("drm"))
            {
                return ThumbnailFailureKind.DrmProtected;
            }
            if (normalized.Contains("unsupported codec"))
            {
                return ThumbnailFailureKind.UnsupportedCodec;
            }
            if (
                normalized.Contains("moov atom not found")
                || normalized.Contains("invalid data found")
                || normalized.Contains("find stream info failed")
                || normalized.Contains("avformat_open_input failed")
                || normalized.Contains("avformat_find_stream_info failed")
            )
            {
                return ThumbnailFailureKind.IndexCorruption;
            }
            if (
                normalized.Contains("video stream is missing")
                || normalized.Contains("no video stream")
            )
            {
                return ThumbnailFailureKind.NoVideoStream;
            }
            if (normalized.Contains("no frames decoded"))
            {
                return ThumbnailFailureKind.TransientDecodeFailure;
            }
            if (
                normalized.Contains("being used by another process")
                || normalized.Contains("file is locked")
                || normalized.Contains("locked")
            )
            {
                return ThumbnailFailureKind.FileLocked;
            }

            return ThumbnailFailureKind.Unknown;
        }

        private static string BuildAttemptExtraJson(string engineId, bool repairApplied)
        {
            return JsonSerializer.Serialize(
                new
                {
                    WorkerRole = "rescue",
                    EngineForced = engineId ?? "",
                    RepairApplied = repairApplied,
                }
            );
        }

        private static string BuildTerminalExtraJson(
            string phase,
            string engineId,
            bool repairApplied,
            string detail
        )
        {
            return JsonSerializer.Serialize(
                new
                {
                    WorkerRole = "rescue",
                    Phase = phase ?? "",
                    EngineForced = engineId ?? "",
                    RepairApplied = repairApplied,
                    Detail = detail ?? "",
                }
            );
        }

        private static bool TryParseArguments(string[] args, out string mainDbFullPath)
        {
            mainDbFullPath = "";
            for (int i = 0; i < (args?.Length ?? 0); i++)
            {
                if (
                    string.Equals(args[i], "--main-db", StringComparison.OrdinalIgnoreCase)
                    && i + 1 < args.Length
                )
                {
                    mainDbFullPath = args[i + 1] ?? "";
                    break;
                }
            }

            return !string.IsNullOrWhiteSpace(mainDbFullPath);
        }

        private static void TryDeleteFileQuietly(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // 一時修復ファイルの掃除失敗は観測だけ残して続行する。
            }
        }

        private readonly record struct MainDbContext(string DbName, string ThumbFolder);

        private sealed class RescueAttemptResult
        {
            public bool IsSuccess { get; set; }
            public string EngineId { get; set; } = "";
            public string OutputThumbPath { get; set; } = "";
            public string LastFailureReason { get; set; } = "";
            public ThumbnailFailureKind LastFailureKind { get; set; } =
                ThumbnailFailureKind.Unknown;
            public int NextAttemptNo { get; set; }
        }
    }
}
