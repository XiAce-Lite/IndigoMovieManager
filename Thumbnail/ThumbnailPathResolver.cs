using System.IO;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// サムネイル画像の保存パスと命名規則を一元管理する。
    /// 生成時・表示時・削除時で同じ規則を使うことで不一致を防ぐ。
    /// </summary>
    internal static class ThumbnailPathResolver
    {
        /// <summary>
        /// タブ情報、動画パス（またはムービー名）、ハッシュからサムネイルの保存フルパスを構築する。
        /// </summary>
        public static string BuildThumbnailPath(TabInfo tbi, string movieFullPath, string hash)
        {
            string fileName = BuildThumbnailFileName(movieFullPath, hash);
            return Path.Combine(tbi.OutPath, fileName);
        }

        /// <summary>
        /// サムネイルファイル名を構築する（ディレクトリを含まない）。
        /// 形式: "{movieName}.#{hash}.jpg"
        /// </summary>
        public static string BuildThumbnailFileName(string movieFullPath, string hash)
        {
            string movieName = Path.GetFileNameWithoutExtension(movieFullPath)?.ToLower() ?? "";
            return $"{movieName}.#{hash}.jpg";
        }
    }
}
