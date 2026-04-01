using System.Drawing;
using System.Drawing.Imaging;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 動画の隣にある同名画像を、通常サムネ資産(jpg + WB互換メタ)として取り込む。
    /// </summary>
    internal sealed class ThumbnailSourceImageImportCoordinator
    {
        private static readonly string[] SupportedImageExtensions = [".jpg", ".jpeg", ".png"];

        // queue/precheck から「同名画像があるか」を軽く判定したい時に使う。
        internal static bool HasImportableSourceImage(string movieFullPath)
        {
            return TryResolveSourceImagePath(movieFullPath, out _);
        }

        // 同名画像が見つかった時だけ、既存の管理サムネ形式へ正規化保存する。
        internal bool TryImport(
            ThumbnailLayoutProfile layoutProfile,
            string movieFullPath,
            string saveThumbFileName,
            out string sourceImagePath,
            out string errorMessage
        )
        {
            sourceImagePath = "";
            errorMessage = "";

            if (
                layoutProfile == null
                || string.IsNullOrWhiteSpace(movieFullPath)
                || string.IsNullOrWhiteSpace(saveThumbFileName)
            )
            {
                return false;
            }

            if (!TryResolveSourceImagePath(movieFullPath, out sourceImagePath))
            {
                return false;
            }

            int panelWidth = Math.Max(1, layoutProfile.Width);
            int panelHeight = Math.Max(1, layoutProfile.Height);
            int columns = Math.Max(1, layoutProfile.Columns);
            int rows = Math.Max(1, layoutProfile.Rows);

            try
            {
                using Bitmap sourceBitmap = LoadBitmapWithoutLock(sourceImagePath);
                using Bitmap panelBitmap = ThumbnailImageTransformHelper.ResizeBitmap(
                    sourceBitmap,
                    new Size(panelWidth, panelHeight)
                );
                using Bitmap sheetBitmap = BuildRepeatedSheet(
                    panelBitmap,
                    panelWidth,
                    panelHeight,
                    columns,
                    rows
                );

                ThumbInfo thumbInfo = ThumbnailAutoThumbInfoBuilder.Build(layoutProfile, null);
                return ThumbnailJpegMetadataWriter.TrySaveJpegWithThumbInfo(
                    sheetBitmap,
                    saveThumbFileName,
                    thumbInfo,
                    out errorMessage
                );
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        private static bool TryResolveSourceImagePath(
            string movieFullPath,
            out string sourceImagePath
        )
        {
            sourceImagePath = "";
            if (string.IsNullOrWhiteSpace(movieFullPath))
            {
                return false;
            }

            for (int i = 0; i < SupportedImageExtensions.Length; i++)
            {
                string candidatePath = Path.ChangeExtension(
                    movieFullPath,
                    SupportedImageExtensions[i]
                );
                if (!HasUsableFile(candidatePath))
                {
                    continue;
                }

                sourceImagePath = candidatePath;
                return true;
            }

            return false;
        }

        // Image.FromStream を使ってロック無しで読み込み、元stream をすぐ解放する。
        private static Bitmap LoadBitmapWithoutLock(string imagePath)
        {
            using FileStream fs = new(
                imagePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete
            );
            using Image image = Image.FromStream(
                fs,
                useEmbeddedColorManagement: false,
                validateImageData: true
            );
            return new Bitmap(image);
        }

        // Small/Big/List/Big10 も既存契約どおりのシート形状へそろえる。
        private static Bitmap BuildRepeatedSheet(
            Bitmap panelBitmap,
            int panelWidth,
            int panelHeight,
            int columns,
            int rows
        )
        {
            Bitmap canvas = new(
                panelWidth * columns,
                panelHeight * rows,
                PixelFormat.Format24bppRgb
            );
            using Graphics g = Graphics.FromImage(canvas);
            g.Clear(Color.Black);

            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    Rectangle destRect = new(
                        column * panelWidth,
                        row * panelHeight,
                        panelWidth,
                        panelHeight
                    );
                    g.DrawImage(panelBitmap, destRect);
                }
            }

            return canvas;
        }

        private static bool HasUsableFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                FileInfo fi = new(path);
                return fi.Exists && fi.Length > 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
