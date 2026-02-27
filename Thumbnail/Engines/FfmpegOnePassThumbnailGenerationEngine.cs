using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace IndigoMovieManager.Thumbnail.Engines
{
    /// <summary>
    /// ffmpeg.exe を1パスで呼び出してサムネイルを生成するエンジン。
    /// </summary>
    internal sealed class FfmpegOnePassThumbnailGenerationEngine : IThumbnailGenerationEngine
    {
        public string EngineId => "ffmpeg1pass";

        public async Task<bool> CreateBookmarkAsync(
            string movieFullPath,
            string saveThumbPath,
            int capturePosSec,
            CancellationToken ct
        )
        {
            string ffmpegPath = ResolveFfmpegPath();
            if (string.IsNullOrEmpty(ffmpegPath) || !File.Exists(ffmpegPath))
            {
                return false;
            }

            string args =
                $"-y -ss {capturePosSec} -i \"{movieFullPath}\" -frames:v 1 -q:v 2 \"{saveThumbPath}\"";

            return await RunFfmpegAsync(ffmpegPath, args, ct);
        }

        public async Task<ThumbnailCreateResult> CreateAsync(
            ThumbnailJobContext context,
            CancellationToken ct
        )
        {
            string ffmpegPath = ResolveFfmpegPath();
            if (string.IsNullOrEmpty(ffmpegPath) || !File.Exists(ffmpegPath))
            {
                return ThumbnailCreationService.CreateFailedResult(
                    context.SaveThumbFileName,
                    context.DurationSec,
                    "ffmpeg1pass: ffmpeg.exe not found"
                );
            }

            List<Bitmap> frames = new();
            List<string> tempFiles = new();

            try
            {
                foreach (int sec in context.ThumbInfo.ThumbSec)
                {
                    string tempPath = Path.Combine(
                        Path.GetTempPath(),
                        $"imm_ffmpeg_{Guid.NewGuid():N}.jpg"
                    );
                    tempFiles.Add(tempPath);

                    string args =
                        $"-y -ss {sec} -i \"{context.MovieFullPath}\" -frames:v 1 -q:v 2 \"{tempPath}\"";

                    bool ok = await RunFfmpegAsync(ffmpegPath, args, ct);
                    if (ok && File.Exists(tempPath))
                    {
                        try
                        {
                            using Bitmap raw = new(tempPath);
                            Rectangle rect = ThumbnailCreationService.GetAspectRect(
                                raw.Width,
                                raw.Height
                            );
                            using Bitmap cropped = ThumbnailCreationService.CropBitmap(raw, rect);
                            Size targetSize = context.IsResizeThumb
                                ? new Size(
                                    context.ThumbInfo.ThumbWidth,
                                    context.ThumbInfo.ThumbHeight
                                )
                                : ThumbnailCreationService.ResolveDefaultTargetSize(cropped);
                            frames.Add(ThumbnailCreationService.ResizeBitmap(cropped, targetSize));
                        }
                        catch
                        {
                            // フレーム読み取り失敗は個別スキップ
                        }
                    }
                }

                if (frames.Count < 1)
                {
                    return ThumbnailCreationService.CreateFailedResult(
                        context.SaveThumbFileName,
                        context.DurationSec,
                        "ffmpeg1pass: no frames captured"
                    );
                }

                bool saved = ThumbnailCreationService.SaveCombinedThumbnail(
                    context.SaveThumbFileName,
                    frames,
                    context.ThumbInfo.ThumbColumns,
                    context.ThumbInfo.ThumbRows
                );

                return saved
                    ? ThumbnailCreationService.CreateSuccessResult(
                        context.SaveThumbFileName,
                        context.DurationSec
                    )
                    : ThumbnailCreationService.CreateFailedResult(
                        context.SaveThumbFileName,
                        context.DurationSec,
                        "ffmpeg1pass: save combined thumbnail failed"
                    );
            }
            finally
            {
                foreach (Bitmap bmp in frames)
                {
                    bmp?.Dispose();
                }
                foreach (string tmp in tempFiles)
                {
                    try
                    {
                        if (File.Exists(tmp))
                            File.Delete(tmp);
                    }
                    catch { }
                }
            }
        }

        private static string ResolveFfmpegPath()
        {
            string envPath = ThumbnailEnvConfig.GetFfmpegExePath();
            if (!string.IsNullOrEmpty(envPath))
            {
                return envPath;
            }

            // 既定: アプリ配下 tools/ffmpeg/ffmpeg.exe
            string appDir =
                AppDomain.CurrentDomain.BaseDirectory ?? Directory.GetCurrentDirectory();
            return Path.Combine(appDir, "tools", "ffmpeg", "ffmpeg.exe");
        }

        private static async Task<bool> RunFfmpegAsync(
            string ffmpegPath,
            string args,
            CancellationToken ct
        )
        {
            try
            {
                using Process proc = new();
                proc.StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                proc.Start();
                await proc.WaitForExitAsync(ct);
                return proc.ExitCode == 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ffmpeg1pass: run failed: {ex.Message}");
                return false;
            }
        }
    }
}
