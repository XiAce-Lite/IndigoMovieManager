using System.Drawing;
using System.IO;
using Force.Crc32;
using OpenCvSharp;
using OpenCvSharp.Extensions;

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
            var fileName = filePath;
            if (!Path.Exists(filePath))
            {
                return "";
            }

            try
            {
                using var reader = new BinaryReader(
                    new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read)
                );
                var buff = reader.ReadBytes(1024 * 128);
                var algorithm = new Crc32Algorithm();
                var crc32AsBytes = algorithm.ComputeHash(buff);
                return BitConverter.ToString(crc32AsBytes, 0).Replace("-", string.Empty).ToLower();
            }
            catch (UnauthorizedAccessException)
            {
                throw;
            }
        }

        public static string ConvertTagsWithNewLine(List<string> tags)
        {
            // タグ編集画面で扱いやすいように、重複排除して改行連結する。
            string tagWithNewLine = "";
            IEnumerable<string> result = tags.Distinct();

            foreach (var tagItem in result)
            {
                if (string.IsNullOrEmpty(tagWithNewLine))
                {
                    tagWithNewLine = tagItem;
                }
                else
                {
                    tagWithNewLine += (Environment.NewLine + tagItem);
                }
            }
            return tagWithNewLine;
        }

        /// <summary>
        /// 【サムネイル生成の事前準備】
        /// 安定してサムネイルを作成するため、またはリトライ時に古いゴミを残さないため、
        /// 作業用の一時フォルダ（temp）内に残っている古い一時画像（*.jpg）を一括削除するお掃除メソッドです。
        /// </summary>
        public static void ClearTempJpg()
        {
            // 失敗時の再生成を安定させるため、temp配下のjpgを先に掃除する。
            // テンプフォルダ取得
            var currentPath = Directory.GetCurrentDirectory();
            var tempPath = Path.Combine(currentPath, "temp"); //Path.GetTempPath();
            if (!Path.Exists(tempPath))
            {
                return;
            }

            // 既存テンプファイルの削除
            var oldTempFiles = Directory.GetFiles(tempPath, $"*.jpg", SearchOption.AllDirectories);
            foreach (var oldFile in oldTempFiles)
            {
                if (File.Exists(oldFile))
                {
                    File.Delete(oldFile);
                }
            }
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
            // 行ごとに横結合してから、最後に縦結合して1枚へまとめる。
            List<Mat> dst = [];
            Mat all = new();
            for (int j = 0; j < rows; j++)
            {
                List<Mat> src = [];
                for (int i = 0; i < columns; i++)
                {
                    src.Add(new Mat(paths[i + (j * columns)]));
                }
                dst.Add(new Mat());
                Cv2.HConcat(src, dst[j]);
            }

            Cv2.VConcat(dst, all);
            return BitmapConverter.ToBitmap(all);
        }
    }
}
