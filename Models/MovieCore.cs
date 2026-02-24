using System.IO;

namespace IndigoMovieManager
{
    /// <summary>
    /// アプリケーション全体（DB・UI・キュー等）で使い回す基本的な動画情報をまとめたコアモデル。
    /// 旧実装（MovieInfo, MovieRecords 等）から段階的にこのクラスへと集約していくための受け皿となる。
    /// </summary>
    public class MovieCore
    {
        private string moviePath = "";
        private string moviePathNormalized = "";

        // DBにおける主キー（連番）
        public long MovieId { get; set; }

        // 動画の表示名（基本はファイル名等）
        public string MovieName { get; set; } = "";

        /// <summary>
        /// 動画のファイルパス。
        /// setter内で「生パス」の保持と「正規化パス(絵文字対策)」への変換を同時に行い、
        /// UIやDB用には生パスを、外部ライブラリ（OpenCV等）には正規化パスを渡せるようにする。
        /// </summary>
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

        // 外部ライブラリへ渡す用の正規化済みパス（読み取り専用）
        public string MoviePathNormalized
        {
            get { return moviePathNormalized; }
        }

        // 再生時間（秒数）
        public long MovieLength { get; set; }

        // ファイルサイズ（バイト）
        public long MovieSize { get; set; }

        public DateTime LastDate { get; set; } = DateTime.Now;
        public DateTime FileDate { get; set; } = DateTime.Now;
        public DateTime RegistDate { get; set; } = DateTime.Now;

        // ユーザー評価スコア
        public long Score { get; set; }

        // 再生回数
        public long ViewCount { get; set; }

        // ファイルのハッシュ値（重複チェック用などに使用）
        public string Hash { get; set; } = "";

        // sinku.exe等から取得する詳細なメディアメタデータ群
        public string Container { get; set; } = "";
        public string Video { get; set; } = "";
        public string Audio { get; set; } = "";
        public string Extra { get; set; } = "";

        // その他、動画固有のタグや属性情報群（MP4タグ等由来）
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Album { get; set; } = "";
        public string Grouping { get; set; } = "";
        public string Writer { get; set; } = "";
        public string Genre { get; set; } = "";
        public string Track { get; set; } = "";
        public string Camera { get; set; } = "";
        public string CreateTime { get; set; } = "";

        // 検索用（よみがななど）
        public string Kana { get; set; } = "";
        public string Roma { get; set; } = "";

        // ユーザー付与のタグ文字列（複数行やカンマ区切りなどを想定）
        public string Tags { get; set; } = "";

        // 自由入力のコメント欄
        public string Comment1 { get; set; } = "";
        public string Comment2 { get; set; } = "";
        public string Comment3 { get; set; } = "";

        // OpenCV等から取得する映像プロパティ
        public double FPS { get; set; } = 30;
        public double TotalFrames { get; set; }

        /// <summary>
        /// Movie系モデルで共通利用するパス正規化メソッド。主目的は絵文字対策
        /// 1. 前後空白と外側のダブルクォートを除去する。
        /// 2. フルパス判定できるものは Path.GetFullPath で記述の揺れ（..\ など）を吸収する。
        /// 3. ドライブレターがない等、フルパス判定できない論理名などはそのまま保持する。
        /// </summary>
        public static string NormalizeMoviePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "";
            }

            string normalized = path.Trim();

            // "C:\hoge\test.mp4" のように不必要な括弧がついていれば剥がす
            if (normalized.Length >= 2 && normalized.StartsWith('"') && normalized.EndsWith('"'))
            {
                normalized = normalized[1..^1].Trim();
            }

            try
            {
                // 実ファイルのフルパスのみ正規化し、論理パス(例えばブックマークの仮想名)は変更しない。
                if (Path.IsPathFullyQualified(normalized))
                {
                    return Path.GetFullPath(normalized);
                }
            }
            catch
            {
                // 例外（不正文字等が混じる場合）は、元の文字列を維持して上位層で扱うようにする。
            }

            return normalized;
        }
    }
}
