using System;
using System.Drawing;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace IndigoMovieManager.Thumbnail.Decoders
{
    /// <summary>
    /// OpenCvSharp を使用した動画フレーム読み取りソース。
    /// GPU デコード（CUDA）を環境変数で切替可能。
    /// </summary>
    internal sealed class OpenCvThumbnailFrameDecoder : IThumbnailFrameSource
    {
        private const string OpenCvFfmpegCaptureOptionsEnvName = "OPENCV_FFMPEG_CAPTURE_OPTIONS";

        private readonly VideoCapture capture;
        private bool disposed;

        public OpenCvThumbnailFrameDecoder(string movieFullPath)
        {
            string gpuMode = ThumbnailEnvConfig.GetGpuDecodeMode();
            if (string.Equals(gpuMode, "cuda", StringComparison.OrdinalIgnoreCase))
            {
                Environment.SetEnvironmentVariable(
                    OpenCvFfmpegCaptureOptionsEnvName,
                    "hwaccel;cuda|video_codec;h264_cuvid"
                );
            }

            capture = new VideoCapture(movieFullPath, VideoCaptureAPIs.FFMPEG);
        }

        public bool TryReadFrame(TimeSpan position, out Bitmap frameBitmap)
        {
            frameBitmap = null;
            if (disposed || capture == null || !capture.IsOpened())
            {
                return false;
            }

            capture.Set(VideoCaptureProperties.PosMsec, position.TotalMilliseconds);

            using Mat mat = new();
            if (!capture.Read(mat) || mat.Empty())
            {
                return false;
            }

            try
            {
                frameBitmap = BitmapConverter.ToBitmap(mat);
                return frameBitmap != null;
            }
            catch
            {
                frameBitmap?.Dispose();
                frameBitmap = null;
                return false;
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            capture?.Release();
            capture?.Dispose();

            // GPU モード使用後は環境変数をクリアする。
            string gpuMode = ThumbnailEnvConfig.GetGpuDecodeMode();
            if (string.Equals(gpuMode, "cuda", StringComparison.OrdinalIgnoreCase))
            {
                Environment.SetEnvironmentVariable(OpenCvFfmpegCaptureOptionsEnvName, null);
            }
        }
    }
}
