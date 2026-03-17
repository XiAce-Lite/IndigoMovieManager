using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using IndigoMovieManager.Thumbnail.Engines;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 既知失敗をプレースホルダー画像へ置き換える判断と描画をまとめる。
    /// </summary>
    internal static class ThumbnailFailurePlaceholderWriter
    {
        private static readonly string[] DrmErrorKeywords =
        [
            "prdy",
            "playready",
            "drm",
            "encrypted",
            "protected",
            "no decoder found for: none",
            "video stream is missing",
        ];
        private static readonly string[] UnsupportedErrorKeywords =
        [
            "decoder not found",
            "video stream not found",
            "unknown codec",
            "unknown",
            "unsupported",
            "invalid data found",
            "failed to open input",
        ];

        public static ThumbnailFailurePlaceholderKind ClassifyFailureKind(
            string codec,
            IReadOnlyList<string> engineErrorMessages
        )
        {
            StringBuilder merged = new();
            if (!string.IsNullOrWhiteSpace(codec))
            {
                merged.Append(codec);
                merged.Append(' ');
            }

            if (engineErrorMessages != null)
            {
                for (int i = 0; i < engineErrorMessages.Count; i++)
                {
                    if (string.IsNullOrWhiteSpace(engineErrorMessages[i]))
                    {
                        continue;
                    }

                    merged.Append(engineErrorMessages[i]);
                    merged.Append(' ');
                }
            }

            string text = merged.ToString().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(text))
            {
                return ThumbnailFailurePlaceholderKind.None;
            }

            if (ContainsAnyKeyword(text, DrmErrorKeywords))
            {
                return ThumbnailFailurePlaceholderKind.DrmSuspected;
            }

            if (ContainsAnyKeyword(text, UnsupportedErrorKeywords))
            {
                return ThumbnailFailurePlaceholderKind.UnsupportedCodec;
            }

            return ThumbnailFailurePlaceholderKind.None;
        }

        public static bool TryCreate(
            ThumbnailJobContext context,
            ThumbnailFailurePlaceholderKind kind,
            out string detail
        )
        {
            detail = "";
            if (kind == ThumbnailFailurePlaceholderKind.None || context == null)
            {
                return false;
            }

            try
            {
                int columns = Math.Max(1, context.PanelColumns);
                int rows = Math.Max(1, context.PanelRows);
                int width = Math.Max(1, context.PanelWidth > 0 ? context.PanelWidth : 120);
                int height = Math.Max(1, context.PanelHeight > 0 ? context.PanelHeight : 90);
                int count = columns * rows;

                List<Bitmap> frames = [];
                try
                {
                    for (int i = 0; i < count; i++)
                    {
                        frames.Add(CreatePlaceholderFrame(width, height, kind));
                    }

                    bool saved = ThumbnailImageWriter.SaveCombinedThumbnail(
                        context.SaveThumbFileName,
                        frames,
                        columns,
                        rows
                    );
                    if (!saved || !Path.Exists(context.SaveThumbFileName))
                    {
                        detail = "placeholder save failed";
                        return false;
                    }
                }
                finally
                {
                    for (int i = 0; i < frames.Count; i++)
                    {
                        frames[i]?.Dispose();
                    }
                }

                if (context.ThumbInfo != null)
                {
                    WhiteBrowserThumbInfoSerializer.AppendToJpeg(
                        context.SaveThumbFileName,
                        context.ThumbInfo.ToSheetSpec()
                    );
                }

                detail = "placeholder saved";
                return true;
            }
            catch (Exception ex)
            {
                detail = ex.Message;
                return false;
            }
        }

        public static string ResolveProcessEngineId(ThumbnailFailurePlaceholderKind kind)
        {
            return kind switch
            {
                ThumbnailFailurePlaceholderKind.DrmSuspected => "placeholder-drm",
                ThumbnailFailurePlaceholderKind.UnsupportedCodec => "placeholder-unsupported",
                _ => "placeholder-unknown",
            };
        }

        private static bool ContainsAnyKeyword(string text, IReadOnlyList<string> keywords)
        {
            if (string.IsNullOrWhiteSpace(text) || keywords == null)
            {
                return false;
            }

            for (int i = 0; i < keywords.Count; i++)
            {
                string keyword = keywords[i];
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    continue;
                }

                if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static Bitmap CreatePlaceholderFrame(
            int width,
            int height,
            ThumbnailFailurePlaceholderKind kind
        )
        {
            Bitmap bitmap = new(width, height, PixelFormat.Format24bppRgb);
            using Graphics g = Graphics.FromImage(bitmap);

            Color background = kind == ThumbnailFailurePlaceholderKind.DrmSuspected
                ? Color.FromArgb(90, 35, 35)
                : Color.FromArgb(45, 45, 45);
            Color stripe = kind == ThumbnailFailurePlaceholderKind.DrmSuspected
                ? Color.FromArgb(170, 65, 65)
                : Color.FromArgb(85, 110, 130);
            string title = kind == ThumbnailFailurePlaceholderKind.DrmSuspected
                ? "DRM?"
                : "CODEC NG";
            string subtitle = kind == ThumbnailFailurePlaceholderKind.DrmSuspected
                ? "保護コンテンツの可能性"
                : "非対応/破損の可能性";

            g.Clear(background);
            using (Brush stripeBrush = new SolidBrush(stripe))
            {
                g.FillRectangle(stripeBrush, 0, 0, width, Math.Max(18, height / 4));
            }
            using (Pen borderPen = new(Color.FromArgb(220, 220, 220), 1))
            {
                g.DrawRectangle(borderPen, 0, 0, width - 1, height - 1);
            }

            float titleSize = Math.Max(8f, Math.Min(16f, width * 0.11f));
            float subtitleSize = Math.Max(6f, Math.Min(11f, width * 0.065f));
            using Font titleFont = new("Yu Gothic UI", titleSize, FontStyle.Bold, GraphicsUnit.Point);
            using Font subtitleFont = new(
                "Yu Gothic UI",
                subtitleSize,
                FontStyle.Regular,
                GraphicsUnit.Point
            );
            using Brush textBrush = new SolidBrush(Color.WhiteSmoke);
            using StringFormat centered = new()
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };

            Rectangle titleRect = new(0, Math.Max(18, height / 4), width, Math.Max(16, height / 3));
            Rectangle subtitleRect = new(
                0,
                titleRect.Bottom,
                width,
                Math.Max(14, height - titleRect.Bottom - 2)
            );
            g.DrawString(title, titleFont, textBrush, titleRect, centered);
            g.DrawString(subtitle, subtitleFont, textBrush, subtitleRect, centered);

            return bitmap;
        }
    }

    internal enum ThumbnailFailurePlaceholderKind
    {
        None = 0,
        DrmSuspected = 1,
        UnsupportedCodec = 2,
    }
}
