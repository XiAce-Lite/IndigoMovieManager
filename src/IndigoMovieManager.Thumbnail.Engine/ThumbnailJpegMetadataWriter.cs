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
            if (thumbInfo == null)
            {
                errorMessage = "thumb info is null";
                return false;
            }

            if (!ThumbnailImageWriter.TrySaveJpegWithRetry(image, savePath, out errorMessage))
            {
                return false;
            }

            if (TryEnsureThumbInfoMetadata(savePath, thumbInfo, out errorMessage))
            {
                return true;
            }

            TryDeleteIncompleteJpeg(savePath, ref errorMessage);
            return false;
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
                errorMessage = "thumb info is null";
                return false;
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

        // 保存後に契約どおりのメタが載らなかった時は、中途半端な jpg を残さない。
        internal static void TryDeleteIncompleteJpeg(string savePath, ref string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(savePath) || !File.Exists(savePath))
            {
                return;
            }

            try
            {
                File.Delete(savePath);
            }
            catch (Exception ex)
            {
                errorMessage = string.IsNullOrWhiteSpace(errorMessage)
                    ? $"failed to delete incomplete jpeg: {ex.Message}"
                    : $"{errorMessage} / failed to delete incomplete jpeg: {ex.Message}";
                ThumbnailRuntimeLog.Write(
                    "thumbnail",
                    $"incomplete jpeg delete failed: path='{savePath}', err='{ex.Message}'"
                );
            }
        }
    }
}
