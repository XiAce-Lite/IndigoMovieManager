using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace IndigoMovieManager.Thumbnail.Engines.IndexRepair
{
    /// <summary>
    /// FFmpeg.AutoGen で動画インデックスの判定と再MUX修復を行う。
    /// </summary>
    public sealed class VideoIndexRepairService : IVideoIndexRepairService
    {
        private static readonly string[] AllowedOutputExtensions = [".mp4", ".mkv"];
        private static readonly string[] VideoOnlyRetryInputExtensions = [".avi", ".wmv", ".asf"];
        private static bool _isInitialized;
        private static bool _initAttempted;
        private static string _initFailureReason = "";
        private static readonly object _initLock = new();

        public Task<VideoIndexProbeResult> ProbeAsync(
            string moviePath,
            CancellationToken cts = default
        )
        {
            return Task.Run(() => ProbeInternal(moviePath, cts), cts);
        }

        public Task<VideoIndexRepairResult> RepairAsync(
            string moviePath,
            string outputPath,
            CancellationToken cts = default
        )
        {
            return Task.Run(() => RepairInternal(moviePath, outputPath, cts), cts);
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
                    ? "index repair ffmpeg initialization failed"
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
                        ? "index repair ffmpeg initialization failed"
                        : _initFailureReason;
                    return false;
                }

                _initAttempted = true;
                try
                {
                    // 既存 autogen と同じ探索順で shared DLL の配置先を解決する。
                    string ffmpegSharedDir = ResolveFfmpegSharedDirectory();
                    ffmpeg.RootPath = ffmpegSharedDir;
                    DynamicallyLoadedBindings.Initialize();
                    _isInitialized = true;
                    _initFailureReason = "";
                    errorMessage = "";
                    return true;
                }
                catch (Exception ex)
                {
                    _isInitialized = false;
                    _initFailureReason =
                        $"index repair ffmpeg init failed: {ex.GetType().Name}: {ex.Message}";
                    errorMessage = _initFailureReason;
                    ThumbnailRuntimeLog.Write("index-repair", _initFailureReason);
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

        private static VideoIndexProbeResult ProbeInternal(string moviePath, CancellationToken cts)
        {
            VideoIndexProbeResult result = new()
            {
                MoviePath = moviePath ?? "",
                IsIndexCorruptionDetected = false,
                DetectionReason = "",
                ContainerFormat = "",
                ErrorCode = "",
            };

            if (string.IsNullOrWhiteSpace(moviePath))
            {
                result.DetectionReason = "movie path is empty";
                return result;
            }

            string normalizedMoviePath = moviePath.Trim();
            result.MoviePath = normalizedMoviePath;
            if (!File.Exists(normalizedMoviePath))
            {
                result.DetectionReason = "movie file not found";
                return result;
            }

            cts.ThrowIfCancellationRequested();
            if (!EnsureFfmpegInitializedSafe(out string initError))
            {
                result.DetectionReason = initError;
                return result;
            }

            ProbeOpenResult normalProbe = ProbeOpen(normalizedMoviePath, ignoreIndex: false, cts);
            ProbeOpenResult ignoreProbe = ProbeOpen(normalizedMoviePath, ignoreIndex: true, cts);

            result.ContainerFormat = !string.IsNullOrWhiteSpace(normalProbe.ContainerFormat)
                ? normalProbe.ContainerFormat
                : ignoreProbe.ContainerFormat;

            // 通常オープンが失敗し、IGNIDX 付きで成功する場合だけ repair 候補にする。
            if (!normalProbe.IsSuccess && ignoreProbe.IsSuccess)
            {
                result.IsIndexCorruptionDetected = true;
                result.DetectionReason = "open_failed_but_ignidx_succeeded";
                result.ErrorCode = normalProbe.ErrorCode;
                return result;
            }

            if (
                normalProbe.FindStreamInfoErrorCode < 0
                && ignoreProbe.FindStreamInfoErrorCode >= 0
                && ignoreProbe.IsSuccess
            )
            {
                result.IsIndexCorruptionDetected = true;
                result.DetectionReason = "stream_info_failed_but_ignidx_succeeded";
                result.ErrorCode = FormatErrorCode(normalProbe.FindStreamInfoErrorCode);
                return result;
            }

            result.IsIndexCorruptionDetected = false;
            result.DetectionReason = normalProbe.IsSuccess ? "probe_ok" : normalProbe.Reason;
            result.ErrorCode = normalProbe.ErrorCode;
            return result;
        }

        private static VideoIndexRepairResult RepairInternal(
            string moviePath,
            string outputPath,
            CancellationToken cts
        )
        {
            VideoIndexRepairResult result = new()
            {
                IsSuccess = false,
                InputPath = moviePath ?? "",
                OutputPath = outputPath ?? "",
                UsedTemporaryRemux = true,
                ErrorMessage = "",
            };

            if (string.IsNullOrWhiteSpace(moviePath) || string.IsNullOrWhiteSpace(outputPath))
            {
                result.ErrorMessage = "movie path or output path is empty";
                return result;
            }

            string inputFullPath = Path.GetFullPath(moviePath.Trim());
            string outputFullPath = Path.GetFullPath(outputPath.Trim());
            result.InputPath = inputFullPath;
            result.OutputPath = outputFullPath;

            if (!File.Exists(inputFullPath))
            {
                result.ErrorMessage = "movie file not found";
                return result;
            }

            if (string.Equals(inputFullPath, outputFullPath, StringComparison.OrdinalIgnoreCase))
            {
                result.ErrorMessage = "input and output path must be different";
                return result;
            }

            string ext = Path.GetExtension(outputFullPath);
            if (!AllowedOutputExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            {
                result.ErrorMessage = "output extension must be .mp4 or .mkv";
                return result;
            }

            cts.ThrowIfCancellationRequested();
            if (!EnsureFfmpegInitializedSafe(out string initError))
            {
                result.ErrorMessage = initError;
                return result;
            }

            string outputDir = Path.GetDirectoryName(outputFullPath) ?? "";
            if (!string.IsNullOrWhiteSpace(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            if (
                TryRemuxCopy(inputFullPath, outputFullPath, cts, out string remuxError)
                && File.Exists(outputFullPath)
            )
            {
                result.IsSuccess = true;
                result.ErrorMessage = "";
                return result;
            }

            result.ErrorMessage = string.IsNullOrWhiteSpace(remuxError)
                ? "index repair remux failed"
                : remuxError;
            TryDeleteFileQuietly(outputFullPath);
            return result;
        }

        private static unsafe ProbeOpenResult ProbeOpen(
            string moviePath,
            bool ignoreIndex,
            CancellationToken cts
        )
        {
            AVFormatContext* pFormatContext = ffmpeg.avformat_alloc_context();
            if (pFormatContext == null)
            {
                return new ProbeOpenResult(
                    IsSuccess: false,
                    Reason: "avformat_alloc_context failed",
                    ErrorCode: "alloc_failed",
                    ContainerFormat: "",
                    FindStreamInfoErrorCode: -1
                );
            }

            if (ignoreIndex)
            {
                pFormatContext->flags |= ffmpeg.AVFMT_FLAG_IGNIDX;
            }

            int findStreamInfoErrorCode = -1;
            string containerFormat = "";
            try
            {
                cts.ThrowIfCancellationRequested();
                int openRet = ffmpeg.avformat_open_input(&pFormatContext, moviePath, null, null);
                if (openRet < 0)
                {
                    return new ProbeOpenResult(
                        IsSuccess: false,
                        Reason: "avformat_open_input failed",
                        ErrorCode: FormatErrorCode(openRet),
                        ContainerFormat: "",
                        FindStreamInfoErrorCode: findStreamInfoErrorCode
                    );
                }

                if (pFormatContext->iformat != null)
                {
                    containerFormat =
                        Marshal.PtrToStringAnsi((IntPtr)pFormatContext->iformat->name) ?? "";
                }

                int infoRet = ffmpeg.avformat_find_stream_info(pFormatContext, null);
                findStreamInfoErrorCode = infoRet;
                if (infoRet < 0)
                {
                    return new ProbeOpenResult(
                        IsSuccess: false,
                        Reason: "avformat_find_stream_info failed",
                        ErrorCode: FormatErrorCode(infoRet),
                        ContainerFormat: containerFormat,
                        FindStreamInfoErrorCode: findStreamInfoErrorCode
                    );
                }

                bool hasVideo = false;
                for (int i = 0; i < pFormatContext->nb_streams; i++)
                {
                    AVStream* stream = pFormatContext->streams[i];
                    if (
                        stream != null
                        && stream->codecpar != null
                        && stream->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO
                    )
                    {
                        hasVideo = true;
                        break;
                    }
                }

                if (!hasVideo)
                {
                    return new ProbeOpenResult(
                        IsSuccess: false,
                        Reason: "video stream not found",
                        ErrorCode: "video_stream_not_found",
                        ContainerFormat: containerFormat,
                        FindStreamInfoErrorCode: findStreamInfoErrorCode
                    );
                }

                return new ProbeOpenResult(
                    IsSuccess: true,
                    Reason: "ok",
                    ErrorCode: "",
                    ContainerFormat: containerFormat,
                    FindStreamInfoErrorCode: findStreamInfoErrorCode
                );
            }
            finally
            {
                if (pFormatContext != null)
                {
                    ffmpeg.avformat_close_input(&pFormatContext);
                }
            }
        }

        // 壊れたインデックスを信用せず、-c copy 相当でコンテナだけ組み直す。
        private static unsafe bool TryRemuxCopy(
            string inputPath,
            string outputPath,
            CancellationToken cts,
            out string errorMessage
        )
        {
            if (TryRemuxCopyCore(inputPath, outputPath, includeNonVideoStreams: true, cts, out errorMessage))
            {
                return true;
            }

            if (!ShouldRetryRemuxAsVideoOnly(inputPath, errorMessage))
            {
                return false;
            }

            string firstError = errorMessage;
            TryDeleteFileQuietly(outputPath);
            ThumbnailRuntimeLog.Write(
                "index-repair",
                $"video-only remux retry: input='{inputPath}', output='{outputPath}', reason='{firstError}'"
            );
            if (
                TryRemuxCopyCore(
                    inputPath,
                    outputPath,
                    includeNonVideoStreams: false,
                    cts,
                    out string videoOnlyError
                )
            )
            {
                errorMessage = "";
                return true;
            }

            errorMessage =
                $"video_only_retry failed: first='{firstError}', second='{videoOnlyError}'";
            return false;
        }

        // 古い WMV/ASF は音声 DTS で muxer が止まる個体があるため、
        // 一度だけ video-only remux を試してサムネ用の作業入力を確保する。
        internal static bool ShouldRetryRemuxAsVideoOnly(string inputPath, string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(inputPath) || string.IsNullOrWhiteSpace(errorMessage))
            {
                return false;
            }

            string extension = Path.GetExtension(inputPath);
            if (!VideoOnlyRetryInputExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            string normalized = errorMessage.ToLowerInvariant();
            return normalized.Contains("av_interleaved_write_frame failed")
                || normalized.Contains("av_write_trailer failed");
        }

        private static unsafe bool TryRemuxCopyCore(
            string inputPath,
            string outputPath,
            bool includeNonVideoStreams,
            CancellationToken cts,
            out string errorMessage
        )
        {
            AVFormatContext* inFmt = null;
            AVFormatContext* outFmt = null;
            AVPacket* packet = null;
            int[] streamMap = [];
            int mappedStreamCount = 0;
            long[] lastWrittenDts = [];
            bool openedOutputIo = false;
            errorMessage = "";

            try
            {
                inFmt = ffmpeg.avformat_alloc_context();
                if (inFmt == null)
                {
                    errorMessage = "avformat_alloc_context failed";
                    return false;
                }

                inFmt->flags |= ffmpeg.AVFMT_FLAG_IGNIDX;

                int ret = ffmpeg.avformat_open_input(&inFmt, inputPath, null, null);
                if (ret < 0)
                {
                    errorMessage = "open input failed: " + FormatErrorCode(ret);
                    return false;
                }

                ret = ffmpeg.avformat_find_stream_info(inFmt, null);
                if (ret < 0)
                {
                    errorMessage = "find stream info failed: " + FormatErrorCode(ret);
                    return false;
                }

                ret = ffmpeg.avformat_alloc_output_context2(&outFmt, null, null, outputPath);
                if (ret < 0 || outFmt == null)
                {
                    errorMessage = "alloc output context failed: " + FormatErrorCode(ret);
                    return false;
                }

                streamMap = new int[inFmt->nb_streams];
                Array.Fill(streamMap, -1);

                for (int i = 0; i < inFmt->nb_streams; i++)
                {
                    AVStream* inStream = inFmt->streams[i];
                    if (inStream == null || inStream->codecpar == null)
                    {
                        continue;
                    }

                    AVMediaType codecType = inStream->codecpar->codec_type;
                    if (
                        codecType != AVMediaType.AVMEDIA_TYPE_VIDEO
                        && (
                            !includeNonVideoStreams
                            || (
                                codecType != AVMediaType.AVMEDIA_TYPE_AUDIO
                                && codecType != AVMediaType.AVMEDIA_TYPE_SUBTITLE
                            )
                        )
                    )
                    {
                        continue;
                    }

                    AVStream* outStream = ffmpeg.avformat_new_stream(outFmt, null);
                    if (outStream == null)
                    {
                        errorMessage = "avformat_new_stream failed";
                        return false;
                    }

                    streamMap[i] = mappedStreamCount++;
                    ret = ffmpeg.avcodec_parameters_copy(outStream->codecpar, inStream->codecpar);
                    if (ret < 0)
                    {
                        errorMessage = "avcodec_parameters_copy failed: " + FormatErrorCode(ret);
                        return false;
                    }

                    outStream->codecpar->codec_tag = 0;
                    outStream->time_base = inStream->time_base;
                }

                if (mappedStreamCount < 1)
                {
                    errorMessage = "no remuxable stream found";
                    return false;
                }

                lastWrittenDts = Enumerable.Repeat(long.MinValue, mappedStreamCount).ToArray();

                if ((outFmt->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0)
                {
                    ret = ffmpeg.avio_open(&outFmt->pb, outputPath, ffmpeg.AVIO_FLAG_WRITE);
                    if (ret < 0)
                    {
                        errorMessage = "avio_open failed: " + FormatErrorCode(ret);
                        return false;
                    }
                    openedOutputIo = true;
                }

                ret = ffmpeg.avformat_write_header(outFmt, null);
                if (ret < 0)
                {
                    errorMessage = "avformat_write_header failed: " + FormatErrorCode(ret);
                    return false;
                }

                packet = ffmpeg.av_packet_alloc();
                if (packet == null)
                {
                    errorMessage = "av_packet_alloc failed";
                    return false;
                }

                while (true)
                {
                    cts.ThrowIfCancellationRequested();
                    ret = ffmpeg.av_read_frame(inFmt, packet);
                    if (ret < 0)
                    {
                        if (ret == ffmpeg.AVERROR_EOF)
                        {
                            break;
                        }

                        errorMessage = "av_read_frame failed: " + FormatErrorCode(ret);
                        return false;
                    }

                    int inputStreamIndex = packet->stream_index;
                    if (
                        inputStreamIndex < 0
                        || inputStreamIndex >= streamMap.Length
                        || streamMap[inputStreamIndex] < 0
                    )
                    {
                        ffmpeg.av_packet_unref(packet);
                        continue;
                    }

                    AVStream* inStream = inFmt->streams[inputStreamIndex];
                    AVStream* outStream = outFmt->streams[streamMap[inputStreamIndex]];
                    packet->stream_index = streamMap[inputStreamIndex];
                    ffmpeg.av_packet_rescale_ts(packet, inStream->time_base, outStream->time_base);
                    packet->pos = -1;

                    // video-only retry では欠けた timestamp を荒くでも整えて、
                    // サムネ用作業入力の生成を優先する。
                    if (!includeNonVideoStreams)
                    {
                        if (TryNormalizeMissingTimestamp(ref packet->pts, ref packet->dts))
                        {
                            ThumbnailRuntimeLog.Write(
                                "index-repair",
                                $"normalize missing timestamp: input='{inputPath}', output='{outputPath}', stream={packet->stream_index}, pts={packet->pts}, dts={packet->dts}"
                            );
                        }

                        if (ShouldSkipPacketForUnknownTimestamp(packet->pts, packet->dts))
                        {
                            ThumbnailRuntimeLog.Write(
                                "index-repair",
                                $"skip unknown timestamp packet: input='{inputPath}', output='{outputPath}', stream={packet->stream_index}"
                            );
                            ffmpeg.av_packet_unref(packet);
                            continue;
                        }
                    }

                    long currentDts = packet->dts;
                    long previousDts = lastWrittenDts[packet->stream_index];
                    if (ShouldSkipPacketForNonMonotonicDts(previousDts, currentDts))
                    {
                        ThumbnailRuntimeLog.Write(
                            "index-repair",
                            $"skip non-monotonic dts: input='{inputPath}', output='{outputPath}', stream={packet->stream_index}, prev_dts={previousDts}, dts={currentDts}"
                        );
                        ffmpeg.av_packet_unref(packet);
                        continue;
                    }

                    ret = ffmpeg.av_interleaved_write_frame(outFmt, packet);
                    if (ret >= 0 && currentDts != ffmpeg.AV_NOPTS_VALUE)
                    {
                        lastWrittenDts[packet->stream_index] = currentDts;
                    }
                    ffmpeg.av_packet_unref(packet);
                    if (ret < 0)
                    {
                        errorMessage = "av_interleaved_write_frame failed: " + FormatErrorCode(ret);
                        return false;
                    }
                }

                ret = ffmpeg.av_write_trailer(outFmt);
                if (ret < 0)
                {
                    errorMessage = "av_write_trailer failed: " + FormatErrorCode(ret);
                    return false;
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
            finally
            {
                if (packet != null)
                {
                    ffmpeg.av_packet_free(&packet);
                }

                if (outFmt != null)
                {
                    if (openedOutputIo && outFmt->pb != null)
                    {
                        ffmpeg.avio_closep(&outFmt->pb);
                    }

                    AVFormatContext* outFmtToFree = outFmt;
                    ffmpeg.avformat_free_context(outFmtToFree);
                }

                if (inFmt != null)
                {
                    ffmpeg.avformat_close_input(&inFmt);
                }
            }
        }

        internal static bool ShouldSkipPacketForNonMonotonicDts(long previousDts, long currentDts)
        {
            if (previousDts == long.MinValue || currentDts == ffmpeg.AV_NOPTS_VALUE)
            {
                return false;
            }

            return currentDts <= previousDts;
        }

        internal static bool TryNormalizeMissingTimestamp(ref long pts, ref long dts)
        {
            if (pts == ffmpeg.AV_NOPTS_VALUE && dts != ffmpeg.AV_NOPTS_VALUE)
            {
                pts = dts;
                return true;
            }

            if (dts == ffmpeg.AV_NOPTS_VALUE && pts != ffmpeg.AV_NOPTS_VALUE)
            {
                dts = pts;
                return true;
            }

            return false;
        }

        internal static bool ShouldSkipPacketForUnknownTimestamp(long pts, long dts)
        {
            return pts == ffmpeg.AV_NOPTS_VALUE && dts == ffmpeg.AV_NOPTS_VALUE;
        }

        private static string FormatErrorCode(int errorCode)
        {
            return errorCode.ToString("x8");
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

        private readonly record struct ProbeOpenResult(
            bool IsSuccess,
            string Reason,
            string ErrorCode,
            string ContainerFormat,
            int FindStreamInfoErrorCode
        );
    }
}
