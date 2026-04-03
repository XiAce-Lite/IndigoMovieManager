using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using FFMediaToolkit;
using FFMediaToolkit.Decoding;
using FFMediaToolkit.Graphics;

namespace IndigoMovieManager.Thumbnail.Decoders
{
    /// <summary>
    /// FFMediaToolkit でフレームを取得する実装。
    /// </summary>
    internal sealed class FfMediaToolkitThumbnailFrameDecoder : IThumbnailFrameDecoder
    {
        private const string GpuDecodeModeEnvName = "IMM_THUMB_GPU_DECODE";
        private static readonly object LoadSync = new();
        private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(15);
        private static bool ffmpegLoaded;
        private static DateTime nextRetryUtc = DateTime.MinValue;
        private static string lastLoadError = "";

        public string LibraryName => "FFMediaToolkit";

        public bool TryOpen(
            string movieFullPath,
            out IThumbnailFrameSource frameSource,
            out double? durationSec,
            out string errorMessage
        )
        {
            frameSource = null;
            durationSec = null;
            errorMessage = "";
            MediaFile mediaFile = null;

            if (!EnsureFfMediaToolkitLoaded(out string loadError))
            {
                errorMessage = loadError;
                return false;
            }

            try
            {
                MediaOptions options = new()
                {
                    StreamsToLoad = MediaMode.Video,
                    VideoPixelFormat = ImagePixelFormat.Bgr24,
                };

                var decoderOptions = BuildDecoderOptionsFromEnv();
                if (decoderOptions.Count > 0)
                {
                    options.DecoderOptions = decoderOptions;
                }

                mediaFile = MediaFile.Open(movieFullPath, options);
                if (mediaFile == null)
                {
                    errorMessage = "MediaFile.Open returned null";
                    return false;
                }

                bool hasVideo = mediaFile.HasVideo && mediaFile.VideoStreams.Any();
                if (!hasVideo)
                {
                    mediaFile.Dispose();
                    errorMessage = "video stream is missing";
                    return false;
                }

                var frameSize = mediaFile.Video.Info.FrameSize;
                if (frameSize.Width <= 0 || frameSize.Height <= 0)
                {
                    mediaFile.Dispose();
                    errorMessage = "invalid frame size";
                    return false;
                }

                double sec = mediaFile.Info.Duration.TotalSeconds;
                if (sec > 0 && !double.IsNaN(sec) && !double.IsInfinity(sec))
                {
                    durationSec = sec;
                }

                frameSource = new FfMediaToolkitFrameSource(
                    mediaFile,
                    new Size(frameSize.Width, frameSize.Height)
                );
                return true;
            }
            catch (Exception ex)
            {
                try
                {
                    // Open途中で例外化した時のリソース漏れを防ぐ。
                    mediaFile?.Dispose();
                }
                catch
                {
                    // 解放失敗時は元の例外情報を優先する。
                }
                errorMessage = ex.Message;
                return false;
            }
        }

        // 初期化失敗時でも一定間隔で再試行して、運用中復旧を可能にする。
        private static bool EnsureFfMediaToolkitLoaded(out string errorMessage)
        {
            lock (LoadSync)
            {
                if (ffmpegLoaded)
                {
                    errorMessage = "";
                    return true;
                }

                DateTime utcNow = DateTime.UtcNow;
                if (utcNow < nextRetryUtc)
                {
                    errorMessage = string.IsNullOrWhiteSpace(lastLoadError)
                        ? "FFMediaToolkit load retry is cooling down"
                        : lastLoadError;
                    return false;
                }

                string ffmpegSharedDir = Path.Combine(
                    AppContext.BaseDirectory,
                    "tools",
                    "ffmpeg-shared"
                );
                string lastError = "";
                try
                {
                    if (!Directory.Exists(ffmpegSharedDir))
                    {
                        lastError = "tools/ffmpeg-shared folder not found";
                    }
                    else if (!HasRequiredSharedDllSet(ffmpegSharedDir))
                    {
                        lastError = "required shared dll set is incomplete";
                    }
                    else
                    {
                        try
                        {
                            // FFmpegPath設定時点でも「already loaded」が飛ぶ実装差分があるため、
                            // パス設定とロードを同じtryで扱う。
                            FFmpegLoader.FFmpegPath = ffmpegSharedDir;
                            FFmpegLoader.LoadFFmpeg();
                        }
                        catch (InvalidOperationException ex)
                        {
                            // 他経路で先行ロード済みの時はここに入るため、成功扱いで継続する。
                            // メッセージ依存にすると取りこぼすため InvalidOperationException は包括的に許容する。
                            ThumbnailRuntimeLog.Write(
                                "thumbnail",
                                $"ffmediatoolkit already initialized: {ex.Message}"
                            );
                        }
                        ffmpegLoaded = true;
                        nextRetryUtc = DateTime.MinValue;
                        lastLoadError = "";
                        ThumbnailRuntimeLog.Write(
                            "thumbnail",
                            $"ffmediatoolkit init ok: dir='{ffmpegSharedDir}'"
                        );
                        errorMessage = "";
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                }

                nextRetryUtc = utcNow.Add(RetryInterval);
                lastLoadError = string.IsNullOrWhiteSpace(lastError)
                    ? "ffmediatoolkit shared dll load failed"
                    : lastError;
                ThumbnailRuntimeLog.Write(
                    "thumbnail",
                    $"ffmediatoolkit init failed: {lastLoadError} retry_after_utc={nextRetryUtc:O}"
                );
                errorMessage = lastLoadError;
                return false;
            }
        }

        private static Dictionary<string, string> BuildDecoderOptionsFromEnv()
        {
            string mode = ThumbnailEnvConfig.NormalizeGpuDecodeMode(
                Environment.GetEnvironmentVariable(GpuDecodeModeEnvName)?.Trim()
            );
            if (mode is not ("cuda" or "qsv" or "amd"))
            {
                return [];
            }

            string hwAccel = mode switch
            {
                "cuda" => "cuda",
                "qsv" => "qsv",
                // AMD系はFFmpeg側で d3d11va 指定が最も無難。
                "amd" => "d3d11va",
                _ => "",
            };

            if (string.IsNullOrWhiteSpace(hwAccel))
            {
                return [];
            }

            // TryGetFrame はCPUメモリへの書き込み前提。
            // hwaccel_output_format を固定するとGPUメモリフレームになり
            // 読み出し失敗しやすいため、ここでは hwaccel のみ指定する。
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["hwaccel"] = hwAccel,
            };
        }

