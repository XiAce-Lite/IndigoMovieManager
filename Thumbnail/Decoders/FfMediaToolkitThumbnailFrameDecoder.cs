using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using FFMediaToolkit;
using FFMediaToolkit.Decoding;
using FFMediaToolkit.Graphics;

namespace IndigoMovieManager.Thumbnail.Decoders
{
    /// <summary>
    /// FFMediaToolkit (libav) を使用した動画フレーム読み取りソース。
    /// </summary>
    internal sealed class FfMediaToolkitThumbnailFrameDecoder : IThumbnailFrameSource
    {
        private readonly MediaFile mediaFile;
        private bool disposed;

        public FfMediaToolkitThumbnailFrameDecoder(string movieFullPath)
        {
            string gpuMode = ThumbnailEnvConfig.GetGpuDecodeMode();
            var options = new MediaOptions { VideoPixelFormat = ImagePixelFormat.Bgr24 };

            // CUDA 有効時は追加オプションを将来的に設定可能。
            // 現時点では FFMediaToolkit のデフォルト設定で動作する。

            mediaFile = MediaFile.Open(movieFullPath, options);
        }

        public bool TryReadFrame(TimeSpan position, out Bitmap frameBitmap)
        {
            frameBitmap = null;
            if (disposed || mediaFile?.Video == null)
            {
                return false;
            }

            try
            {
                ImageData imageData = mediaFile.Video.GetFrame(position);
                frameBitmap = ConvertToBitmap(imageData);
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
            mediaFile?.Dispose();
        }

        private static Bitmap ConvertToBitmap(ImageData imageData)
        {
            int width = imageData.ImageSize.Width;
            int height = imageData.ImageSize.Height;

            Bitmap bmp = new(width, height, PixelFormat.Format24bppRgb);
            BitmapData bmpData = bmp.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly,
                PixelFormat.Format24bppRgb
            );

            try
            {
                ReadOnlySpan<byte> srcSpan = imageData.Data;
                int srcStride = imageData.Stride;
                int dstStride = bmpData.Stride;
                int rowBytes = Math.Min(srcStride, dstStride);

                for (int y = 0; y < height; y++)
                {
                    ReadOnlySpan<byte> srcRow = srcSpan.Slice(y * srcStride, rowBytes);
                    IntPtr dstRow = bmpData.Scan0 + y * dstStride;
                    Marshal.Copy(srcRow.ToArray(), 0, dstRow, rowBytes);
                }
            }
            finally
            {
                bmp.UnlockBits(bmpData);
            }

            return bmp;
        }
    }
}
