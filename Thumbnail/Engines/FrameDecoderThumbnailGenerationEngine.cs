using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using IndigoMovieManager.Thumbnail.Decoders;

namespace IndigoMovieManager.Thumbnail.Engines
{
    /// <summary>
    /// フレーム取得型デコーダー（FFMediaToolkit/OpenCv）を使う共通エンジン。
    /// </summary>
    internal class FrameDecoderThumbnailGenerationEngine : IThumbnailGenerationEngine
    {
        private readonly IThumbnailFrameDecoder decoder;

        public FrameDecoderThumbnailGenerationEngine(
            string engineId,
            IThumbnailFrameDecoder decoder
        )
        {
            EngineId = engineId ?? throw new ArgumentNullException(nameof(engineId));
            this.decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
        }

        public string EngineId { get; }
        public string EngineName => decoder.LibraryName;

        public bool CanHandle(ThumbnailJobContext context)
        {
            return true;
        }

        public async Task<ThumbnailCreateResult> CreateAsync(
            ThumbnailJobContext context,
            CancellationToken cts = default
        )
        {
            return await Task.Run(
                () =>
                {
                    if (context == null)
                    {
                        return ThumbnailCreateResultFactory.CreateFailed(
                            "",
                            null,
                            "context is null"
                        );
                    }

                    cts.ThrowIfCancellationRequested();

                    if (
                        !decoder.TryOpen(
                            context.MovieFullPath,
                            out IThumbnailFrameSource frameSource,
                            out double? decoderDurationSec,
                            out string openError
                        )
                    )
                    {
                        ThumbnailRuntimeLog.Write(
                            "thumbnail",
                            $"thumb open failed: lib={decoder.LibraryName}, movie='{context.MovieFullPath}', reason='{openError}'"
                        );
                        return ThumbnailCreateResultFactory.CreateFailed(
                            context.SaveThumbFileName,
                            context.DurationSec,
                            openError
                        );
                    }

                    using (frameSource)
                    {
                        double? durationSec = context.DurationSec;
                        if (!durationSec.HasValue || durationSec.Value <= 0)
                        {
                            durationSec = decoderDurationSec;
                            if (!durationSec.HasValue || durationSec.Value <= 0)
                            {
                                durationSec = ThumbnailShellDurationResolver.TryGetDurationSec(
                                    context.MovieFullPath
                                );
                            }
                        }

                        ThumbInfo thumbInfo = context.ThumbInfo;
                        if (!context.IsManual && (!context.DurationSec.HasValue || context.DurationSec.Value <= 0))
                        {
                            thumbInfo = ThumbnailAutoThumbInfoBuilder.Build(
                                context.LayoutProfile,
                                durationSec
                            );
                        }

                        if (thumbInfo == null || thumbInfo.ThumbSec == null || thumbInfo.ThumbSec.Count < 1)
                        {
                            return ThumbnailCreateResultFactory.CreateFailed(
                                context.SaveThumbFileName,
                                durationSec,
                                "thumb info is empty"
                            );
                        }

                        List<Bitmap> resizedFrames = [];
                        try
                        {
                            Size? targetSize = context.IsResizeThumb
                                ? new Size(context.PanelWidth, context.PanelHeight)
                                : null;

                            for (int i = 0; i < thumbInfo.ThumbSec.Count; i++)
                            {
                                cts.ThrowIfCancellationRequested();
                                int sec = thumbInfo.ThumbSec[i];

                                // 実デコード直前に末尾超過を避けるため秒をクランプする。
                                if (durationSec.HasValue && durationSec.Value > 0)
                                {
                                    int maxSec = Math.Max(0, (int)Math.Floor(durationSec.Value) - 1);
                                    if (sec > maxSec)
                                    {
                                        sec = maxSec;
                                    }
                                }
                                if (sec < 0)
                                {
                                    sec = 0;
                                }
                                thumbInfo.ThumbSec[i] = sec;

                                if (
                                    !ThumbnailFrameReadRetryHelper.TryReadFrameWithRetry(
                                        frameSource,
                                        TimeSpan.FromSeconds(Math.Max(0, sec)),
                                        out Bitmap frame
                                    )
                                )
                                {
                                    return ThumbnailCreateResultFactory.CreateFailed(
                                        context.SaveThumbFileName,
                                        durationSec,
                                        $"frame decode failed at sec={sec}"
                                    );
                                }

                                using (frame)
                                {
                                    if (
                                        !targetSize.HasValue
                                        || targetSize.Value.Width <= 0
                                        || targetSize.Value.Height <= 0
                                    )
                                    {
                                        targetSize =
                                            ThumbnailImageTransformHelper.ResolveDefaultTargetSize(
                                                frame
                                            );
                                    }

                                    Bitmap resized = ThumbnailImageTransformHelper.ResizeBitmap(
                                        frame,
                                        targetSize.Value
                                    );
                                    resizedFrames.Add(resized);
                                }
                            }

                            if (resizedFrames.Count < 1)
                            {
                                return ThumbnailCreateResultFactory.CreateFailed(
                                    context.SaveThumbFileName,
                                    durationSec,
                                    "decoded frame list is empty"
                                );
                            }

                            bool saved = ThumbnailImageWriter.SaveCombinedThumbnail(
                                context.SaveThumbFileName,
                                resizedFrames,
                                context.PanelColumns,
                                context.PanelRows
                            );
                            if (!saved)
                            {
                                return ThumbnailCreateResultFactory.CreateFailed(
                                    context.SaveThumbFileName,
                                    durationSec,
                                    "combined thumbnail save failed"
                                );
                            }

                            WhiteBrowserThumbInfoSerializer.AppendToJpeg(
                                context.SaveThumbFileName,
                                thumbInfo?.ToSheetSpec()
                            );
                            return ThumbnailCreateResultFactory.CreateSuccess(
                                context.SaveThumbFileName,
                                durationSec
                            );
                        }
                        catch (Exception ex)
                        {
                            return ThumbnailCreateResultFactory.CreateFailed(
                                context.SaveThumbFileName,
                                durationSec,
                                ex.Message
                            );
                        }
                        finally
                        {
                            foreach (var frame in resizedFrames)
                            {
                                frame.Dispose();
                            }
                        }
                    }
                },
                cts
            );
        }