        private static bool HasRequiredSharedDllSet(string dir)
        {
            return HasDll(dir, "avcodec*.dll")
                && HasDll(dir, "avformat*.dll")
                && HasDll(dir, "avutil*.dll")
                && HasDll(dir, "swscale*.dll")
                && HasDll(dir, "swresample*.dll");
        }

        private static bool HasDll(string dir, string pattern)
        {
            try
            {
                return Directory.EnumerateFiles(dir, pattern, SearchOption.TopDirectoryOnly).Any();
            }
            catch
            {
                return false;
            }
        }

        private sealed class FfMediaToolkitFrameSource : IThumbnailFrameSource
        {
            private readonly MediaFile mediaFile;

            public FfMediaToolkitFrameSource(MediaFile mediaFile, Size frameSize)
            {
                this.mediaFile = mediaFile;
                FrameSize = frameSize;
            }

            public Size FrameSize { get; }

            public bool TryReadFrame(TimeSpan position, out Bitmap frameBitmap)
            {
                frameBitmap = null;
                if (FrameSize.Width <= 0 || FrameSize.Height <= 0)
                {
                    return false;
                }

                try
                {
                    if (!mediaFile.Video.TryGetFrame(position, out ImageData imageData))
                    {
                        return false;
                    }

                    Size imageSize = imageData.ImageSize;
                    if (imageSize.Width <= 0 || imageSize.Height <= 0)
                    {
                        return false;
                    }

                    // IntPtr版TryGetFrameを避け、ImageDataをマネージド側で安全にBitmap化する。
                    byte[] srcBytes = imageData.Data.ToArray();
                    int srcStride = imageData.Stride;
                    int srcStrideAbs = Math.Abs(srcStride);
                    int srcRowBytes = Math.Min(imageSize.Width * 3, srcStrideAbs);
                    if (srcRowBytes <= 0)
                    {
                        return false;
                    }

                    Bitmap bitmap = new(imageSize.Width, imageSize.Height, PixelFormat.Format24bppRgb);
                    Rectangle rect = new(Point.Empty, bitmap.Size);
                    BitmapData lockBits = bitmap.LockBits(
                        rect,
                        ImageLockMode.WriteOnly,
                        PixelFormat.Format24bppRgb
                    );
                    try
                    {
                        nint dstBase = lockBits.Scan0;
                        int dstStride = lockBits.Stride;
                        if (dstStride < 0)
                        {
                            dstBase = nint.Add(dstBase, dstStride * (imageSize.Height - 1));
                            dstStride = -dstStride;
                        }

                        for (int y = 0; y < imageSize.Height; y++)
                        {
                            int srcY = srcStride >= 0 ? y : (imageSize.Height - 1 - y);
                            int srcOffset = srcY * srcStrideAbs;
                            int srcRemain = srcBytes.Length - srcOffset;
                            if (srcRemain <= 0)
                            {
                                bitmap.Dispose();
                                return false;
                            }

                            int copyBytes = Math.Min(srcRowBytes, srcRemain);
                            nint dstRow = nint.Add(dstBase, y * dstStride);
                            Marshal.Copy(srcBytes, srcOffset, dstRow, copyBytes);
                        }
                    }
                    finally
                    {
                        bitmap.UnlockBits(lockBits);
                    }

                    frameBitmap = bitmap;
                    return true;
                }
                catch (AccessViolationException ex)
                {
                    ThumbnailRuntimeLog.Write(
                        "thumbnail",
                        $"ffmediatoolkit av exception: pos={position.TotalSeconds:0.###}, err='{ex.Message}'"
                    );
                    frameBitmap?.Dispose();
                    frameBitmap = null;
                    return false;
                }
                catch (SEHException ex)
                {
                    ThumbnailRuntimeLog.Write(
                        "thumbnail",
                        $"ffmediatoolkit seh exception: pos={position.TotalSeconds:0.###}, err='{ex.Message}'"
                    );
                    frameBitmap?.Dispose();
                    frameBitmap = null;
                    return false;
                }
                catch (Exception ex)
                {
                    ThumbnailRuntimeLog.Write(
                        "thumbnail",
                        $"ffmediatoolkit frame convert failed: pos={position.TotalSeconds:0.###}, err='{ex.Message}'"
                    );
                    frameBitmap?.Dispose();
                    frameBitmap = null;
                    return false;
                }
            }

            public void Dispose()
            {
                mediaFile.Dispose();
            }
        }
    }
}
