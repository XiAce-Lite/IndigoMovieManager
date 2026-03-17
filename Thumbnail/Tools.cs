using System.Drawing;

namespace IndigoMovieManager.Thumbnail
{
    public class Tools
    {
        /// <summary>
        /// 動画ファイルからCRC32ハッシュを高速に計算します。
        /// ファイル全体ではなく、先頭128KBだけを読み込むことで巨大な動画ファイルでも一瞬で処理できるようにしています。
        /// このハッシュはサムネイルファイル名の一部として使われ、同名別ファイルの衝突を防ぎます。
        /// </summary>
        /// <param name="filePath">対象のファイル</param>
        /// <returns></returns>
        public static string GetHashCRC32(string filePath = "")
        {
            return MovieHashCalculator.GetHashCrc32(filePath);
        }

        public static string ConvertTagsWithNewLine(List<string> tags)
        {
            return ThumbnailTagFormatter.ConvertTagsWithNewLine(tags);
        }

        /// <summary>
        /// 【サムネイル生成の事前準備】
        /// 安定してサムネイルを作成するため、またはリトライ時に古いゴミを残さないため、
        /// 作業用の一時フォルダ（temp）内に残っている古い一時画像（*.jpg）を一括削除するお掃除メソッドです。
        /// </summary>
        public static void ClearTempJpg()
        {
            ThumbnailTempFileCleaner.ClearCurrentWorkingTempJpg();
        }

        /// <summary>
        /// 【サムネイル生成の最終工程（ffmpegフォールバック用）】
        /// ffmpegで抽出したバラバラのサムネイル画像（複数枚）を、
        /// OpenCV（Cv2.HConcat / VConcat）を使って1枚のタイル状の画像に綺麗に敷き詰めて結合するメソッドです。
        /// </summary>
        /// <param name="paths">元サムネ(テンポラリファイル)のパス群</param>
        /// <param name="columns">横のパネル数（列数）</param>
        /// <param name="rows">縦の行数</param>
        /// <returns>結合された1枚のBitmap画像</returns>
        public static Bitmap ConcatImages(List<string> paths, int columns, int rows)
        {
            return ThumbnailSheetComposer.ConcatImages(paths, columns, rows);
        }
    }
}
