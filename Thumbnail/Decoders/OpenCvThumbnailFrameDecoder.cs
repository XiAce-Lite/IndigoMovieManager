using System.Drawing;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using DrawingSize = System.Drawing.Size;

namespace IndigoMovieManager.Thumbnail.Decoders
{
    /// <summary>
    /// OpenCvSharp でフレームを取得する実装。
    /// </summary>
    internal sealed class OpenCvThumbnailFrameDecoder : IThumbnailFrameDecoder
    {
        private const string GpuDecodeModeEnvName = "IMM_THUMB_GPU_DECODE";
        private const string OpenCvFfmpegCaptureOptionsEnvName = "OPENCV_FFMPEG_CAPTURE_OPTIONS";
        private const string CudaCaptureOptions = "hwaccel;cuda|hwaccel_output_format;cuda";
        private static readonly object GpuDecodeOptionLock = new();

        public string LibraryName => "OpenCvSharp";

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
            VideoCapture capture = null;

            try
            {
                ConfigureGpuDecodeOptionsFromEnv();

                capture = new VideoCapture(movieFullPath);
                if (!capture.IsOpened())
                {
                    errorMessage = "VideoCapture open failed";
                    capture.Dispose();
                    return false;
                }

                int frameWidth = (int)capture.Get(VideoCaptureProperties.FrameWidth);
                int frameHeight = (int)capture.Get(VideoCaptureProperties.FrameHeight);
                if (frameWidth <= 0 || frameHeight <= 0)
                {
                    if (!TryProbeFrameSize(capture, out frameWidth, out frameHeight))
                    {
                        errorMessage = "invalid frame size";
                        capture.Dispose();
                        return false;
                    }
                }

                double frameCount = capture.Get(VideoCaptureProperties.FrameCount);
                double fps = capture.Get(VideoCaptureProperties.Fps);
                if (fps > 0 && frameCount > 0 && !double.IsNaN(fps) && !double.IsNaN(frameCount))
                {
                    double sec = Math.Truncate(frameCount / fps);
                    if (sec > 0 && !double.IsInfinity(sec))
                    {
                        durationSec = sec;
                    }
                }

                frameSource = new OpenCvFrameSource(capture, new DrawingSize(frameWidth, frameHeight));
                capture = null;
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
            finally
            {
                // 失敗経路ではここで確実に解放する。
                capture?.Dispose();
            }
        }

        // プロパティでサイズが取れない入力でも、1フレームだけ読んで寸法を確定する。
        private static bool TryProbeFrameSize(
            VideoCapture capture,
            out int frameWidth,
            out int frameHeight
        )
        {
            frameWidth = 0;
            frameHeight = 0;
            using Mat probe = new();
            if (!capture.Read(probe) || probe.Empty())
            {
                return false;
            }

            frameWidth = probe.Width;
            frameHeight = probe.Height;
            capture.Set(VideoCaptureProperties.PosFrames, 0);
            return frameWidth > 0 && frameHeight > 0;
        }

        // IMM_THUMB_GPU_DECODE=cuda の時だけ OpenCV 側のFFmpegオプションを設定する。
        private static void ConfigureGpuDecodeOptionsFromEnv()
        {
            string mode = Environment.GetEnvironmentVariable(GpuDecodeModeEnvName)?.Trim();
            bool useCuda = string.Equals(mode, "cuda", StringComparison.OrdinalIgnoreCase);

            lock (GpuDecodeOptionLock)
            {
                string current = Environment.GetEnvironmentVariable(
                    OpenCvFfmpegCaptureOptionsEnvName
                );
                if (useCuda)
                {
                    if (
                        string.IsNullOrWhiteSpace(current)
                        || string.Equals(
                            current,
                            CudaCaptureOptions,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        Environment.SetEnvironmentVariable(
                            OpenCvFfmpegCaptureOptionsEnvName,
                            CudaCaptureOptions
                        );
                    }
                    return;
                }

                // このアプリが設定した値だけ戻して、他用途の設定は壊さない。
                if (
                    string.Equals(current, CudaCaptureOptions, StringComparison.OrdinalIgnoreCase)
                )
                {
                    Environment.SetEnvironmentVariable(OpenCvFfmpegCaptureOptionsEnvName, null);
                }
            }
        }

        private sealed class OpenCvFrameSource : IThumbnailFrameSource
        {
            private readonly VideoCapture capture;

            public OpenCvFrameSource(VideoCapture capture, DrawingSize frameSize)
            {
                this.capture = capture;
                FrameSize = frameSize;
            }

            public DrawingSize FrameSize { get; }

            public bool TryReadFrame(TimeSpan position, out Bitmap frameBitmap)
            {
                frameBitmap = null;
                using Mat frame = new();

                double msec = Math.Max(0, position.TotalMilliseconds);
                capture.Set(VideoCaptureProperties.PosMsec, msec);
                if (!capture.Read(frame) || frame.Empty())
                {
                    return false;
                }

                try
                {
                    frameBitmap = ConvertToBitmap(frame);
                    return frameBitmap != null;
                }
                catch
                {
                    frameBitmap?.Dispose();
                    frameBitmap = null;
                    return false;
                }
            }

            // チャンネル数を揃えてからBitmap化し、後段のSystem.Drawing処理を安定させる。
            private static Bitmap ConvertToBitmap(Mat source)
            {
                if (source.Channels() == 3)
                {
                    return BitmapConverter.ToBitmap(source);
                }

                using Mat normalized = new();
                if (source.Channels() == 4)
                {
                    Cv2.CvtColor(source, normalized, ColorConversionCodes.BGRA2BGR);
                    return BitmapConverter.ToBitmap(normalized);
                }

                if (source.Channels() == 1)
                {
                    Cv2.CvtColor(source, normalized, ColorConversionCodes.GRAY2BGR);
                    return BitmapConverter.ToBitmap(normalized);
                }

                return BitmapConverter.ToBitmap(source);
            }

            public void Dispose()
            {
                capture.Dispose();
            }
        }
    }
}