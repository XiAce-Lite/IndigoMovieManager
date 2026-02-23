using Force.Crc32;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System.Drawing;
using System.IO;

namespace IndigoMovieManager.Thumbnail
{
    public class Tools
    {
        /// <summary>
        /// CRC32ハッシュの取得。先頭128K分でハッシュ作る。
        /// </summary>
        /// <param name="filePath">対象のファイル</param>
        /// <returns></returns>
        public static string GetHashCRC32(string filePath = "")
        {
            var fileName = filePath;
            if (!Path.Exists(filePath)) { return ""; }

            try
            {
                using var reader = new BinaryReader(new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read));
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
        /// テンプフォルダのJpeg削除
        /// </summary>
        public static void ClearTempJpg ()
        {
            // 失敗時の再生成を安定させるため、temp配下のjpgを先に掃除する。
            // テンプフォルダ取得
            var currentPath = Directory.GetCurrentDirectory();
            var tempPath = Path.Combine(currentPath, "temp");  //Path.GetTempPath();
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
        /// サムネを並べる
        /// </summary>
        /// <param name="paths">元サムネ(テンポラリファイル)のパス群</param>
        /// <param name="columns">横のパネル数</param>
        /// <param name="rows">縦の行数</param>
        /// <returns></returns>
        public static Bitmap ConcatImages(List<string>paths, int columns, int rows)
        {
            // 行ごとに横結合してから、最後に縦結合して1枚へまとめる。
            List<Mat> dst = [];
            Mat all = new();
            for (int j = 0; j < rows; j++)
            {
                List<Mat> src = [];
                for (int i = 0; i < columns; i++)
                {
                    src.Add(new Mat(paths[i+(j * columns)]));
                }
                dst.Add(new Mat());
                Cv2.HConcat(src, dst[j]);
            }

            Cv2.VConcat(dst, all);
            return BitmapConverter.ToBitmap(all);
        }
    }
}