        public async Task<bool> CreateBookmarkAsync(
            string movieFullPath,
            string saveThumbPath,
            int capturePos,
            CancellationToken cts = default
        )
        {
            if (!Path.Exists(movieFullPath))
            {
                return false;
            }

            return await Task.Run(
                () =>
                {
                    cts.ThrowIfCancellationRequested();
                    if (
                        !decoder.TryOpen(
                            movieFullPath,
                            out IThumbnailFrameSource frameSource,
                            out _,
                            out string openError
                        )
                    )
                    {
                        ThumbnailRuntimeLog.Write(
                            "thumbnail",
                            $"bookmark open failed: lib={decoder.LibraryName}, movie='{movieFullPath}', reason='{openError}'"
                        );
                        return false;
                    }

                    using (frameSource)
                    {
                        if (
                            !ThumbnailFrameReadRetryHelper.TryReadFrameWithRetry(
                                frameSource,
                                TimeSpan.FromSeconds(Math.Max(0, capturePos)),
                                out Bitmap frame
                            )
                        )
                        {
                            return false;
                        }

                        using (frame)
                        {
                            using Bitmap resized = ThumbnailImageTransformHelper.ResizeBitmap(
                                frame,
                                new Size(640, 480)
                            );

                            string saveDir = Path.GetDirectoryName(saveThumbPath) ?? "";
                            if (!string.IsNullOrWhiteSpace(saveDir))
                            {
                                Directory.CreateDirectory(saveDir);
                            }

                            if (Path.Exists(saveThumbPath))
                            {
                                File.Delete(saveThumbPath);
                            }

                            resized.Save(saveThumbPath, ImageFormat.Jpeg);
                            return true;
                        }
                    }
                },
                cts
            );
        }
    }
}
