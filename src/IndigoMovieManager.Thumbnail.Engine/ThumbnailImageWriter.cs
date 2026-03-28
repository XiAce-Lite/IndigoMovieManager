using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// JPEG 保存まわりの一時エラー吸収と、複数フレームの結合保存をまとめる。
    /// </summary>
    internal static class ThumbnailImageWriter
    {
        private const string JpegSaveParallelEnvName = "IMM_THUMB_JPEG_SAVE_PARALLEL";
        private const int DefaultJpegSaveParallel = 4;
        private const int MaxJpegSaveRetryCount = 3;
        private const int BaseJpegSaveRetryDelayMs = 60;
        // GDI+ の保存処理だけは同時実行数を絞り、ハンドル圧迫での瞬断を減らす。
        private static readonly SemaphoreSlim JpegSaveGate = CreateJpegSaveGate();

        public static bool SaveCombinedThumbnail(
            string saveThumbFileName,
            IReadOnlyList<Bitmap> frames,
            int columns,
            int rows
        )
        {
            if (frames == null || frames.Count < 1)
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
        public static bool TrySaveJpegWithRetry(Image image, string savePath, out string errorMessage)
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
                        ThumbnailOutputMarkerCoordinator.DeleteFileQuietly(tempPath);
                        if (attempt >= MaxJpegSaveRetryCount)
                        {
                            break;
                        }

                        Thread.Sleep(BaseJpegSaveRetryDelayMs * attempt);
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        ThumbnailOutputMarkerCoordinator.DeleteFileQuietly(tempPath);
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
    }
}
