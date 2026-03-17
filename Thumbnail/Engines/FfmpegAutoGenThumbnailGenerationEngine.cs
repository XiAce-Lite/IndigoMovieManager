using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FFmpeg.AutoGen;

namespace IndigoMovieManager.Thumbnail.Engines
{
    /// <summary>
    /// FFmpeg.AutoGen を用いてアンマネージド領域で高速にサムネイルを生成するエンジン。
    /// </summary>
    internal sealed class FfmpegAutoGenThumbnailGenerationEngine : IThumbnailGenerationEngine
    {
        private static bool _isInitialized;
        private static bool _initAttempted;
        private static string _initFailureReason = "";
        private static readonly object _initLock = new();
        private static int _safetyConfigLogged;
        private const string EngineParallelEnvName = "IMM_THUMB_AUTOGEN_ENGINE_PARALLEL";
        private const int DefaultEngineParallel = 1;
        private static readonly int EngineParallelLimit = ResolveParallelLimit(
            EngineParallelEnvName,
            DefaultEngineParallel
        );
        private static readonly SemaphoreSlim EngineExecutionGate = CreateEngineExecutionGate();
        private const string NativeScaleParallelEnvName = "IMM_THUMB_AUTOGEN_NATIVE_PARALLEL";
        private const int DefaultNativeScaleParallel = 1;
        private static readonly int NativeScaleParallelLimit = ResolveParallelLimit(
            NativeScaleParallelEnvName,
            DefaultNativeScaleParallel
        );
        private static readonly SemaphoreSlim NativeScaleGate = CreateNativeScaleGate();

        public string EngineId => "autogen";
        public string EngineName => "autogen";

        public FfmpegAutoGenThumbnailGenerationEngine()
        {
            // コンストラクタでは重い初期化を行わず、実行時に遅延初期化する。
        }

        private static bool EnsureFfmpegInitializedSafe(out string errorMessage)
        {
            if (_isInitialized)
            {
                errorMessage = "";
                return true;
            }

            if (_initAttempted)
            {
                errorMessage = string.IsNullOrWhiteSpace(_initFailureReason)
                    ? "autogen initialization failed"
                    : _initFailureReason;
                return false;
            }

            lock (_initLock)
            {
                if (_isInitialized)
                {
                    errorMessage = "";
                    return true;
                }

                if (_initAttempted)
                {
                    errorMessage = string.IsNullOrWhiteSpace(_initFailureReason)
                        ? "autogen initialization failed"
                        : _initFailureReason;
                    return false;
                }

                _initAttempted = true;
                try
                {
                    // IMM_FFMPEG_EXE_PATH がファイルでもディレクトリでも扱えるように正規化する。
                    string ffmpegSharedDir = ResolveFfmpegSharedDirectory();
                    ffmpeg.RootPath = ffmpegSharedDir;
                    DynamicallyLoadedBindings.Initialize();
                    _isInitialized = true;
                    _initFailureReason = "";
                    errorMessage = "";
                    LogSafetyConfigurationOnce(ffmpegSharedDir);
                    return true;
                }
                catch (Exception ex)
                {
                    _isInitialized = false;
                    _initFailureReason =
                        $"autogen init failed: {ex.GetType().Name}: {ex.Message}";
                    errorMessage = _initFailureReason;
                    ThumbnailRuntimeLog.Write("thumbnail", _initFailureReason);
                    return false;
                }
            }
        }

        private static string ResolveFfmpegSharedDirectory()
        {
            string configuredPath = ThumbnailEnvConfig.GetFfmpegExePath()?.Trim().Trim('"') ?? "";
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                if (Directory.Exists(configuredPath))
                {
                    return configuredPath;
                }

                if (File.Exists(configuredPath))
                {
                    string fromFile = Path.GetDirectoryName(configuredPath) ?? "";
                    if (!string.IsNullOrWhiteSpace(fromFile) && Directory.Exists(fromFile))
                    {
                        return fromFile;
                    }
                }
            }

            string bundled = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg-shared");
            if (Directory.Exists(bundled))
            {
                return bundled;
            }

            throw new DirectoryNotFoundException(
                "ffmpeg shared directory not found. expected tools/ffmpeg-shared or IMM_FFMPEG_EXE_PATH"
            );
        }

        private static string BuildInitFailedMessage(string initError, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(initError))
            {
                return initError;
            }

