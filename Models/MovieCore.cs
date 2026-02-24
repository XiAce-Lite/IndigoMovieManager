using System.IO;

namespace IndigoMovieManager
{
    /// <summary>
    /// MovieInfo と MovieRecords の共通項目をまとめるコアモデル。
    /// 段階移行の受け皿として使い、DB層とUI層の橋渡しを行う。
    /// </summary>
    public class MovieCore
    {
        private string moviePath = "";
        private string moviePathNormalized = "";

        public long MovieId { get; set; }
        public string MovieName { get; set; } = "";
        public string MoviePath
        {
            get { return moviePath; }
            set
            {
                // 生パスはそのまま保持し、必要な場面だけ正規化パスを使えるようにする。
                moviePath = value ?? "";
                moviePathNormalized = NormalizeMoviePath(value);
            }
        }
        // 外部ライブラリへ渡す用の正規化済みパス。
        public string MoviePathNormalized { get { return moviePathNormalized; } }
        public long MovieLength { get; set; }
        public long MovieSize { get; set; }
        public DateTime LastDate { get; set; } = DateTime.Now;
        public DateTime FileDate { get; set; } = DateTime.Now;
        public DateTime RegistDate { get; set; } = DateTime.Now;
        public long Score { get; set; }
        public long ViewCount { get; set; }
        public string Hash { get; set; } = "";
        public string Container { get; set; } = "";
        public string Video { get; set; } = "";
        public string Audio { get; set; } = "";
        public string Extra { get; set; } = "";
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Album { get; set; } = "";
        public string Grouping { get; set; } = "";
        public string Writer { get; set; } = "";
        public string Genre { get; set; } = "";
        public string Track { get; set; } = "";
        public string Camera { get; set; } = "";
        public string CreateTime { get; set; } = "";
        public string Kana { get; set; } = "";
        public string Roma { get; set; } = "";
        public string Tags { get; set; } = "";
        public string Comment1 { get; set; } = "";
        public string Comment2 { get; set; } = "";
        public string Comment3 { get; set; } = "";
        public double FPS { get; set; } = 30;
        public double TotalFrames { get; set; }

        /// <summary>
        /// Movie系モデルで共通利用するパス正規化。
        /// - 前後空白と外側のダブルクォートを除去
        /// - フルパス判定できるものは Path.GetFullPath で表記ゆれを吸収
        /// - それ以外（bookmarkの論理名など）はそのまま保持
        /// </summary>
        public static string NormalizeMoviePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) { return ""; }

            string normalized = path.Trim();
            if (normalized.Length >= 2 &&
                normalized.StartsWith('"') &&
                normalized.EndsWith('"'))
            {
                normalized = normalized[1..^1].Trim();
            }

            try
            {
                // 実ファイルのフルパスのみ正規化し、論理パスは変更しない。
                if (Path.IsPathFullyQualified(normalized))
                {
                    return Path.GetFullPath(normalized);
                }
            }
            catch
            {
                // 不正文字等が混じる場合は、元の文字列を保持して上位で判定する。
            }

            return normalized;
        }
    }
}
