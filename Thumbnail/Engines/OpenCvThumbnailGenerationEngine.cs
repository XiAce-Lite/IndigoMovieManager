using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using IndigoMovieManager.Thumbnail.Decoders;
using static IndigoMovieManager.Thumbnail.Tools;

namespace IndigoMovieManager.Thumbnail.Engines
{
    /// <summary>
    /// OpenCvSharp ベースのサムネイル生成エンジン。
    /// </summary>
    internal sealed class OpenCvThumbnailGenerationEngine : IThumbnailGenerationEngine
    {
        public string EngineId => "opencv";

        public async Task<bool> CreateBookmarkAsync(
            string movieFullPath,
            string saveThumbPath,
            int capturePosSec,
            CancellationToken ct
        )
        {
            return await Task.Run(
                () =>
                {
                    using IThumbnailFrameSource source = ThumbnailFrameDecoderFactory.Create(
                        "opencv",
                        movieFullPath
                    );
                    if (source == null)
                        return false;

                    if (
                        !ThumbnailCreationService.TryReadFrameWithRetry(
                            source,
                            TimeSpan.FromSeconds(capturePosSec),
                            out Bitmap frame
                        )
                    )
                    {
                        return false;
                    }

                    using (frame)
                    {
                        Rectangle rect = ThumbnailCreationService.GetAspectRect(
                            frame.Width,
                            frame.Height
                        );
                        using Bitmap cropped = ThumbnailCreationService.CropBitmap(frame, rect);
                        Size targetSize = ThumbnailCreationService.ResolveDefaultTargetSize(
                            cropped
                        );
                        using Bitmap resized = ThumbnailCreationService.ResizeBitmap(
                            cropped,
                            targetSize
                        );
                        resized.Save(saveThumbPath, System.Drawing.Imaging.ImageFormat.Jpeg);
                    }
                    return true;
                },
                ct
            );
        }

        public async Task<ThumbnailCreateResult> CreateAsync(
            ThumbnailJobContext context,
            CancellationToken ct
        )
        {
            return await Task.Run(
                () =>
                {
                    using IThumbnailFrameSource source = ThumbnailFrameDecoderFactory.Create(
                        "opencv",
                        context.MovieFullPath
                    );
                    if (source == null)
                    {
                        return ThumbnailCreationService.CreateFailedResult(
                            context.SaveThumbFileName,
                            context.DurationSec,
                            "opencv: failed to create frame source"
                        );
                    }

                    List<Bitmap> frames = new();
                    try
                    {
                        foreach (int sec in context.ThumbInfo.ThumbSec)
                        {
                            if (
                                ThumbnailCreationService.TryReadFrameWithRetry(
                                    source,
                                    TimeSpan.FromSeconds(sec),
                                    out Bitmap frame
                                )
                            )
                            {
                                Rectangle rect = ThumbnailCreationService.GetAspectRect(
                                    frame.Width,
                                    frame.Height
                                );
                                using Bitmap cropped = ThumbnailCreationService.CropBitmap(
                                    frame,
                                    rect
                                );
                                frame.Dispose();

                                Size targetSize = context.IsResizeThumb
                                    ? new Size(
                                        context.ThumbInfo.ThumbWidth,
                                        context.ThumbInfo.ThumbHeight
                                    )
                                    : ThumbnailCreationService.ResolveDefaultTargetSize(cropped);

                                frames.Add(
                                    ThumbnailCreationService.ResizeBitmap(cropped, targetSize)
                                );
                            }
                        }

                        if (frames.Count < 1)
                        {
                            return ThumbnailCreationService.CreateFailedResult(
                                context.SaveThumbFileName,
                                context.DurationSec,
                                "opencv: no frames captured"
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
                                "opencv: save combined thumbnail failed"
                            );
                    }
                    finally
                    {
                        foreach (Bitmap bmp in frames)
                        {
                            bmp?.Dispose();
                        }
                    }
                },
                ct
            );
        }
    }
}
