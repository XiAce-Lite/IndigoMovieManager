using System.Drawing;
using System.Linq;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// JPEG保存と WB互換メタ確認を一本化し、後段の「メタ欠落扱い」を減らす。
    /// </summary>
    public static class ThumbnailJpegMetadataWriter
    {
        // Bitmap保存が必要な経路は、まず JPEG を安定保存してからメタ確認へ進める。
        public static bool TrySaveJpegWithThumbInfo(
            Image image,
            string savePath,
            ThumbInfo thumbInfo,
            out string errorMessage
        )
        {
            errorMessage = "";
            if (!ThumbnailImageWriter.TrySaveJpegWithRetry(image, savePath, out errorMessage))
            {
                return false;
            }

            return TryEnsureThumbInfoMetadata(savePath, thumbInfo, out errorMessage);
        }

        // 既に保存済みの jpg でも、必要な ThumbInfo が無ければ追記して確認する。
        public static bool TryEnsureThumbInfoMetadata(
            string savePath,
            ThumbInfo thumbInfo,
            out string errorMessage
        )
        {
            errorMessage = "";
            if (string.IsNullOrWhiteSpace(savePath))
            {
                errorMessage = "save path is empty";
                return false;
            }

            if (thumbInfo == null)
            {
                return true;
            }

            ThumbnailSheetSpec expectedSpec = thumbInfo.ToSheetSpec();
            if (HasExpectedThumbInfo(savePath, expectedSpec))
            {
                return true;
            }

            try
            {
                WhiteBrowserThumbInfoSerializer.AppendToJpeg(savePath, expectedSpec);
            }
            catch (Exception ex)
            {
                errorMessage = $"thumbnail metadata append failed: {ex.Message}";
                ThumbnailRuntimeLog.Write(
                    "thumbnail",
                    $"thumb metadata append failed: path='{savePath}', err='{ex.Message}'"
                );
                return false;
            }

            if (HasExpectedThumbInfo(savePath, expectedSpec))
            {
                return true;
            }

            errorMessage = "thumbnail metadata is missing";
            ThumbnailRuntimeLog.Write(
                "thumbnail",
                $"thumb metadata verify failed: path='{savePath}'"
            );
            return false;
        }

        // 同一仕様が既に入っている時は追記を省略し、末尾メタの重複を避ける。
        private static bool HasExpectedThumbInfo(string savePath, ThumbnailSheetSpec expectedSpec)
        {
            if (
                !WhiteBrowserThumbInfoSerializer.TryReadFromJpeg(
                    savePath,
                    out ThumbnailSheetSpec actualSpec
                )
            )
            {
                return false;
            }

            return actualSpec != null
                && actualSpec.ThumbCount == expectedSpec.ThumbCount
                && actualSpec.ThumbWidth == expectedSpec.ThumbWidth
                && actualSpec.ThumbHeight == expectedSpec.ThumbHeight
                && actualSpec.ThumbColumns == expectedSpec.ThumbColumns
                && actualSpec.ThumbRows == expectedSpec.ThumbRows
                && actualSpec.CaptureSeconds.SequenceEqual(expectedSpec.CaptureSeconds ?? []);
        }
    }
}
