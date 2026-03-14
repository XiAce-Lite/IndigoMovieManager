using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using IndigoMovieManager.Thumbnail.Engines;
using static IndigoMovieManager.Thumbnail.Tools;
using IndigoMovieManager;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 【サムネイル生成の絶対的オーケストレータ】✨
    /// 状況とルールを見極め、最適な生成エンジンを召喚してサムネイルを爆誕させるぜ！🔥
    /// </summary>
    public sealed class ThumbnailCreationService
    {
        // .NET では既定で一部コードページ（例: 932）が無効なため、
        // 既存処理互換としてCodePagesプロバイダを有効化しておく。
        static ThumbnailCreationService()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        private readonly IThumbnailGenerationEngine ffMediaToolkitEngine;
        private readonly IThumbnailGenerationEngine ffmpegOnePassEngine;
        private readonly IThumbnailGenerationEngine openCvEngine;
        private readonly IThumbnailGenerationEngine autogenEngine;
        private readonly ThumbnailEngineRouter engineRouter;
        private readonly IVideoMetadataProvider videoMetadataProvider;
        private readonly IThumbnailLogger logger;

        // 同一出力ファイルへの同時書き込みを防ぐ。
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> OutputFileLocks = new(
            StringComparer.OrdinalIgnoreCase
        );

        // 同一動画の再処理を軽くするため、ハッシュと動画秒数をキャッシュする。
        private static readonly ConcurrentDictionary<string, CachedMovieMeta> MovieMetaCache = new(
            StringComparer.OrdinalIgnoreCase
        );
        private const int MovieMetaCacheMaxCount = 10000;
        private static readonly object ThumbnailProcessLogLock = new();
        private const string ThumbnailProcessLogFileName = "thumbnail-create-process.csv";
        private const string EngineEnvName = "IMM_THUMB_ENGINE";
        private const string AutogenRetryEnvName = "IMM_THUMB_AUTOGEN_RETRY";
        private const string AutogenRetryDelayMsEnvName = "IMM_THUMB_AUTOGEN_RETRY_DELAY_MS";
        // Phase 2 では通常系の粘りを減らし、transient failure の再試行は1回に絞る。
        private const int DefaultAutogenRetryCount = 1;
        private const int DefaultAutogenRetryDelayMs = 300;
        private const string JpegSaveParallelEnvName = "IMM_THUMB_JPEG_SAVE_PARALLEL";
        private const int DefaultJpegSaveParallel = 4;
        private const int MaxJpegSaveRetryCount = 3;
        private const int BaseJpegSaveRetryDelayMs = 60;
        private const int AsfDrmScanMaxBytes = 64 * 1024;
        // GDI+ の保存処理だけは同時実行数を絞り、ハンドル圧迫での瞬断を減らす。
        private static readonly SemaphoreSlim JpegSaveGate = CreateJpegSaveGate();
        private static readonly byte[] AsfContentEncryptionObjectGuid =
        [
            0xFB,
            0xB3,
            0x11,
            0x22,
            0x23,
            0xBD,
            0xD2,
            0x11,
            0xB4,
            0xB7,
            0x00,
            0xA0,
            0xC9,
            0x55,
            0xFC,
            0x6E,
        ];
        private static readonly string[] AutogenTransientRetryKeywords =
        [
            "a generic error occurred in gdi+",
            "no frames decoded",
            "resource temporarily unavailable",
            "cannot allocate memory",
            "timeout",
        ];
        private static readonly string[] DrmErrorKeywords =
        [
            "prdy",
            "playready",
            "drm",
            "encrypted",
            "protected",
            "no decoder found for: none",
            "video stream is missing",
        ];
        private static readonly string[] UnsupportedErrorKeywords =
        [
            "decoder not found",
            "video stream not found",
            "unknown codec",
            "unknown",
            "unsupported",
            "invalid data found",
            "failed to open input",
        ];
        private static readonly string[] FfmpegOnePassSkipKeywords =
        [
            "invalid data found when processing input",
            "moov atom not found",
            "video stream is missing",
        ];

        public ThumbnailCreationService()
            : this(NoOpVideoMetadataProvider.Instance, NoOpThumbnailLogger.Instance) { }

        public ThumbnailCreationService(
            IVideoMetadataProvider videoMetadataProvider,
            IThumbnailLogger logger
        )
            : this(
                new FfMediaToolkitThumbnailGenerationEngine(),
                new FfmpegOnePassThumbnailGenerationEngine(),
                new OpenCvThumbnailGenerationEngine(),
                new FfmpegAutoGenThumbnailGenerationEngine(),
                videoMetadataProvider,
                logger
            ) { }

        internal ThumbnailCreationService(
            IThumbnailGenerationEngine ffMediaToolkitEngine,
            IThumbnailGenerationEngine ffmpegOnePassEngine,
            IThumbnailGenerationEngine openCvEngine,
            IThumbnailGenerationEngine autogenEngine
        )
            : this(
                ffMediaToolkitEngine,
                ffmpegOnePassEngine,
                openCvEngine,
                autogenEngine,
                NoOpVideoMetadataProvider.Instance,
                NoOpThumbnailLogger.Instance
            ) { }

        internal ThumbnailCreationService(
            IThumbnailGenerationEngine ffMediaToolkitEngine,
            IThumbnailGenerationEngine ffmpegOnePassEngine,
            IThumbnailGenerationEngine openCvEngine,
            IThumbnailGenerationEngine autogenEngine,
            IVideoMetadataProvider videoMetadataProvider,
            IThumbnailLogger logger
        )
        {
            this.ffMediaToolkitEngine =
                ffMediaToolkitEngine
                ?? throw new ArgumentNullException(nameof(ffMediaToolkitEngine));
            this.ffmpegOnePassEngine =
                ffmpegOnePassEngine ?? throw new ArgumentNullException(nameof(ffmpegOnePassEngine));
            this.openCvEngine =
                openCvEngine ?? throw new ArgumentNullException(nameof(openCvEngine));
            this.autogenEngine =
                autogenEngine ?? throw new ArgumentNullException(nameof(autogenEngine));
            this.videoMetadataProvider =
                videoMetadataProvider
                ?? throw new ArgumentNullException(nameof(videoMetadataProvider));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

            ThumbnailRuntimeLog.SetLogger(this.logger);

            engineRouter = new ThumbnailEngineRouter([
                this.ffMediaToolkitEngine,
                this.ffmpegOnePassEngine,
                this.openCvEngine,
                this.autogenEngine,
            ]);
        }

        /// <summary>
        /// ブックマーク用のとっておきの一枚（単一フレーム）を生成する専用ルートだ！📸
        /// </summary>
        public async Task<bool> CreateBookmarkThumbAsync(
            string movieFullPath,
            string saveThumbPath,
            int capturePos
        )
        {
            if (!Path.Exists(movieFullPath))
            {
                return false;
            }

            IThumbnailGenerationEngine engine = engineRouter.ResolveForBookmark();
            try
            {
                return await engine.CreateBookmarkAsync(
                    movieFullPath,
                    saveThumbPath,
                    capturePos,
                    CancellationToken.None
                );
            }
            catch (Exception ex)
            {
                ThumbnailRuntimeLog.Write(
                    "thumbnail",
                    $"bookmark create failed: engine={engine.EngineId}, movie='{movieFullPath}', err='{ex.Message}'"
                );
                return false;
            }
        }

        /// <summary>
        /// サムネイル生成の本丸！通常・手動を問わず、すべての生成処理はここから始まる激アツなメイン・エントリーポイントだぜ！🚀
        /// </summary>
        public async Task<ThumbnailCreateResult> CreateThumbAsync(
            QueueObj queueObj,
            string dbName,
            string thumbFolder,
            bool isResizeThumb,
            bool isManual = false,
            CancellationToken cts = default,
            string sourceMovieFullPathOverride = null
        )
        {
            TabInfo tbi = new(queueObj.Tabindex, dbName, thumbFolder);
            string movieFullPath = queueObj.MovieFullPath;
            string sourceMovieFullPath = string.IsNullOrWhiteSpace(sourceMovieFullPathOverride)
                ? movieFullPath
                : sourceMovieFullPathOverride.Trim();

            var cacheMeta = GetCachedMovieMeta(movieFullPath, queueObj?.Hash, out string cacheKey);
            string hash = cacheMeta.Hash;
            double? durationSec = cacheMeta.DurationSec;
            if (queueObj != null && string.IsNullOrWhiteSpace(queueObj.Hash))
            {
                // 以降の経路でも再利用できるよう、確定済みハッシュをQueueObjへ戻す。
                queueObj.Hash = hash;
            }

            string saveThumbFileName = ThumbnailPathResolver.BuildThumbnailPath(
                tbi,
                movieFullPath,
                hash
            );
            var outputLock = OutputFileLocks.GetOrAdd(
                saveThumbFileName,
                _ => new SemaphoreSlim(1, 1)
            );
            await outputLock.WaitAsync(cts);

            try
            {
                // 返却直前に処理ログを確実に残すため、戻り値生成をこの関数に集約する。
                ThumbnailCreateResult ReturnWithProcessLog(
                    ThumbnailCreateResult result,
                    string engineId,
                    string codec,
                    long fileSizeBytes
                )
                {
                    double? loggedDurationSec = result.DurationSec;
                    if (
                        (!loggedDurationSec.HasValue || loggedDurationSec.Value <= 0)
                        && durationSec.HasValue
                        && durationSec.Value > 0
                    )
                    {
                        loggedDurationSec = durationSec;
                    }

                    WriteThumbnailCreateProcessLog(
                        engineId,
                        movieFullPath,
                        codec,
                        loggedDurationSec,
                        fileSizeBytes,
                        result.SaveThumbFileName,
                        result.IsSuccess,
                        result.ErrorMessage
                    );
                    return result;
                }

                if (isManual && !Path.Exists(saveThumbFileName))
                {
                    // 手動更新は既存サムネイルが前提。
                    return ReturnWithProcessLog(
                        CreateFailedResult(
                            saveThumbFileName,
                            durationSec,
                            "manual target thumbnail does not exist"
                        ),
                        "precheck",
                        "",
                        0
                    );
                }

                if (!Path.Exists(tbi.OutPath))
                {
                    Directory.CreateDirectory(tbi.OutPath);
                }

                if (!Path.Exists(sourceMovieFullPath))
                {
                    if (!Path.Exists(saveThumbFileName))
                    {
                        string noFileJpeg = Path.Combine(Directory.GetCurrentDirectory(), "Images");
                        noFileJpeg = queueObj.Tabindex switch
                        {
                            0 => Path.Combine(noFileJpeg, "noFileSmall.jpg"),
                            1 => Path.Combine(noFileJpeg, "noFileBig.jpg"),
                            2 => Path.Combine(noFileJpeg, "noFileGrid.jpg"),
                            3 => Path.Combine(noFileJpeg, "noFileList.jpg"),
                            4 => Path.Combine(noFileJpeg, "noFileBig.jpg"),
                            99 => Path.Combine(noFileJpeg, "noFileGrid.jpg"),
                            _ => Path.Combine(noFileJpeg, "noFileSmall.jpg"),
                        };
                        File.Copy(noFileJpeg, saveThumbFileName, true);
                    }

                    return ReturnWithProcessLog(
                        CreateSuccessResult(saveThumbFileName, durationSec),
                        "missing-movie",
                        "",
                        0
                    );
                }

                long fileSizeBytes = Math.Max(0, queueObj?.MovieSizeBytes ?? 0);
                if (fileSizeBytes < 1)
                {
                    try
                    {
                        fileSizeBytes = new FileInfo(sourceMovieFullPath).Length;
                    }
                    catch
                    {
                        fileSizeBytes = 0;
                    }
                }

                // 後段で同じ情報を再利用できるよう、取得できたサイズをQueueObjへ戻しておく。
                if (queueObj != null && fileSizeBytes > 0)
                {
                    queueObj.MovieSizeBytes = fileSizeBytes;
                }

                if (!isManual && cacheMeta.IsDrmSuspected)
                {
                    // DRM判定ヒット時はデコーダーへ進まず、即プレースホルダーを生成して完了扱いにする。
                    string drmDetail = string.IsNullOrWhiteSpace(cacheMeta.DrmDetail)
                        ? "drm_suspected"
                        : cacheMeta.DrmDetail;
                    ThumbnailJobContext drmContext = new()
                    {
                        QueueObj = queueObj,
                        TabInfo = tbi,
                        ThumbInfo = BuildAutoThumbInfo(tbi, durationSec),
                        MovieFullPath = movieFullPath,
                        SaveThumbFileName = saveThumbFileName,
                        IsResizeThumb = isResizeThumb,
                        IsManual = isManual,
                        DurationSec = durationSec,
                        FileSizeBytes = fileSizeBytes,
                        AverageBitrateMbps = null,
                        HasEmojiPath = false,
                        VideoCodec = "",
                    };

                    if (
                        TryCreateFailurePlaceholderThumbnail(
                            drmContext,
                            FailurePlaceholderKind.DrmSuspected,
                            out string placeholderDetail
                        )
                    )
                    {
                        ThumbnailRuntimeLog.Write(
                            "thumbnail",
                            $"drm precheck hit: movie='{movieFullPath}', detail='{drmDetail}', placeholder='{placeholderDetail}'"
                        );
                        return ReturnWithProcessLog(
                            CreateSuccessResult(saveThumbFileName, durationSec),
                            "placeholder-drm-precheck",
                            "",
                            fileSizeBytes
                        );
                    }

                    string error = $"drm precheck hit but placeholder failed: {drmDetail}";
                    ThumbnailRuntimeLog.Write(
                        "thumbnail",
                        $"drm precheck failed: movie='{movieFullPath}', reason='{error}'"
                    );
                    return ReturnWithProcessLog(
                        CreateFailedResult(saveThumbFileName, durationSec, error),
                        "drm-precheck",
                        "",
                        fileSizeBytes
                    );
                }

                if (!durationSec.HasValue || durationSec.Value <= 0)
                {
                    if (
                        videoMetadataProvider.TryGetDurationSec(
                            sourceMovieFullPath,
                            out double providedDurationSec
                        )
                        && providedDurationSec > 0
                    )
                    {
                        durationSec = providedDurationSec;
                    }
                    else
                    {
                        durationSec = TryGetDurationSecFromShell(sourceMovieFullPath);
                    }
                    CacheMovieDuration(cacheKey, cacheMeta, durationSec);
                }

                ThumbInfo thumbInfo;
                if (isManual)
                {
                    thumbInfo = new ThumbInfo();
                    thumbInfo.GetThumbInfo(saveThumbFileName);
                    if (!thumbInfo.IsThumbnail)
                    {
                        return ReturnWithProcessLog(
                            CreateFailedResult(
                                saveThumbFileName,
                                durationSec,
                                "manual source thumbnail metadata is missing"
                            ),
                            "precheck",
                            "",
                            0
                        );
                    }

                    if ((queueObj.ThumbPanelPos != null) && (queueObj.ThumbTimePos != null))
                    {
                        int panelPos = (int)queueObj.ThumbPanelPos;
                        if (panelPos >= 0 && panelPos < thumbInfo.ThumbSec.Count)
                        {
                            thumbInfo.ThumbSec[panelPos] = (int)queueObj.ThumbTimePos;
                        }
                    }
                    thumbInfo.NewThumbInfo();
                }
                else
                {
                    thumbInfo = BuildAutoThumbInfo(tbi, durationSec);
                }

                double? avgBitrateMbps = null;
                if (fileSizeBytes > 0 && durationSec.HasValue && durationSec.Value > 0)
                {
                    avgBitrateMbps = (fileSizeBytes * 8d) / (durationSec.Value * 1_000_000d);
                }

                string videoCodec = "";
                if (
                    videoMetadataProvider.TryGetVideoCodec(
                        sourceMovieFullPath,
                        out string providedVideoCodec
                    )
                    && !string.IsNullOrWhiteSpace(providedVideoCodec)
                )
                {
                    videoCodec = providedVideoCodec;
                }

                ThumbnailJobContext context = new()
                {
                    QueueObj = queueObj,
                    TabInfo = tbi,
                    ThumbInfo = thumbInfo,
                    MovieFullPath = sourceMovieFullPath,
                    SaveThumbFileName = saveThumbFileName,
                    IsResizeThumb = isResizeThumb,
                    IsManual = isManual,
                    DurationSec = durationSec,
                    FileSizeBytes = fileSizeBytes,
                    AverageBitrateMbps = avgBitrateMbps,
                    HasEmojiPath = ThumbnailEngineRouter.HasUnmappableAnsiChar(movieFullPath),
                    VideoCodec = videoCodec,
                };

                IThumbnailGenerationEngine selectedEngine = engineRouter.ResolveForThumbnail(
                    context
                );
                List<IThumbnailGenerationEngine> engineOrder = BuildThumbnailEngineOrder(
                    selectedEngine,
                    context
                );
                ThumbnailCreateResult result = null;
                IThumbnailGenerationEngine executedEngine = selectedEngine;
                string processEngineId = selectedEngine?.EngineId ?? "unknown";
                List<string> engineErrorMessages = [];

                for (int i = 0; i < engineOrder.Count; i++)
                {
                    IThumbnailGenerationEngine candidate = engineOrder[i];
                    executedEngine = candidate;
                    processEngineId = candidate.EngineId;
                    ThumbnailRuntimeLog.Write(
                        "thumbnail",
                        i == 0
                            ? $"engine selected: id={candidate.EngineId}, panel={context.PanelCount}, size={context.FileSizeBytes}, avg_mbps={context.AverageBitrateMbps:0.###}, emoji={context.HasEmojiPath}, manual={context.IsManual}, is_rescue={context.QueueObj?.IsRescueRequest == true}"
                            : $"engine fallback: from={selectedEngine.EngineId}, to={candidate.EngineId}, attempt={i + 1}/{engineOrder.Count}, is_rescue={context.QueueObj?.IsRescueRequest == true}"
                    );
                    if (
                        i > 0
                        && string.Equals(
                            selectedEngine?.EngineId,
                            "autogen",
                            StringComparison.OrdinalIgnoreCase
                        )
                        && string.Equals(
                            candidate.EngineId,
                            "ffmpeg1pass",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        ThumbnailEngineRuntimeStats.RecordFallbackToFfmpegOnePass();
                    }

                    // 先行エンジンで入力破損が確定している場合、重いffmpeg1pass起動を省略する。
                    if (
                        !isManual
                        && string.Equals(
                            candidate.EngineId,
                            "ffmpeg1pass",
                            StringComparison.OrdinalIgnoreCase
                        )
                        && ShouldSkipFfmpegOnePassByKnownInvalidInput(engineErrorMessages)
                    )
                    {
                        const string skipReason = "known invalid input signature";
                        ThumbnailRuntimeLog.Write(
                            "thumbnail",
                            $"engine skipped: id=ffmpeg1pass, reason='{skipReason}'"
                        );
                        result = CreateFailedResult(
                            saveThumbFileName,
                            durationSec,
                            $"ffmpeg1pass skipped: {skipReason}"
                        );
                        engineErrorMessages.Add($"[ffmpeg1pass] skipped: {skipReason}");
                        break;
                    }

                    bool isAutogenCandidate = string.Equals(
                        candidate.EngineId,
                        "autogen",
                        StringComparison.OrdinalIgnoreCase
                    );
                    int autogenRetryCount = 0;
                    int maxAutogenRetryCount = ResolveAutogenRetryCount();
                    bool transientFailureRecorded = false;
                    while (true)
                    {
                        try
                        {
                            result = await candidate.CreateAsync(context, cts);
                        }
                        catch (OperationCanceledException) when (cts.IsCancellationRequested)
                        {
                            // 呼び出し元キャンセル時は既存どおり中断として扱う。
                            throw;
                        }
                        catch (Exception ex)
                        {
                            // エンジン内部例外は失敗結果へ変換して次候補へフォールバックする。
                            result = CreateFailedResult(saveThumbFileName, durationSec, ex.Message);
                        }

                        if (result == null)
                        {
                            result = CreateFailedResult(
                                saveThumbFileName,
                                durationSec,
                                "thumbnail engine returned null result"
                            );
                        }

                        bool isTransientAutogenFailure =
                            isAutogenCandidate
                            && !result.IsSuccess
                            && IsAutogenTransientRetryError(result.ErrorMessage);
                        if (isTransientAutogenFailure && !transientFailureRecorded)
                        {
                            transientFailureRecorded = true;
                            ThumbnailEngineRuntimeStats.RecordAutogenTransientFailure();
                        }

                        bool canRetryAutogen =
                            isTransientAutogenFailure
                            && autogenRetryCount < maxAutogenRetryCount
                            && IsAutogenRetryEnabled();
                        if (canRetryAutogen)
                        {
                            autogenRetryCount++;
                            int retryDelayMs = ResolveAutogenRetryDelayMs();
                            ThumbnailRuntimeLog.Write(
                                "thumbnail",
                                $"engine retry scheduled: id=autogen, attempt={autogenRetryCount}/{maxAutogenRetryCount}, delay_ms={retryDelayMs}, reason='{result.ErrorMessage}'"
                            );
                            if (retryDelayMs > 0)
                            {
                                await Task.Delay(retryDelayMs, cts).ConfigureAwait(false);
                            }
                            continue;
                        }

                        if (isAutogenCandidate && autogenRetryCount > 0 && result.IsSuccess)
                        {
                            ThumbnailEngineRuntimeStats.RecordAutogenRetrySuccess();
                            ThumbnailRuntimeLog.Write("thumbnail", "engine retry success: id=autogen");
                        }
                        break;
                    }

                    if (!result.IsSuccess && !string.IsNullOrWhiteSpace(result.ErrorMessage))
                    {
                        engineErrorMessages.Add($"[{candidate.EngineId}] {result.ErrorMessage}");
                    }

                    if (result.IsSuccess)
                    {
                        break;
                    }

                    if (i < engineOrder.Count - 1)
                    {
                        ThumbnailRuntimeLog.Write(
                            "thumbnail",
                            $"engine failed: id={candidate.EngineId}, reason='{result.ErrorMessage}', try_next=True"
                        );
                    }
                }

                if (result == null)
                {
                    result = CreateFailedResult(
                        saveThumbFileName,
                        durationSec,
                        "thumbnail engine was not executed"
                    );
                }

                // 全エンジン失敗時は、既知エラーを分類して専用プレースホルダー画像へ置き換える。
                // 置き換え成功時はキュー完了として扱い、同一ファイルの再試行ループを避ける。
                if (!result.IsSuccess && !isManual)
                {
                    FailurePlaceholderKind placeholderKind = ClassifyFailureForPlaceholder(
                        context.VideoCodec,
                        engineErrorMessages
                    );
                    if (
                        TryCreateFailurePlaceholderThumbnail(
                            context,
                            placeholderKind,
                            out string placeholderDetail
                        )
                    )
                    {
                        processEngineId = placeholderKind switch
                        {
                            FailurePlaceholderKind.DrmSuspected => "placeholder-drm",
                            FailurePlaceholderKind.UnsupportedCodec => "placeholder-unsupported",
                            _ => "placeholder-unknown",
                        };
                        ThumbnailRuntimeLog.Write(
                            "thumbnail",
                            $"failure placeholder created: kind={placeholderKind}, movie='{movieFullPath}', path='{saveThumbFileName}', detail='{placeholderDetail}'"
                        );
                        result = CreateSuccessResult(saveThumbFileName, durationSec);
                    }
                }

                // ── エラーマーカー出力 ──
                // 全エンジンが失敗した場合、次回スキャンで再度「新規」と誤判定されるのを防ぐため、
                // エラーを示すダミーjpg（0バイト）を出力フォルダに配置する。
                // 手動操作時は意図的なリトライのためマーカーを作らない。
                if (!result.IsSuccess && !isManual)
                {
                    try
                    {
                        string errorMarkerPath = ThumbnailPathResolver.BuildErrorMarkerPath(
                            tbi.OutPath,
                            movieFullPath
                        );
                        if (!Path.Exists(errorMarkerPath))
                        {
                            File.WriteAllBytes(errorMarkerPath, []);
                            ThumbnailRuntimeLog.Write(
                                "thumbnail",
                                $"error marker created: '{errorMarkerPath}'"
                            );
                        }
                    }
                    catch (Exception markerEx)
                    {
                        ThumbnailRuntimeLog.Write(
                            "thumbnail",
                            $"error marker write failed: '{markerEx.Message}'"
                        );
                    }
                }

                if (
                    (!durationSec.HasValue || durationSec.Value <= 0)
                    && result.DurationSec.HasValue
                    && result.DurationSec.Value > 0
                )
                {
                    CacheMovieDuration(cacheKey, cacheMeta, result.DurationSec);
                }
                return ReturnWithProcessLog(
                    result,
                    processEngineId,
                    context.VideoCodec,
                    context.FileSizeBytes
                );
            }
            finally
            {
                outputLock.Release();
            }
        }

        internal static ThumbnailCreateResult CreateSuccessResult(
            string saveThumbFileName,
            double? durationSec,
            ThumbnailPreviewFrame previewFrame = null
        )
        {
            return new ThumbnailCreateResult
            {
                SaveThumbFileName = saveThumbFileName,
                DurationSec = durationSec,
                IsSuccess = true,
                PreviewFrame = previewFrame,
            };
        }

        internal static ThumbnailCreateResult CreateFailedResult(
            string saveThumbFileName,
            double? durationSec,
            string errorMessage,
            ThumbnailPreviewFrame previewFrame = null
        )
        {
            return new ThumbnailCreateResult
            {
                SaveThumbFileName = saveThumbFileName,
                DurationSec = durationSec,
                IsSuccess = false,
                ErrorMessage = errorMessage ?? "",
                PreviewFrame = previewFrame,
            };
        }

        // エンジン内部で得たBitmapを、UI非依存のプレビューDTOへ詰め替える。
        internal static ThumbnailPreviewFrame CreatePreviewFrameFromBitmap(
            Bitmap source,
            int maxHeight = 120
        )
        {
            if (source == null || source.Width < 1 || source.Height < 1)
            {
                return null;
            }

            Size scaledSize = ResolvePreviewTargetSize(source.Size, maxHeight);
            using Bitmap normalized = new(
                scaledSize.Width,
                scaledSize.Height,
                PixelFormat.Format24bppRgb
            );
            using (Graphics g = Graphics.FromImage(normalized))
            {
                g.Clear(Color.Black);
                g.DrawImage(source, 0, 0, scaledSize.Width, scaledSize.Height);
            }

            BitmapData bitmapData = null;
            try
            {
                bitmapData = normalized.LockBits(
                    new Rectangle(0, 0, normalized.Width, normalized.Height),
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format24bppRgb
                );
                int stride = bitmapData.Stride;
                if (stride < 1)
                {
                    return null;
                }

                int pixelByteLength = stride * normalized.Height;
                if (pixelByteLength < 1)
                {
                    return null;
                }

                byte[] pixelBytes = new byte[pixelByteLength];
                Marshal.Copy(bitmapData.Scan0, pixelBytes, 0, pixelByteLength);
                return new ThumbnailPreviewFrame
                {
                    PixelBytes = pixelBytes,
                    Width = normalized.Width,
                    Height = normalized.Height,
                    Stride = stride,
                    PixelFormat = ThumbnailPreviewPixelFormat.Bgr24,
                };
            }
            catch
            {
                return null;
            }
            finally
            {
                if (bitmapData != null)
                {
                    normalized.UnlockBits(bitmapData);
                }
            }
        }

        // ミニパネル用途で過剰メモリを避けるため、上限高さだけ抑えて等比縮小する。
        private static Size ResolvePreviewTargetSize(Size sourceSize, int maxHeight)
        {
            if (sourceSize.Width < 1 || sourceSize.Height < 1)
            {
                return new Size(1, 1);
            }

            int safeMaxHeight = maxHeight < 1 ? sourceSize.Height : maxHeight;
            if (sourceSize.Height <= safeMaxHeight)
            {
                return sourceSize;
            }

            double scale = (double)safeMaxHeight / sourceSize.Height;
            int width = Math.Max(1, (int)Math.Round(sourceSize.Width * scale));
            return new Size(width, safeMaxHeight);
        }

        /// <summary>
        /// 自動生成時だけの特別ルール！もし今のエンジンが力尽きても、次の候補へバトンを繋いでサムネイル欠損を意地でも防ぐぜ！🏃‍♂️💨
        /// 絵文字パス時はOpenCV（ANSI制約あり）をフォールバック候補から除外し、DLL系エンジンだけで完結する！🤩
        /// </summary>
        private List<IThumbnailGenerationEngine> BuildThumbnailEngineOrder(
            IThumbnailGenerationEngine selectedEngine,
            ThumbnailJobContext context
        )
        {
            List<IThumbnailGenerationEngine> order = [];
            AddEngine(order, selectedEngine);

            bool forced = IsForcedEngineMode();
            if (forced)
            {
                return order;
            }

            // 絵文字パス時はOpenCVをフォールバック候補から除外！
            // OpenCVはANSI制約があり4段階入力パス解決が必要だが、
            // DLL系エンジン（autogen/ffMediaToolkit/ffmpeg1pass）は.NETのUnicode文字列を
            // そのまま扱えるため、絵文字パスでも一発で開ける！🔥
            bool skipOpenCv = context?.HasEmojiPath == true;

            if (context?.IsManual == true)
            {
                if (
                    string.Equals(
                        selectedEngine?.EngineId,
                        "ffmediatoolkit",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    if (!skipOpenCv)
                    {
                        AddEngine(order, openCvEngine);
                    }
                }
                else
                {
                    AddEngine(order, ffMediaToolkitEngine);
                }
                return order;
            }

            if (
                string.Equals(
                    selectedEngine?.EngineId,
                    "autogen",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                AddEngine(order, ffMediaToolkitEngine);
                AddEngine(order, ffmpegOnePassEngine);
                if (!skipOpenCv)
                {
                    AddEngine(order, openCvEngine);
                }
                return order;
            }

            if (
                string.Equals(
                    selectedEngine?.EngineId,
                    "ffmediatoolkit",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                AddEngine(order, autogenEngine);
                AddEngine(order, ffmpegOnePassEngine);
                if (!skipOpenCv)
                {
                    AddEngine(order, openCvEngine);
                }
                return order;
            }

            if (
                string.Equals(
                    selectedEngine?.EngineId,
                    "ffmpeg1pass",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                AddEngine(order, autogenEngine);
                AddEngine(order, ffMediaToolkitEngine);
                if (!skipOpenCv)
                {
                    AddEngine(order, openCvEngine);
                }
                return order;
            }

            if (
                string.Equals(
                    selectedEngine?.EngineId,
                    "opencv",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                AddEngine(order, autogenEngine);
                AddEngine(order, ffMediaToolkitEngine);
                AddEngine(order, ffmpegOnePassEngine);
                return order;
            }

            AddEngine(order, autogenEngine);
            AddEngine(order, ffMediaToolkitEngine);
            AddEngine(order, ffmpegOnePassEngine);
            if (!skipOpenCv)
            {
                AddEngine(order, openCvEngine);
            }
            return order;
        }

        // 既知の失敗文言をもとに、プレースホルダー画像の種類を判定する。
        private static FailurePlaceholderKind ClassifyFailureForPlaceholder(
            string codec,
            IReadOnlyList<string> engineErrorMessages
        )
        {
            StringBuilder merged = new();
            if (!string.IsNullOrWhiteSpace(codec))
            {
                merged.Append(codec);
                merged.Append(' ');
            }

            if (engineErrorMessages != null)
            {
                for (int i = 0; i < engineErrorMessages.Count; i++)
                {
                    if (string.IsNullOrWhiteSpace(engineErrorMessages[i]))
                    {
                        continue;
                    }
                    merged.Append(engineErrorMessages[i]);
                    merged.Append(' ');
                }
            }

            string text = merged.ToString().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(text))
            {
                return FailurePlaceholderKind.None;
            }

            if (ContainsAnyKeyword(text, DrmErrorKeywords))
            {
                return FailurePlaceholderKind.DrmSuspected;
            }

            if (ContainsAnyKeyword(text, UnsupportedErrorKeywords))
            {
                return FailurePlaceholderKind.UnsupportedCodec;
            }

            return FailurePlaceholderKind.None;
        }

        private static bool ContainsAnyKeyword(string text, IReadOnlyList<string> keywords)
        {
            if (string.IsNullOrWhiteSpace(text) || keywords == null)
            {
                return false;
            }

            for (int i = 0; i < keywords.Count; i++)
            {
                string keyword = keywords[i];
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    continue;
                }

                if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        // 既知の破損シグネチャが既に出ているなら、ffmpeg1pass起動は高確率で無駄なので省略する。
        private static bool ShouldSkipFfmpegOnePassByKnownInvalidInput(
            IReadOnlyList<string> engineErrorMessages
        )
        {
            if (engineErrorMessages == null || engineErrorMessages.Count < 1)
            {
                return false;
            }

            for (int i = 0; i < engineErrorMessages.Count; i++)
            {
                string message = engineErrorMessages[i];
                if (string.IsNullOrWhiteSpace(message))
                {
                    continue;
                }

                if (ContainsAnyKeyword(message, FfmpegOnePassSkipKeywords))
                {
                    return true;
                }
            }
            return false;
        }

        // 全エンジン失敗時に、用途別の画像を作ってサムネイル欠損を防ぐ。
        private static bool TryCreateFailurePlaceholderThumbnail(
            ThumbnailJobContext context,
            FailurePlaceholderKind kind,
            out string detail
        )
        {
            detail = "";
            if (kind == FailurePlaceholderKind.None || context == null)
            {
                return false;
            }

            try
            {
                int columns = Math.Max(1, context.TabInfo?.Columns ?? 1);
                int rows = Math.Max(1, context.TabInfo?.Rows ?? 1);
                int width = Math.Max(1, context.TabInfo?.Width ?? 120);
                int height = Math.Max(1, context.TabInfo?.Height ?? 90);
                int count = columns * rows;

                List<Bitmap> frames = [];
                try
                {
                    for (int i = 0; i < count; i++)
                    {
                        frames.Add(CreateFailurePlaceholderFrame(width, height, kind));
                    }

                    bool saved = SaveCombinedThumbnail(
                        context.SaveThumbFileName,
                        frames,
                        columns,
                        rows
                    );
                    if (!saved || !Path.Exists(context.SaveThumbFileName))
                    {
                        detail = "placeholder save failed";
                        return false;
                    }
                }
                finally
                {
                    for (int i = 0; i < frames.Count; i++)
                    {
                        frames[i]?.Dispose();
                    }
                }

                if (context.ThumbInfo?.SecBuffer != null && context.ThumbInfo.InfoBuffer != null)
                {
                    using FileStream dest = new(
                        context.SaveThumbFileName,
                        FileMode.Append,
                        FileAccess.Write
                    );
                    dest.Write(context.ThumbInfo.SecBuffer);
                    dest.Write(context.ThumbInfo.InfoBuffer);
                }

                detail = "placeholder saved";
                return true;
            }
            catch (Exception ex)
            {
                detail = ex.Message;
                return false;
            }
        }

        // プレースホルダー1コマを描画する。画面で原因が分かることを優先する。
        private static Bitmap CreateFailurePlaceholderFrame(
            int width,
            int height,
            FailurePlaceholderKind kind
        )
        {
            Bitmap bitmap = new(width, height, PixelFormat.Format24bppRgb);
            using Graphics g = Graphics.FromImage(bitmap);

            Color background = kind == FailurePlaceholderKind.DrmSuspected
                ? Color.FromArgb(90, 35, 35)
                : Color.FromArgb(45, 45, 45);
            Color stripe = kind == FailurePlaceholderKind.DrmSuspected
                ? Color.FromArgb(170, 65, 65)
                : Color.FromArgb(85, 110, 130);
            string title = kind == FailurePlaceholderKind.DrmSuspected ? "DRM?" : "CODEC NG";
            string subtitle = kind == FailurePlaceholderKind.DrmSuspected
                ? "保護コンテンツの可能性"
                : "非対応/破損の可能性";

            g.Clear(background);
            using (Brush stripeBrush = new SolidBrush(stripe))
            {
                g.FillRectangle(stripeBrush, 0, 0, width, Math.Max(18, height / 4));
            }
            using (Pen borderPen = new(Color.FromArgb(220, 220, 220), 1))
            {
                g.DrawRectangle(borderPen, 0, 0, width - 1, height - 1);
            }

            float titleSize = Math.Max(8f, Math.Min(16f, width * 0.11f));
            float subtitleSize = Math.Max(6f, Math.Min(11f, width * 0.065f));
            using Font titleFont = new("Yu Gothic UI", titleSize, FontStyle.Bold, GraphicsUnit.Point);
            using Font subtitleFont = new(
                "Yu Gothic UI",
                subtitleSize,
                FontStyle.Regular,
                GraphicsUnit.Point
            );
            using Brush textBrush = new SolidBrush(Color.WhiteSmoke);
            using StringFormat centered = new()
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };

            Rectangle titleRect = new(0, Math.Max(18, height / 4), width, Math.Max(16, height / 3));
            Rectangle subtitleRect = new(
                0,
                titleRect.Bottom,
                width,
                Math.Max(14, height - titleRect.Bottom - 2)
            );
            g.DrawString(title, titleFont, textBrush, titleRect, centered);
            g.DrawString(subtitle, subtitleFont, textBrush, subtitleRect, centered);

            return bitmap;
        }

        // ASF系（wmv/asf）のみ先頭ヘッダー判定を有効化する。
        private static bool IsAsfFamilyFile(string movieFullPath)
        {
            if (string.IsNullOrWhiteSpace(movieFullPath))
            {
                return false;
            }

            string ext = Path.GetExtension(movieFullPath);
            return ext.Equals(".wmv", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".asf", StringComparison.OrdinalIgnoreCase);
        }

        // Content Encryption Object GUID がヘッダー内にあるかを調べる。
        private static bool TryDetectAsfDrmProtected(string movieFullPath, out string detail)
        {
            detail = "";
            if (!Path.Exists(movieFullPath))
            {
                detail = "file_not_found";
                return false;
            }

            try
            {
                using FileStream fs = new(
                    movieFullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite
                );
                int readLength = (int)Math.Min(AsfDrmScanMaxBytes, fs.Length);
                if (readLength < AsfContentEncryptionObjectGuid.Length)
                {
                    detail = "header_too_short";
                    return false;
                }

                byte[] buffer = new byte[readLength];
                int totalRead = 0;
                while (totalRead < readLength)
                {
                    int read = fs.Read(buffer, totalRead, readLength - totalRead);
                    if (read <= 0)
                    {
                        break;
                    }
                    totalRead += read;
                }

                int hitIndex = IndexOfBytes(
                    buffer,
                    totalRead,
                    AsfContentEncryptionObjectGuid
                );
                if (hitIndex >= 0)
                {
                    detail = $"drm_guid_found_offset={hitIndex}";
                    return true;
                }

                detail = "drm_guid_not_found";
                return false;
            }
            catch (Exception ex)
            {
                detail = $"scan_error:{ex.GetType().Name}";
                return false;
            }
        }

        private static int IndexOfBytes(byte[] source, int sourceLength, byte[] pattern)
        {
            if (
                source == null
                || pattern == null
                || sourceLength < pattern.Length
                || pattern.Length < 1
            )
            {
                return -1;
            }

            int last = sourceLength - pattern.Length;
            for (int i = 0; i <= last; i++)
            {
                bool matched = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (source[i + j] != pattern[j])
                    {
                        matched = false;
                        break;
                    }
                }

                if (matched)
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool IsForcedEngineMode()
        {
            string mode = Environment.GetEnvironmentVariable(EngineEnvName)?.Trim() ?? "";
            return !string.IsNullOrWhiteSpace(mode)
                && !string.Equals(mode, "auto", StringComparison.OrdinalIgnoreCase);
        }

        // autogenリトライを有効化する。未指定時はONで動作させる。
        private static bool IsAutogenRetryEnabled()
        {
            string mode = Environment.GetEnvironmentVariable(AutogenRetryEnvName)?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(mode))
            {
                return true;
            }

            string normalized = mode.ToLowerInvariant();
            return normalized is "1" or "true" or "on" or "yes" or "auto";
        }

        private static int ResolveAutogenRetryCount()
        {
            return DefaultAutogenRetryCount;
        }

        // autogen再試行の待機時間を環境変数で調整可能にする。
        private static int ResolveAutogenRetryDelayMs()
        {
            string raw = Environment.GetEnvironmentVariable(AutogenRetryDelayMsEnvName)?.Trim() ?? "";
            if (
                !string.IsNullOrWhiteSpace(raw)
                && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            )
            {
                if (parsed < 0)
                {
                    return 0;
                }
                if (parsed > 5000)
                {
                    return 5000;
                }
                return parsed;
            }
            return DefaultAutogenRetryDelayMs;
        }

        // 一時的な負荷要因で回復が見込めるエラーかを判定する。
        private static bool IsAutogenTransientRetryError(string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(errorMessage))
            {
                return false;
            }

            string normalized = errorMessage.ToLowerInvariant();
            for (int i = 0; i < AutogenTransientRetryKeywords.Length; i++)
            {
                if (normalized.Contains(AutogenTransientRetryKeywords[i]))
                {
                    return true;
                }
            }
            return false;
        }

        private static void AddEngine(
            List<IThumbnailGenerationEngine> order,
            IThumbnailGenerationEngine engine
        )
        {
            if (engine == null)
            {
                return;
            }

            for (int i = 0; i < order.Count; i++)
            {
                if (
                    string.Equals(
                        order[i].EngineId,
                        engine.EngineId,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return;
                }
            }

            order.Add(engine);
        }

        /// <summary>
        /// 指定秒のフレーム探しだ！前方100ms刻みでしぶとく再試行し、短尺動画なら0秒近傍をミクロな視点で舐め回す執念のキャプチャ処理！🔎
        /// </summary>
        internal static bool TryReadFrameWithRetry(
            Decoders.IThumbnailFrameSource frameSource,
            TimeSpan baseTime,
            out Bitmap frameBitmap
        )
        {
            frameBitmap = null;
            if (frameSource == null)
            {
                return false;
            }

            for (int i = 0; i <= 100; i++)
            {
                TimeSpan tryTime = baseTime + TimeSpan.FromMilliseconds(i * 100);
                if (tryTime < TimeSpan.Zero)
                {
                    tryTime = TimeSpan.Zero;
                }

                if (frameSource.TryReadFrame(tryTime, out frameBitmap))
                {
                    return true;
                }
            }

            // 1秒未満～1秒付近の短尺動画は、0秒起点の細かい時刻で拾えることがある。
            if (baseTime <= TimeSpan.FromSeconds(1))
            {
                for (int ms = 0; ms <= 1000; ms += 33)
                {
                    if (frameSource.TryReadFrame(TimeSpan.FromMilliseconds(ms), out frameBitmap))
                    {
                        return true;
                    }
                }
            }

            frameBitmap?.Dispose();
            frameBitmap = null;
            return false;
        }

        // 動画末尾超えを避けるための安全な最大秒を返す（dur+1防止）。
        internal static int ResolveSafeMaxCaptureSec(double durationSec)
        {
            if (durationSec <= 0 || double.IsNaN(durationSec) || double.IsInfinity(durationSec))
            {
                return 0;
            }

            // 端数や丸め誤差で末尾超えしないよう、わずかに手前へ寄せる。
            double safeEnd = Math.Max(0, durationSec - 0.001);
            return Math.Max(0, (int)Math.Floor(safeEnd));
        }

        /// <summary>
        /// 動画の時間とパネル分割数から、昔ながらの王道ルールに従ってキャプチャする秒数の配列をバッチリ構築するぜ！📐
        /// </summary>
        internal static ThumbInfo BuildAutoThumbInfo(TabInfo tbi, double? durationSec)
        {
            int thumbCount = tbi.Columns * tbi.Rows;
            int divideSec = 1;
            int maxCaptureSec = int.MaxValue;
            if (durationSec.HasValue && durationSec.Value > 0)
            {
                divideSec = (int)(durationSec.Value / (thumbCount + 1));
                if (divideSec < 1)
                {
                    divideSec = 1;
                }

                // 短尺動画でも末尾超えしないよう、安全上限で丸める。
                maxCaptureSec = ResolveSafeMaxCaptureSec(durationSec.Value);
            }

            ThumbInfo thumbInfo = new()
            {
                ThumbWidth = tbi.Width,
                ThumbHeight = tbi.Height,
                ThumbRows = tbi.Rows,
                ThumbColumns = tbi.Columns,
                ThumbCounts = thumbCount,
            };

            for (int i = 1; i < thumbInfo.ThumbCounts + 1; i++)
            {
                int sec = i * divideSec;
                if (sec > maxCaptureSec)
                {
                    sec = maxCaptureSec;
                }
                thumbInfo.Add(sec);
            }
            thumbInfo.NewThumbInfo();
            return thumbInfo;
        }

        // 旧経路互換のため残しているが、新規生成では原則使わない。
        internal static Rectangle GetAspectRect(int imgWidth, int imgHeight)
        {
            int w = imgWidth;
            int h = imgHeight;
            int wdiff = 0;
            int hdiff = 0;

            float aspect = (float)imgWidth / imgHeight;
            if (aspect > 1.34f)
            {
                h = (int)Math.Floor((decimal)imgHeight / 3);
                w = (int)Math.Floor((decimal)h * 4);
                h = imgHeight;
                wdiff = (imgWidth - w) / 2;
                hdiff = 0;
            }

            if (aspect < 1.33f)
            {
                w = (int)Math.Floor((decimal)imgWidth / 4);
                h = (int)Math.Floor((decimal)w * 3);
                w = imgWidth;
                hdiff = (imgHeight - h) / 2;
                wdiff = 0;
            }
            return new Rectangle(wdiff, hdiff, w, h);
        }

        internal static Size ResolveDefaultTargetSize(Bitmap source)
        {
            int width = source.Width < 320 ? source.Width : 320;
            int height = source.Height < 240 ? source.Height : 240;

            if (width <= 0)
            {
                width = 320;
            }
            if (height <= 0)
            {
                height = 240;
            }
            return new Size(width, height);
        }

        internal static Bitmap CropBitmap(Bitmap source, Rectangle cropRect)
        {
            Rectangle bounded = Rectangle.Intersect(
                new Rectangle(0, 0, source.Width, source.Height),
                cropRect
            );
            if (bounded.Width <= 0 || bounded.Height <= 0)
            {
                bounded = new Rectangle(0, 0, source.Width, source.Height);
            }

            Bitmap cropped = new(bounded.Width, bounded.Height, PixelFormat.Format24bppRgb);
            using Graphics g = Graphics.FromImage(cropped);
            g.DrawImage(
                source,
                new Rectangle(0, 0, bounded.Width, bounded.Height),
                bounded,
                GraphicsUnit.Pixel
            );
            return cropped;
        }

        internal static Bitmap ResizeBitmap(Bitmap source, Size targetSize)
        {
            Bitmap resized = new(targetSize.Width, targetSize.Height, PixelFormat.Format24bppRgb);
            using Graphics g = Graphics.FromImage(resized);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.Clear(Color.Black);

            // 固定枠の中へ元動画の比率を保ったまま収め、余白は黒で埋める。
            Rectangle drawRect = CalculateAspectFitRectangle(
                new Size(source.Width, source.Height),
                targetSize
            );
            g.DrawImage(source, drawRect);
            return resized;
        }

        internal static Rectangle CalculateAspectFitRectangle(Size sourceSize, Size targetSize)
        {
            if (sourceSize.Width <= 0 || sourceSize.Height <= 0)
            {
                return new Rectangle(0, 0, Math.Max(1, targetSize.Width), Math.Max(1, targetSize.Height));
            }

            if (targetSize.Width <= 0 || targetSize.Height <= 0)
            {
                return new Rectangle(0, 0, sourceSize.Width, sourceSize.Height);
            }

            double widthScale = (double)targetSize.Width / sourceSize.Width;
            double heightScale = (double)targetSize.Height / sourceSize.Height;
            double scale = Math.Min(widthScale, heightScale);

            int drawWidth = Math.Max(1, (int)Math.Round(sourceSize.Width * scale));
            int drawHeight = Math.Max(1, (int)Math.Round(sourceSize.Height * scale));
            int offsetX = (targetSize.Width - drawWidth) / 2;
            int offsetY = (targetSize.Height - drawHeight) / 2;
            return new Rectangle(offsetX, offsetY, drawWidth, drawHeight);
        }

        /// <summary>
        /// 集めたフレームたちを1枚のキャンバスにタイル状に美しく敷き詰め、渾身のJPEGとして保存する最終仕上げだ！🖼️✨
        /// </summary>
        internal static bool SaveCombinedThumbnail(
            string saveThumbFileName,
            IReadOnlyList<Bitmap> frames,
            int columns,
            int rows
        )
        {
            if (frames.Count < 1)
            {
                return false;
            }

            int total = Math.Min(frames.Count, columns * rows);
            int frameWidth = frames[0].Width;
            int frameHeight = frames[0].Height;
            if (frameWidth <= 0 || frameHeight <= 0)
            {
                return false;
            }

            string saveDir = Path.GetDirectoryName(saveThumbFileName) ?? "";
            if (!string.IsNullOrWhiteSpace(saveDir))
            {
                Directory.CreateDirectory(saveDir);
            }

            using Bitmap canvas = new(
                frameWidth * columns,
                frameHeight * rows,
                PixelFormat.Format24bppRgb
            );
            using Graphics g = Graphics.FromImage(canvas);
            g.Clear(Color.Black);

            for (int i = 0; i < total; i++)
            {
                int r = i / columns;
                int c = i % columns;
                Rectangle destRect = new(c * frameWidth, r * frameHeight, frameWidth, frameHeight);
                g.DrawImage(frames[i], destRect);
            }

            try
            {
                return TrySaveJpegWithRetry(canvas, saveThumbFileName, out _);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"thumb save failed: path='{saveThumbFileName}', err={ex.Message}");
                return false;
            }
        }

        // JPEG保存時の一時エラーを吸収しつつ、壊れた中間ファイルを残さないように保存する。
        internal static bool TrySaveJpegWithRetry(Image image, string savePath, out string errorMessage)
        {
            errorMessage = "";
            if (image == null)
            {
                errorMessage = "image is null";
                return false;
            }

            if (string.IsNullOrWhiteSpace(savePath))
            {
                errorMessage = "save path is empty";
                return false;
            }

            string saveDir = Path.GetDirectoryName(savePath) ?? "";
            if (!string.IsNullOrWhiteSpace(saveDir))
            {
                Directory.CreateDirectory(saveDir);
            }

            Exception lastError = null;
            JpegSaveGate.Wait();
            try
            {
                for (int attempt = 1; attempt <= MaxJpegSaveRetryCount; attempt++)
                {
                    string tempPath = BuildTempJpegPath(savePath, attempt);
                    try
                    {
                        using (FileStream fs = new(
                            tempPath,
                            FileMode.Create,
                            FileAccess.Write,
                            FileShare.None
                        ))
                        {
                            image.Save(fs, ImageFormat.Jpeg);
                            fs.Flush(true);
                        }

                        ReplaceFileAtomically(tempPath, savePath);
                        if (attempt > 1)
                        {
                            ThumbnailRuntimeLog.Write(
                                "thumbnail",
                                $"jpeg save recovered after retry: attempt={attempt}, path='{savePath}'"
                            );
                        }
                        return true;
                    }
                    catch (Exception ex) when (IsTransientJpegSaveError(ex))
                    {
                        lastError = ex;
                        TryDeleteFileQuietly(tempPath);
                        if (attempt >= MaxJpegSaveRetryCount)
                        {
                            break;
                        }

                        Thread.Sleep(BaseJpegSaveRetryDelayMs * attempt);
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        TryDeleteFileQuietly(tempPath);
                        break;
                    }
                }
            }
            finally
            {
                JpegSaveGate.Release();
            }

            errorMessage = lastError?.Message ?? "jpeg save failed";
            ThumbnailRuntimeLog.Write(
                "thumbnail",
                $"jpeg save failed: path='{savePath}', reason='{errorMessage}'"
            );
            return false;
        }

        private static SemaphoreSlim CreateJpegSaveGate()
        {
            int parallel = DefaultJpegSaveParallel;
            string raw = Environment.GetEnvironmentVariable(JpegSaveParallelEnvName);
            if (
                !string.IsNullOrWhiteSpace(raw)
                && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            )
            {
                parallel = Math.Clamp(parsed, 1, 32);
            }
            return new SemaphoreSlim(parallel, parallel);
        }

        private static string BuildTempJpegPath(string savePath, int attempt)
        {
            string fileName = Path.GetFileName(savePath);
            string tempFileName =
                $"{fileName}.tmp.{Environment.ProcessId}.{Thread.CurrentThread.ManagedThreadId}.{attempt}.{Guid.NewGuid():N}";
            string dir = Path.GetDirectoryName(savePath) ?? "";
            return Path.Combine(dir, tempFileName);
        }

        private static void ReplaceFileAtomically(string tempPath, string savePath)
        {
            if (Path.Exists(savePath))
            {
                File.Replace(tempPath, savePath, null, true);
                return;
            }

            File.Move(tempPath, savePath);
        }

        private static bool IsTransientJpegSaveError(Exception ex)
        {
            return ex is ExternalException || ex is IOException || ex is UnauthorizedAccessException;
        }

        private static void TryDeleteFileQuietly(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.Exists(path))
            {
                return;
            }

            try
            {
                File.Delete(path);
            }
            catch
            {
                // 一時ファイル削除失敗は後続処理を優先する。
            }
        }

        // 必要時のみShell経由で秒数を取得する（最後のフォールバック）。
        internal static double? TryGetDurationSecFromShell(string fileName)
        {
            object shellObj = null;
            object folderObj = null;
            object itemObj = null;
            try
            {
                var shellAppType = Type.GetTypeFromProgID("Shell.Application");
                if (shellAppType == null)
                {
                    return null;
                }

                shellObj = Activator.CreateInstance(shellAppType);
                if (shellObj == null)
                {
                    return null;
                }

                dynamic shell = shellObj;
                folderObj = shell.NameSpace(Path.GetDirectoryName(fileName));
                if (folderObj == null)
                {
                    return null;
                }

                dynamic folder = folderObj;
                itemObj = folder.ParseName(Path.GetFileName(fileName));
                if (itemObj == null)
                {
                    return null;
                }

                string timeString = folder.GetDetailsOf(itemObj, 27);
                if (TimeSpan.TryParse(timeString, out TimeSpan ts))
                {
                    if (ts.TotalSeconds > 0)
                    {
                        return Math.Truncate(ts.TotalSeconds);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine($"duration shell err = {e.Message} Movie = {fileName}");
            }
            finally
            {
                ReleaseComObject(itemObj);
                ReleaseComObject(folderObj);
                ReleaseComObject(shellObj);
            }

            return null;
        }

        private static void ReleaseComObject(object comObj)
        {
            if (comObj == null)
            {
                return;
            }
            try
            {
                if (Marshal.IsComObject(comObj))
                {
                    Marshal.FinalReleaseComObject(comObj);
                }
            }
            catch
            {
                // COM解放失敗時は処理継続を優先する。
            }
        }

        private static CachedMovieMeta GetCachedMovieMeta(
            string movieFullPath,
            string hashHint,
            out string cacheKey
        )
        {
            cacheKey = BuildMovieMetaCacheKey(movieFullPath);
            return MovieMetaCache.GetOrAdd(
                cacheKey,
                _ =>
                {
                    string hash = ResolveMovieHash(movieFullPath, hashHint);
                    bool isDrmSuspected = false;
                    string drmDetail = "";
                    if (IsAsfFamilyFile(movieFullPath))
                    {
                        isDrmSuspected = TryDetectAsfDrmProtected(movieFullPath, out drmDetail);
                    }

                    return new CachedMovieMeta(hash, null, isDrmSuspected, drmDetail);
                }
            );
        }

        private static string ResolveMovieHash(string movieFullPath, string hashHint)
        {
            if (!string.IsNullOrWhiteSpace(hashHint))
            {
                return hashHint;
            }

            return GetHashCRC32(movieFullPath);
        }

        private static string BuildMovieMetaCacheKey(string movieFullPath)
        {
            try
            {
                FileInfo fi = new(movieFullPath);
                if (!fi.Exists)
                {
                    return movieFullPath;
                }
                return $"{movieFullPath}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}";
            }
            catch
            {
                return movieFullPath;
            }
        }

        private static void CacheMovieDuration(
            string cacheKey,
            CachedMovieMeta currentMeta,
            double? durationSec
        )
        {
            if (!durationSec.HasValue || durationSec.Value <= 0)
            {
                return;
            }

            string hash = currentMeta?.Hash ?? "";
            bool isDrmSuspected = currentMeta?.IsDrmSuspected ?? false;
            string drmDetail = currentMeta?.DrmDetail ?? "";
            MovieMetaCache[cacheKey] = new CachedMovieMeta(
                hash,
                durationSec,
                isDrmSuspected,
                drmDetail
            );
            if (MovieMetaCache.Count > MovieMetaCacheMaxCount)
            {
                MovieMetaCache.Clear();
            }
        }

        /// <summary>
        /// サムネ生成の生きた証（実績）を1行のCSVとしてバッチリ書き残すぜ！📝
        /// 後から集計・比較しやすいようにガチガチの固定フォーマットで記録だ！
        /// </summary>
        private static void WriteThumbnailCreateProcessLog(
            string engineId,
            string movieFullPath,
            string codec,
            double? durationSec,
            long fileSizeBytes,
            string outputPath,
            bool isSuccess,
            string errorMessage
        )
        {
            try
            {
                string logDir = AppLocalDataPaths.LogsPath;
                Directory.CreateDirectory(logDir);

                string logPath = Path.Combine(logDir, ThumbnailProcessLogFileName);
                bool needsHeader = !Path.Exists(logPath) || new FileInfo(logPath).Length == 0;
                string durationText =
                    durationSec.HasValue && durationSec.Value > 0
                        ? durationSec.Value.ToString("0.###", CultureInfo.InvariantCulture)
                        : "";
                string sizeText =
                    fileSizeBytes > 0 ? fileSizeBytes.ToString(CultureInfo.InvariantCulture) : "0";
                string movieFileName = Path.GetFileName(movieFullPath) ?? "";
                string line = string.Join(
                    ",",
                    EscapeCsvValue(
                        DateTime.Now.ToString(
                            "yyyy-MM-dd HH:mm:ss.fff",
                            CultureInfo.InvariantCulture
                        )
                    ),
                    EscapeCsvValue(engineId ?? ""),
                    EscapeCsvValue(movieFileName),
                    EscapeCsvValue(codec ?? ""),
                    EscapeCsvValue(durationText),
                    EscapeCsvValue(sizeText),
                    EscapeCsvValue(outputPath ?? ""),
                    EscapeCsvValue(isSuccess ? "success" : "failed"),
                    EscapeCsvValue(errorMessage ?? "")
                );

                lock (ThumbnailProcessLogLock)
                {
                    using StreamWriter writer = new(logPath, append: true, new UTF8Encoding(false));
                    if (needsHeader)
                    {
                        writer.WriteLine(
                            "datetime,engine,movie_file_name,codec,length_sec,size_bytes,output_path,status,error_message"
                        );
                    }
                    writer.WriteLine(line);
                }
            }
            catch
            {
                // ログ失敗で本体処理を止めない。
            }
        }

        private static string EscapeCsvValue(string value)
        {
            value ??= "";
            if (
                !value.Contains(',')
                && !value.Contains('"')
                && !value.Contains('\n')
                && !value.Contains('\r')
            )
            {
                return value;
            }
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        private enum FailurePlaceholderKind
        {
            None = 0,
            DrmSuspected = 1,
            UnsupportedCodec = 2,
        }
    }

    /// <summary>
    /// MainWindowへ凱旋報告するための、サムネイル生成結果をまとめたイケてるクラスだ！🏅
    /// </summary>
    public sealed class ThumbnailCreateResult
    {
        public string SaveThumbFileName { get; init; } = "";
        public double? DurationSec { get; init; }
        public bool IsSuccess { get; init; }
        public string ErrorMessage { get; init; } = "";
        public ThumbnailPreviewFrame PreviewFrame { get; init; }
    }

    /// <summary>
    /// WPF非依存でプレビュー画素を受け渡すための中立DTO。
    /// </summary>
    public sealed class ThumbnailPreviewFrame
    {
        public byte[] PixelBytes { get; init; } = [];
        public int Width { get; init; }
        public int Height { get; init; }
        public int Stride { get; init; }
        public ThumbnailPreviewPixelFormat PixelFormat { get; init; } =
            ThumbnailPreviewPixelFormat.Bgr24;

        public bool IsValid()
        {
            if (PixelBytes == null || Width < 1 || Height < 1 || Stride < 1)
            {
                return false;
            }

            long requiredLength = (long)Stride * Height;
            if (requiredLength < 1 || requiredLength > int.MaxValue)
            {
                return false;
            }

            return PixelBytes.Length >= requiredLength;
        }
    }

    public enum ThumbnailPreviewPixelFormat
    {
        Unknown = 0,
        Bgr24 = 1,
        Bgra32 = 2,
    }

    internal sealed class CachedMovieMeta
    {
        public CachedMovieMeta(
            string hash,
            double? durationSec,
            bool isDrmSuspected,
            string drmDetail
        )
        {
            Hash = hash ?? "";
            DurationSec = durationSec;
            IsDrmSuspected = isDrmSuspected;
            DrmDetail = drmDetail ?? "";
        }

        public string Hash { get; }
        public double? DurationSec { get; }
        public bool IsDrmSuspected { get; }
        public string DrmDetail { get; }
    }
}
