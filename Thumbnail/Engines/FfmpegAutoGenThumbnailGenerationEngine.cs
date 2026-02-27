using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
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
        private static readonly object _initLock = new();

        public string EngineId => "autogen";
        public string EngineName => "autogen";

        public FfmpegAutoGenThumbnailGenerationEngine()
        {
            EnsureFfmpegInitialized();
        }

        private static void EnsureFfmpegInitialized()
        {
            if (!_isInitialized)
            {
                lock (_initLock)
                {
                    if (!_isInitialized)
                    {
                        // ThumbnailEnvConfig がある場合はそれを利用可能だが、
                        // 基本は tools/ffmpeg-shared を参照する
                        string ffmpegSharedDir = ThumbnailEnvConfig.GetFfmpegExePath();
                        if (
                            string.IsNullOrWhiteSpace(ffmpegSharedDir)
                            || !Directory.Exists(ffmpegSharedDir)
                        )
                        {
                            ffmpegSharedDir = Path.Combine(
                                AppContext.BaseDirectory,
                                "tools",
                                "ffmpeg-shared"
                            );
                        }

                        ffmpeg.RootPath = ffmpegSharedDir;
                        DynamicallyLoadedBindings.Initialize();
                        _isInitialized = true;
                    }
                }
            }
        }

        public bool CanHandle(ThumbnailJobContext context)
        {
            return true;
        }

        public Task<ThumbnailCreateResult> CreateAsync(
            ThumbnailJobContext context,
            CancellationToken cts = default
        )
        {
            return Task.Run(() => CreateInternal(context, cts), cts);
        }

        public Task<bool> CreateBookmarkAsync(
            string movieFullPath,
            string saveThumbPath,
            int capturePos,
            CancellationToken cts = default
        )
        {
            return Task.Run(
                () => CreateBookmarkInternal(movieFullPath, saveThumbPath, capturePos, cts),
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
            if (context.IsManual)
                return ThumbnailCreationService.CreateFailedResult(
                    context.SaveThumbFileName,
                    context.DurationSec,
                    "autogen does not support manual mode yet"
                );
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

            int cols = context.TabInfo.Columns;
            int rows = context.TabInfo.Rows;
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

            int targetWidth = context.TabInfo.Width > 0 ? context.TabInfo.Width : 320;
            int targetHeight = context.TabInfo.Height > 0 ? context.TabInfo.Height : 240;

            AVFormatContext* pFormatContext = null;
            AVCodecContext* pCodecContext = null;
            AVFrame* pFrame = null;
            AVPacket* pPacket = null;
            SwsContext* pSwsContext = null;

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

                AVPixelFormat sourcePixelFormat =
                    pCodecContext->pix_fmt == AVPixelFormat.AV_PIX_FMT_NONE
                        ? AVPixelFormat.AV_PIX_FMT_YUV420P
                        : pCodecContext->pix_fmt;

                pSwsContext = ffmpeg.sws_getContext(
                    pCodecContext->width,
                    pCodecContext->height,
                    sourcePixelFormat,
                    targetWidth,
                    targetHeight,
                    AVPixelFormat.AV_PIX_FMT_BGR24,
                    1, // SWS_FAST_BILINEAR
                    null,
                    null,
                    null
                );

                pPacket = ffmpeg.av_packet_alloc();
                pFrame = ffmpeg.av_frame_alloc();

                List<Bitmap> bitmaps = new List<Bitmap>();

                foreach (double sec in captureSecs)
                {
                    cts.ThrowIfCancellationRequested();

                    long ts = (long)(sec / ffmpeg.av_q2d(pStream->time_base));
                    ffmpeg.av_seek_frame(
                        pFormatContext,
                        pStream->index,
                        ts,
                        ffmpeg.AVSEEK_FLAG_BACKWARD
                    );
                    ffmpeg.avcodec_flush_buffers(pCodecContext);

                    bool frameGot = false;
                    while (ffmpeg.av_read_frame(pFormatContext, pPacket) >= 0)
                    {
                        cts.ThrowIfCancellationRequested();
                        if (pPacket->stream_index == pStream->index)
                        {
                            ret = ffmpeg.avcodec_send_packet(pCodecContext, pPacket);
                            if (ret >= 0)
                            {
                                ret = ffmpeg.avcodec_receive_frame(pCodecContext, pFrame);
                                if (ret == 0)
                                {
                                    Bitmap bmp = ConvertFrameToBitmap(
                                        pFrame,
                                        pSwsContext,
                                        targetWidth,
                                        targetHeight
                                    );
                                    if (bmp != null)
                                    {
                                        bitmaps.Add(bmp);
                                        frameGot = true;
                                    }
                                    ffmpeg.av_frame_unref(pFrame);
                                    break;
                                }
                                else if (
                                    ret == ffmpeg.AVERROR(ffmpeg.EAGAIN)
                                    || ret == ffmpeg.AVERROR_EOF
                                )
                                {
                                    break;
                                }
                            }
                        }
                        ffmpeg.av_packet_unref(pPacket);
                    }
                    ffmpeg.av_packet_unref(pPacket);
                    ffmpeg.av_frame_unref(pFrame);
                }

                if (bitmaps.Count > 0)
                {
                    SaveCombinedThumbnail(
                        bitmaps,
                        cols,
                        rows,
                        targetWidth,
                        targetHeight,
                        context.SaveThumbFileName
                    );
                }
                else
                {
                    return ThumbnailCreationService.CreateFailedResult(
                        context.SaveThumbFileName,
                        durationSec,
                        "No frames decoded"
                    );
                }

                foreach (var bmp in bitmaps)
                {
                    bmp.Dispose();
                }

                return ThumbnailCreationService.CreateSuccessResult(
                    context.SaveThumbFileName,
                    durationSec
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
                    ffmpeg.sws_freeContext(pSwsContext);
            }
        }

        private unsafe bool CreateBookmarkInternal(
            string movieFullPath,
            string saveThumbPath,
            int capturePos,
            CancellationToken cts
        )
        {
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

                AVPixelFormat sourcePixelFormat =
                    pCodecContext->pix_fmt == AVPixelFormat.AV_PIX_FMT_NONE
                        ? AVPixelFormat.AV_PIX_FMT_YUV420P
                        : pCodecContext->pix_fmt;

                pSwsContext = ffmpeg.sws_getContext(
                    pCodecContext->width,
                    pCodecContext->height,
                    sourcePixelFormat,
                    targetWidth,
                    targetHeight,
                    AVPixelFormat.AV_PIX_FMT_BGR24,
                    1,
                    null,
                    null,
                    null
                );

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
                while (ffmpeg.av_read_frame(pFormatContext, pPacket) >= 0)
                {
                    cts.ThrowIfCancellationRequested();
                    if (pPacket->stream_index == pStream->index)
                    {
                        if (ffmpeg.avcodec_send_packet(pCodecContext, pPacket) >= 0)
                        {
                            int ret = ffmpeg.avcodec_receive_frame(pCodecContext, pFrame);
                            if (ret == 0)
                            {
                                extracted = ConvertFrameToBitmap(
                                    pFrame,
                                    pSwsContext,
                                    targetWidth,
                                    targetHeight
                                );
                                ffmpeg.av_frame_unref(pFrame);
                                break;
                            }
                            else if (
                                ret == ffmpeg.AVERROR(ffmpeg.EAGAIN)
                                || ret == ffmpeg.AVERROR_EOF
                            )
                            {
                                break;
                            }
                        }
                    }
                    ffmpeg.av_packet_unref(pPacket);
                }
                ffmpeg.av_packet_unref(pPacket);
                ffmpeg.av_frame_unref(pFrame);

                if (extracted != null)
                {
                    using (extracted)
                    {
                        extracted.Save(saveThumbPath, ImageFormat.Jpeg);
                    }
                    return true;
                }
                return false;
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
                    ffmpeg.sws_freeContext(pSwsContext);
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
            BitmapData bitmapData = bitmap.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly,
                PixelFormat.Format24bppRgb
            );
            byte*[] dstData = new byte*[] { (byte*)bitmapData.Scan0 };
            int[] dstLinesize = new int[] { bitmapData.Stride };

            int outputHeight = ffmpeg.sws_scale(
                pSwsContext,
                pFrame->data,
                pFrame->linesize,
                0,
                pFrame->height,
                dstData,
                dstLinesize
            );
            bitmap.UnlockBits(bitmapData);

            if (outputHeight <= 0)
            {
                bitmap.Dispose();
                return null;
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

            if (File.Exists(savePath))
                File.Delete(savePath);
            combined.Save(savePath, ImageFormat.Jpeg);
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