            return string.IsNullOrWhiteSpace(fallback) ? "autogen initialization failed" : fallback;
        }

        // swscale 以外の FFmpeg native 呼び出しでも競合が疑われるため、
        // autogen 実行全体は既定で1本ずつ通す。必要なら環境変数で緩められる。
        private static SemaphoreSlim CreateEngineExecutionGate()
        {
            return new SemaphoreSlim(EngineParallelLimit, EngineParallelLimit);
        }

        // swscale-8.dll 側で同時実行中に AccessViolation が出ることがあるため、
        // swscale に触る箇所だけ小さく直列化して native crash を避ける。
        private static SemaphoreSlim CreateNativeScaleGate()
        {
            return new SemaphoreSlim(NativeScaleParallelLimit, NativeScaleParallelLimit);
        }

        // 環境変数で緩める余地は残しつつ、既定は安全側へ倒す。
        private static int ResolveParallelLimit(string envName, int defaultValue)
        {
            string raw = Environment.GetEnvironmentVariable(envName)?.Trim() ?? "";
            if (
                !string.IsNullOrWhiteSpace(raw)
                && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            )
            {
                return Math.Clamp(parsed, 1, 8);
            }

            return defaultValue;
        }

        // 起動後の調査で今どの安全弁が効いているかをすぐ読めるようにする。
        private static void LogSafetyConfigurationOnce(string ffmpegSharedDir)
        {
            if (Interlocked.Exchange(ref _safetyConfigLogged, 1) != 0)
            {
                return;
            }

            ThumbnailRuntimeLog.Write(
                "thumbnail",
                $"autogen safety config: engine_parallel={EngineParallelLimit}, native_parallel={NativeScaleParallelLimit}, sws_path=decoded-frame+sws_scale_frame, ffmpeg_root='{ffmpegSharedDir}'"
            );
        }

        private static unsafe SwsContext* CreateSwsContextSafe(
            int srcWidth,
            int srcHeight,
            AVPixelFormat srcFormat,
            int dstWidth,
            int dstHeight,
            AVPixelFormat dstFormat,
            int flags
        )
        {
            NativeScaleGate.Wait();
            try
            {
                return ffmpeg.sws_getContext(
                    srcWidth,
                    srcHeight,
                    srcFormat,
                    dstWidth,
                    dstHeight,
                    dstFormat,
                    flags,
                    null,
                    null,
                    null
                );
            }
            finally
            {
                NativeScaleGate.Release();
            }
        }

        private static unsafe void FreeSwsContextSafe(SwsContext* pSwsContext)
        {
            if (pSwsContext == null)
            {
                return;
            }

            NativeScaleGate.Wait();
            try
            {
                ffmpeg.sws_freeContext(pSwsContext);
            }
            finally
            {
                NativeScaleGate.Release();
            }
        }

        // コーデック初期化時の pix_fmt ではなく、実際にデコードできたフレーム情報から
        // sws context を作り直す。ここがズレると swscale 側で native crash しやすい。
        private static unsafe bool TryRecreateSwsContextForFrame(
            ref SwsContext* pSwsContext,
            AVFrame* pFrame,
            out string errorMessage
        )
        {
            errorMessage = "";
            if (pFrame == null || pFrame->width < 1 || pFrame->height < 1)
            {
                errorMessage = "decoded frame metadata is invalid";
                return false;
            }

            AVPixelFormat sourcePixelFormat = (AVPixelFormat)pFrame->format;
            if (sourcePixelFormat == AVPixelFormat.AV_PIX_FMT_NONE)
            {
                errorMessage = "decoded frame pixel format is none";
                return false;
            }

            if (pSwsContext != null)
            {
                FreeSwsContextSafe(pSwsContext);
                pSwsContext = null;
            }

            pSwsContext = CreateSwsContextSafe(
                pFrame->width,
                pFrame->height,
                sourcePixelFormat,
                pFrame->width,
                pFrame->height,
                AVPixelFormat.AV_PIX_FMT_BGR24,
                1
            );
            if (pSwsContext == null)
            {
                errorMessage = "Failed to create sws context from decoded frame";
                return false;
            }

            return true;
        }

        // 非正方ピクセル素材でも黒枠判定を誤らないよう、FFmpeg が解決した DAR を使う。
        private static unsafe double? ResolveDisplayAspectRatio(
            AVFormatContext* pFormatContext,
            AVStream* pStream,
            AVFrame* pFrame
        )
        {
            if (pStream == null)
            {
                return null;
            }

            int width = pFrame != null && pFrame->width > 0 ? pFrame->width : pStream->codecpar->width;
            int height =
                pFrame != null && pFrame->height > 0 ? pFrame->height : pStream->codecpar->height;
            if (width < 1 || height < 1)
            {
                return null;
            }

            AVRational sar = ffmpeg.av_guess_sample_aspect_ratio(pFormatContext, pStream, pFrame);
            if (sar.num <= 0 || sar.den <= 0)
            {
                sar = pStream->sample_aspect_ratio;
            }

            if (sar.num > 0 && sar.den > 0)
            {
                double dar = ((double)width * sar.num) / (height * sar.den);
                if (dar > 0 && !double.IsNaN(dar) && !double.IsInfinity(dar))
                {
                    return dar;
                }
            }

            double sourceAspect = ThumbnailCreationService.ResolveAspectRatio(new Size(width, height));
            return sourceAspect > 0 ? sourceAspect : null;
        }

        public bool CanHandle(ThumbnailJobContext context)
        {
            return EnsureFfmpegInitializedSafe(out _);
        }

        public Task<ThumbnailCreateResult> CreateAsync(
            ThumbnailJobContext context,
            CancellationToken cts = default
        )
        {
            return Task.Run(
                () =>
                {
                    if (!EnsureFfmpegInitializedSafe(out string initError))
                    {
                        return ThumbnailCreationService.CreateFailedResult(
                            context?.SaveThumbFileName ?? "",
                            context?.DurationSec,
                            BuildInitFailedMessage(initError, _initFailureReason)
                        );
                    }

                    EngineExecutionGate.Wait(cts);
                    try
                    {
                        return CreateInternal(context, cts);
                    }
                    finally
                    {
                        EngineExecutionGate.Release();
                    }
                },
                cts
            );
        }

        public Task<bool> CreateBookmarkAsync(
            string movieFullPath,
            string saveThumbPath,
            int capturePos,
            CancellationToken cts = default
        )
        {
            return Task.Run(
                () =>
                {
                    if (!EnsureFfmpegInitializedSafe(out _))
                    {
                        return false;
                    }

                    EngineExecutionGate.Wait(cts);
                    try
                    {
                        return CreateBookmarkInternal(movieFullPath, saveThumbPath, capturePos, cts);
                    }
                    finally
                    {
                        EngineExecutionGate.Release();
                    }
                },
                cts
            );
        }

        private unsafe ThumbnailCreateResult CreateInternal(
            ThumbnailJobContext context,
            CancellationToken cts
        )
        {
            if (context == null)
                return ThumbnailCreationService.CreateFailedResult("", null, "context is null");
            if (
                context.ThumbInfo == null
                || context.ThumbInfo.ThumbSec == null
                || context.ThumbInfo.ThumbSec.Count < 1
            )
                return ThumbnailCreationService.CreateFailedResult(
                    context.SaveThumbFileName,
                    context.DurationSec,
                    "thumb info is empty"
                );

            double? durationSec = context.DurationSec;
            if (!durationSec.HasValue || durationSec.Value <= 0)
            {
                durationSec = ThumbnailCreationService.TryGetDurationSecFromShell(
                    context.MovieFullPath
                );
            }

            int cols = context.PanelColumns;
            int rows = context.PanelRows;
            if (cols < 1 || rows < 1)
                return ThumbnailCreationService.CreateFailedResult(
                    context.SaveThumbFileName,
                    durationSec,
                    "invalid panel configuration"
                );

            string saveDir = Path.GetDirectoryName(context.SaveThumbFileName) ?? "";
            if (!string.IsNullOrWhiteSpace(saveDir))
            {
                Directory.CreateDirectory(saveDir);
            }

            var captureSecs = new List<double>();
            foreach (var sec in context.ThumbInfo.ThumbSec)
            {
                captureSecs.Add(sec);
            }

            int targetWidth = context.PanelWidth > 0 ? context.PanelWidth : 320;
            int targetHeight = context.PanelHeight > 0 ? context.PanelHeight : 240;

            AVFormatContext* pFormatContext = null;
            AVCodecContext* pCodecContext = null;
            AVFrame* pFrame = null;
            AVPacket* pPacket = null;
            SwsContext* pSwsContext = null;
            List<Bitmap> bitmaps = [];

            try
            {
                pFormatContext = ffmpeg.avformat_alloc_context();
                int ret = ffmpeg.avformat_open_input(
                    &pFormatContext,
                    context.MovieFullPath,
                    null,
                    null
                );
                if (ret < 0)
                {
                    return ThumbnailCreationService.CreateFailedResult(
                        context.SaveThumbFileName,
                        durationSec,
                        "Failed to open input: " + GetErrorMessage(ret)
                    );
                }

                ret = ffmpeg.avformat_find_stream_info(pFormatContext, null);
                if (ret < 0)
                {
                    return ThumbnailCreationService.CreateFailedResult(
                        context.SaveThumbFileName,
                        durationSec,
                        "Failed to find stream info: " + GetErrorMessage(ret)
                    );
                }

                AVStream* pStream = null;
                for (int i = 0; i < pFormatContext->nb_streams; i++)
                {
                    if (
                        pFormatContext->streams[i]->codecpar->codec_type
                        == AVMediaType.AVMEDIA_TYPE_VIDEO
                    )
                    {
                        pStream = pFormatContext->streams[i];
                        break;
                    }
                }

                if (pStream == null)
                    return ThumbnailCreationService.CreateFailedResult(
                        context.SaveThumbFileName,
                        durationSec,
                        "Video stream not found"
                    );

                var codecId = pStream->codecpar->codec_id;
                var pCodec = ffmpeg.avcodec_find_decoder(codecId);
                if (pCodec == null)
                    return ThumbnailCreationService.CreateFailedResult(
                        context.SaveThumbFileName,
                        durationSec,
                        "Decoder not found"
                    );

                // Get true duration from stream if possible
                if (pStream->duration > 0)
                {
                    double streamDur = pStream->duration * ffmpeg.av_q2d(pStream->time_base);
                    if (streamDur > 0)
                        durationSec = streamDur;
                }

                pCodecContext = ffmpeg.avcodec_alloc_context3(pCodec);
                ffmpeg.avcodec_parameters_to_context(pCodecContext, pStream->codecpar);

                // Hardware decoding could be injected here if needed

                ret = ffmpeg.avcodec_open2(pCodecContext, pCodec, null);
                if (ret < 0)
                    return ThumbnailCreationService.CreateFailedResult(
                        context.SaveThumbFileName,
                        durationSec,
                        "Failed to open codec: " + GetErrorMessage(ret)
                    );

                pPacket = ffmpeg.av_packet_alloc();
                pFrame = ffmpeg.av_frame_alloc();

                foreach (double sec in captureSecs)
                {
                    cts.ThrowIfCancellationRequested();
                    if (
                        TryCaptureFrameAtSecond(
                            sec,
                            pFormatContext,
                            pStream,
                            pCodecContext,
                            pPacket,
                            pFrame,
                            ref pSwsContext,
                            targetWidth,
                            targetHeight,
                            cts,
                            out Bitmap capturedBitmap
                        )
                    )
                    {
                        bitmaps.Add(capturedBitmap);
                    }
                }

                if (bitmaps.Count < 1)
                {
                    AutogenHeaderFallbackResult headerFallback = TryHeaderFrameFallback(
                        context.MovieFullPath,
                        durationSec,
                        captureSecs,
                        cols,
                        rows,
                        pFormatContext,
                        pStream,
                        pCodecContext,
                        pPacket,
                        pFrame,
                        ref pSwsContext,
                        targetWidth,
                        targetHeight,
                        cts
                    );
                    if (!headerFallback.IsSuccess)
                    {
                        return ThumbnailCreationService.CreateFailedResult(
                            context.SaveThumbFileName,
                            durationSec,
                            "No frames decoded"
                        );
                    }

                    bitmaps.AddRange(headerFallback.Bitmaps);
                }

                ThumbnailPreviewFrame previewFrame = null;
                if (bitmaps.Count > 0)
                {
                    // 先頭フレームをミニパネル先行表示用に抜き出す。
                    previewFrame = ThumbnailCreationService.CreatePreviewFrameFromBitmap(
                        bitmaps[0],
                        120
                    );
                    SaveCombinedThumbnail(
                        bitmaps,
                        cols,
                        rows,
                        targetWidth,
                        targetHeight,
                        context.SaveThumbFileName
                    );
                }
                return ThumbnailCreationService.CreateSuccessResult(
                    context.SaveThumbFileName,
                    durationSec,
                    previewFrame
                );
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return ThumbnailCreationService.CreateFailedResult(
                    context.SaveThumbFileName,
                    durationSec,
                    ex.Message
                );
            }
            finally
            {
                if (pFrame != null)
                    ffmpeg.av_frame_free(&pFrame);
                if (pPacket != null)
                    ffmpeg.av_packet_free(&pPacket);
                if (pCodecContext != null)
                    ffmpeg.avcodec_free_context(&pCodecContext);
                if (pFormatContext != null)
                    ffmpeg.avformat_close_input(&pFormatContext);
                if (pSwsContext != null)
                    FreeSwsContextSafe(pSwsContext);
                foreach (Bitmap bmp in bitmaps)
                {
                    bmp.Dispose();
                }
            }
        }

        // 通常代表秒の seek 1 回分を閉じ込めて、主経路と header fallback で同じ取得手順を使う。
        private unsafe bool TryCaptureFrameAtSecond(
            double sec,
            AVFormatContext* pFormatContext,
            AVStream* pStream,
            AVCodecContext* pCodecContext,
            AVPacket* pPacket,
            AVFrame* pFrame,
            ref SwsContext* pSwsContext,
            int targetWidth,
            int targetHeight,
            CancellationToken cts,
            out Bitmap capturedBitmap
        )
        {
            capturedBitmap = null;

            long ts = (long)(sec / ffmpeg.av_q2d(pStream->time_base));
            ffmpeg.av_seek_frame(pFormatContext, pStream->index, ts, ffmpeg.AVSEEK_FLAG_BACKWARD);
            ffmpeg.avcodec_flush_buffers(pCodecContext);

            bool frameCaptured = false;
            bool streamEnded = false;
            while (
                !frameCaptured
                && !streamEnded
                && ffmpeg.av_read_frame(pFormatContext, pPacket) >= 0
            )
            {
                cts.ThrowIfCancellationRequested();
                try
                {
                    if (pPacket->stream_index != pStream->index)
                    {
                        continue;
                    }

                    int ret = ffmpeg.avcodec_send_packet(pCodecContext, pPacket);
                    if (
                        ret < 0
                        && ret != ffmpeg.AVERROR(ffmpeg.EAGAIN)
                        && ret != ffmpeg.AVERROR_EOF
                    )
                    {
                        continue;
                    }

                    while (true)
                    {
                        ret = ffmpeg.avcodec_receive_frame(pCodecContext, pFrame);
                        if (ret == 0)
                        {
                            if (
                                !TryRecreateSwsContextForFrame(
                                    ref pSwsContext,
                                    pFrame,
                                    out _
                                )
                            )
                            {
                                return false;
                            }

                            using Bitmap bmp = ConvertFrameToBitmap(
                                pFrame,
                                pSwsContext,
                                pFrame->width,
                                pFrame->height
                            );
                            if (bmp != null)
                            {
                                double? displayAspectRatio = ResolveDisplayAspectRatio(
                                    pFormatContext,
                                    pStream,
                                    pFrame
                                );
                                capturedBitmap = ThumbnailCreationService.ResizeBitmap(
                                    bmp,
                                    new Size(targetWidth, targetHeight),
                                    displayAspectRatio
                                );
                                frameCaptured = capturedBitmap != null;
                            }

                            ffmpeg.av_frame_unref(pFrame);
                            break;
                        }

                        if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                        {
                            break;
                        }

                        if (ret == ffmpeg.AVERROR_EOF)
                        {
                            streamEnded = true;
                            break;
                        }

                        break;
                    }
                }
                finally
                {
                    ffmpeg.av_packet_unref(pPacket);
                }
            }

            ffmpeg.av_frame_unref(pFrame);
            return capturedBitmap != null;
        }

        // 通常代表秒が全滅した時だけ、先頭付近の 1 枚を浅く探してタイルへ複製する。
        private unsafe AutogenHeaderFallbackResult TryHeaderFrameFallback(
            string movieFullPath,
            double? durationSec,
            IReadOnlyList<double> captureSecs,
            int cols,
            int rows,
            AVFormatContext* pFormatContext,
            AVStream* pStream,
            AVCodecContext* pCodecContext,
            AVPacket* pPacket,
            AVFrame* pFrame,
            ref SwsContext* pSwsContext,
            int targetWidth,
            int targetHeight,
            CancellationToken cts
        )
        {
            ThumbnailRuntimeLog.Write(
                "autogen-header-frame-fallback",
                $"fallback requested: movie='{movieFullPath}' duration_sec={durationSec:0.###} cols={cols} rows={rows} "
                    + $"thumb_sec=[{string.Join(",", captureSecs.Select(x => x.ToString("0.###", CultureInfo.InvariantCulture)))}]"
            );

            List<double> candidates = BuildHeaderFallbackCandidateSeconds(durationSec);
            ThumbnailRuntimeLog.Write(
                "autogen-header-frame-fallback",
                $"fallback candidates: movie='{movieFullPath}' duration_sec={durationSec:0.###} candidates=[{string.Join(",", candidates.Select(x => x.ToString("0.###", CultureInfo.InvariantCulture)))}]"
            );

            foreach (double sec in candidates)
            {
                cts.ThrowIfCancellationRequested();
                ThumbnailRuntimeLog.Write(
                    "autogen-header-frame-fallback",
                    $"fallback try: movie='{movieFullPath}' sec={sec:0.###}"
                );

                if (
                    !TryCaptureFrameAtSecond(
                        sec,
                        pFormatContext,
                        pStream,
                        pCodecContext,
                        pPacket,
                        pFrame,
                        ref pSwsContext,
                        targetWidth,
                        targetHeight,
                        cts,
                        out Bitmap capturedBitmap
                    )
                )
                {
                    continue;
                }

                if (ThumbnailNearBlackDetector.IsNearBlackBitmap(capturedBitmap, out double averageLuma))
                {
                    ThumbnailRuntimeLog.Write(
                        "autogen-header-frame-fallback",
                        $"fallback reject near-black: movie='{movieFullPath}' sec={sec:0.###} avg_luma={averageLuma:0.##}"
                    );
                    capturedBitmap.Dispose();
                    continue;
                }

                ThumbnailRuntimeLog.Write(
                    "autogen-header-frame-fallback",
                    $"fallback hit: movie='{movieFullPath}' sec={sec:0.###}"
                );
                ThumbnailRuntimeLog.Write(
                    "autogen-header-frame-fallback",
                    $"fallback tile replicate: movie='{movieFullPath}' tile_count={cols * rows}"
                );

                try
                {
                    List<Bitmap> replicated = [];
                    for (int i = 0; i < cols * rows; i++)
                    {
                        replicated.Add((Bitmap)capturedBitmap.Clone());
                    }

                    return AutogenHeaderFallbackResult.Success(replicated);
                }
                finally
                {
                    capturedBitmap.Dispose();
                }
            }

            ThumbnailRuntimeLog.Write(
                "autogen-header-frame-fallback",
                $"fallback exhausted: movie='{movieFullPath}' candidates=[{string.Join(",", candidates.Select(x => x.ToString("0.###", CultureInfo.InvariantCulture)))}]"
            );
            return AutogenHeaderFallbackResult.Miss();
        }

        // 先頭の黒味やヘッダ直後の安定フレームを拾えるよう、浅い候補列だけを固定で持つ。
        internal static List<double> BuildHeaderFallbackCandidateSeconds(double? durationSec)
        {
            double[] baseCandidates = [0d, 0.1d, 0.25d, 0.5d, 1d, 2d];
            double maxSec = durationSec.HasValue && durationSec.Value > 0
                ? Math.Max(0d, durationSec.Value - 0.001d)
                : 2d;
            HashSet<long> seen = [];
            List<double> result = [];

            foreach (double candidate in baseCandidates)
            {
                double normalized = Math.Max(0d, Math.Min(candidate, maxSec));
                long key = (long)Math.Round(normalized * 1000d);
                if (!seen.Add(key))
                {
                    continue;
                }

                result.Add(normalized);
            }

            return result;
        }

        private sealed class AutogenHeaderFallbackResult
        {
            private AutogenHeaderFallbackResult(bool isSuccess, List<Bitmap> bitmaps)
            {
                IsSuccess = isSuccess;
                Bitmaps = bitmaps ?? [];
            }

            public bool IsSuccess { get; }
            public List<Bitmap> Bitmaps { get; }

            public static AutogenHeaderFallbackResult Success(List<Bitmap> bitmaps)
            {
                return new AutogenHeaderFallbackResult(true, bitmaps);
            }

            public static AutogenHeaderFallbackResult Miss()
            {
                return new AutogenHeaderFallbackResult(false, []);
            }
        }

        private unsafe bool CreateBookmarkInternal(
            string movieFullPath,
            string saveThumbPath,
            int capturePos,
            CancellationToken cts
        )
        {
            cts.ThrowIfCancellationRequested();

            string saveDir = Path.GetDirectoryName(saveThumbPath) ?? "";
            if (!string.IsNullOrWhiteSpace(saveDir))
            {
                Directory.CreateDirectory(saveDir);
            }

            int targetWidth = 640;
            int targetHeight = 480;

            AVFormatContext* pFormatContext = null;
            AVCodecContext* pCodecContext = null;
            AVFrame* pFrame = null;
            AVPacket* pPacket = null;
            SwsContext* pSwsContext = null;

            try
            {
                pFormatContext = ffmpeg.avformat_alloc_context();
                if (ffmpeg.avformat_open_input(&pFormatContext, movieFullPath, null, null) < 0)
                    return false;
                if (ffmpeg.avformat_find_stream_info(pFormatContext, null) < 0)
                    return false;

                AVStream* pStream = null;
                for (int i = 0; i < pFormatContext->nb_streams; i++)
                {
                    if (
                        pFormatContext->streams[i]->codecpar->codec_type
                        == AVMediaType.AVMEDIA_TYPE_VIDEO
                    )
                    {
                        pStream = pFormatContext->streams[i];
                        break;
                    }
                }
                if (pStream == null)
                    return false;

                var pCodec = ffmpeg.avcodec_find_decoder(pStream->codecpar->codec_id);
                if (pCodec == null)
                    return false;

                pCodecContext = ffmpeg.avcodec_alloc_context3(pCodec);
                ffmpeg.avcodec_parameters_to_context(pCodecContext, pStream->codecpar);
                if (ffmpeg.avcodec_open2(pCodecContext, pCodec, null) < 0)
                    return false;

                pPacket = ffmpeg.av_packet_alloc();
                pFrame = ffmpeg.av_frame_alloc();

                long ts = (long)(capturePos / ffmpeg.av_q2d(pStream->time_base));
                ffmpeg.av_seek_frame(
                    pFormatContext,
                    pStream->index,
                    ts,
                    ffmpeg.AVSEEK_FLAG_BACKWARD
                );
                ffmpeg.avcodec_flush_buffers(pCodecContext);

                Bitmap extracted = null;
                bool streamEnded = false;
                while (!streamEnded && ffmpeg.av_read_frame(pFormatContext, pPacket) >= 0)
                {
                    cts.ThrowIfCancellationRequested();
                    try
                    {
                        if (pPacket->stream_index != pStream->index)
                        {
                            continue;
                        }

                        int sendRet = ffmpeg.avcodec_send_packet(pCodecContext, pPacket);
                        if (
                            sendRet < 0
                            && sendRet != ffmpeg.AVERROR(ffmpeg.EAGAIN)
                            && sendRet != ffmpeg.AVERROR_EOF
                        )
                        {
                            continue;
                        }

                        while (true)
                        {
                            int ret = ffmpeg.avcodec_receive_frame(pCodecContext, pFrame);
                            if (ret == 0)
                            {
                                if (
                                    !TryRecreateSwsContextForFrame(
                                        ref pSwsContext,
                                        pFrame,
                                        out _
                                    )
                                )
                                {
                                    return false;
                                }

                                using Bitmap rawFrame = ConvertFrameToBitmap(
                                    pFrame,
                                    pSwsContext,
                                    pFrame->width,
                                    pFrame->height
                                );
                                if (rawFrame != null)
                                {
                                    double? displayAspectRatio = ResolveDisplayAspectRatio(
                                        pFormatContext,
                                        pStream,
                                        pFrame
                                    );
                                    extracted = ThumbnailCreationService.ResizeBitmap(
                                        rawFrame,
                                        new Size(targetWidth, targetHeight),
                                        displayAspectRatio
                                    );
                                }
                                ffmpeg.av_frame_unref(pFrame);
                                break;
                            }

                            if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                            {
                                break;
                            }

                            if (ret == ffmpeg.AVERROR_EOF)
                            {
                                streamEnded = true;
                                break;
                            }

                            break;
                        }
                    }
                    finally
                    {
                        ffmpeg.av_packet_unref(pPacket);
                    }

                    if (extracted != null)
                    {
                        break;
                    }
                }

                ffmpeg.av_frame_unref(pFrame);

                if (extracted != null)
                {
                    using (extracted)
                    {
                        if (
                            !ThumbnailImageWriter.TrySaveJpegWithRetry(
                                extracted,
                                saveThumbPath,
                                out _
                            )
                        )
                        {
                            return false;
                        }
                    }
                    return true;
                }
                return false;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (pFrame != null)
                    ffmpeg.av_frame_free(&pFrame);
                if (pPacket != null)
                    ffmpeg.av_packet_free(&pPacket);
                if (pCodecContext != null)
                    ffmpeg.avcodec_free_context(&pCodecContext);
                if (pFormatContext != null)
                    ffmpeg.avformat_close_input(&pFormatContext);
                if (pSwsContext != null)
                    FreeSwsContextSafe(pSwsContext);
            }
        }

        private unsafe Bitmap ConvertFrameToBitmap(
            AVFrame* pFrame,
            SwsContext* pSwsContext,
            int width,
            int height
        )
        {
            Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            BitmapData bitmapData = null;
            AVFrame* pBgrFrame = null;
            bool gateEntered = false;
            try
            {
                pBgrFrame = ffmpeg.av_frame_alloc();
                if (pBgrFrame == null)
                {
                    bitmap.Dispose();
                    return null;
                }

                // FFmpeg 管理の出力フレームへ変換し、managed 配列マーシャリングを避ける。
                pBgrFrame->format = (int)AVPixelFormat.AV_PIX_FMT_BGR24;
                pBgrFrame->width = width;
                pBgrFrame->height = height;
                if (ffmpeg.av_frame_get_buffer(pBgrFrame, 1) < 0)
                {
                    bitmap.Dispose();
                    return null;
                }

                NativeScaleGate.Wait();
                gateEntered = true;
                int scaleResult = ffmpeg.sws_scale_frame(pSwsContext, pBgrFrame, pFrame);
                if (scaleResult < 0)
                {
                    bitmap.Dispose();
                    return null;
                }

                bitmapData = bitmap.LockBits(
                    new Rectangle(0, 0, width, height),
                    ImageLockMode.WriteOnly,
                    PixelFormat.Format24bppRgb
                );

                for (int y = 0; y < height; y++)
                {
                    byte* srcRow = pBgrFrame->data[0] + (y * pBgrFrame->linesize[0]);
                    byte* dstRow = ((byte*)bitmapData.Scan0) + (y * bitmapData.Stride);
                    Buffer.MemoryCopy(srcRow, dstRow, bitmapData.Stride, width * 3);
                }
            }
            finally
            {
                if (gateEntered)
                {
                    NativeScaleGate.Release();
                }
                if (bitmapData != null)
                {
                    bitmap.UnlockBits(bitmapData);
                }
                if (pBgrFrame != null)
                {
                    ffmpeg.av_frame_free(&pBgrFrame);
                }
            }

            return bitmap;
        }

        private void SaveCombinedThumbnail(
            List<Bitmap> frames,
            int cols,
            int rows,
            int thumbW,
            int thumbH,
            string savePath
        )
        {
            int combinedW = cols * thumbW;
            int combinedH = rows * thumbH;

            using Bitmap combined = new Bitmap(combinedW, combinedH, PixelFormat.Format24bppRgb);
            using Graphics g = Graphics.FromImage(combined);
            g.Clear(Color.Black);

            for (int i = 0; i < frames.Count && i < (cols * rows); i++)
            {
                int r = i / cols;
                int c = i % cols;
                int x = c * thumbW;
                int y = r * thumbH;
                g.DrawImage(frames[i], new Rectangle(x, y, thumbW, thumbH));
            }

            if (!ThumbnailImageWriter.TrySaveJpegWithRetry(combined, savePath, out string error))
            {
                throw new IOException($"autogen combined save failed: {error}");
            }
        }

        private static unsafe string GetErrorMessage(int error)
        {
            var bufferSize = 1024;
            var buffer = stackalloc byte[bufferSize];
            ffmpeg.av_strerror(error, buffer, (ulong)bufferSize);
            return Marshal.PtrToStringAnsi((IntPtr)buffer) ?? "Unknown error";
        }
    }
}
