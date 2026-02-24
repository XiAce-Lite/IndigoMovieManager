using System.IO;
using IndigoMovieManager.Thumbnail;
using OpenCvSharp;

namespace IndigoMovieManager
{
    /// <summary>
    /// 動画ファイルから直接メタ情報（FPS・尺・ファイルサイズ・ハッシュなど）を読み取って保持する、
    /// 「ファイルから取得した生のデータ」の入れ物となるモデルクラス。
    /// （※データベースから読むデータではなく、ローカルファイルから収集した情報用）
    /// </summary>
    public class MovieInfo : MovieCore
    {
        // 旧実装互換: 既存コードは Tag 名で参照している箇所があるため、基底の Tags への別名プロパティを残しておく。
        public string Tag => Tags;

        /// <summary>
        /// コンストラクタ。指定した動画ファイルを解析し、基本情報を自クラス（MovieCore派生）に格納する。
        /// </summary>
        /// <param name="fileFullPath">解析対象のファイルフルパス</param>
        /// <param name="noHash">ハッシュ計算を省略するか。重い処理を飛ばしたい場合（Bookmark登録等）は true にする</param>
        public MovieInfo(string fileFullPath, bool noHash = false)
        {
            // 1. パスの保持
            // 生パスと正規化パスを両方保持する。
            // 生パス: DB保存やUI表示、Queueへの引渡しなど、システム内で標準的に扱う元の値。
            // 正規化: OpenCV等の外部ライブラリへ処理を依頼する際に渡す、表記ゆれをなくした値。
            string rawPath = fileFullPath ?? "";
            string normalizedPath = NormalizeMoviePath(fileFullPath);

            // 2. 動画の長さを取得する（OpenCVを利用）
            // OpenCVはパス文字列に癖があるため、ここだけは正規化したパスを使う。
            using var capture = new VideoCapture(normalizedPath);

            // フレーム情報を取りに行く前にGrab()を空打ちした方が高速に動作するためのおまじない。
            capture.Grab();

            TotalFrames = capture.Get(VideoCaptureProperties.FrameCount);
            FPS = capture.Get(VideoCaptureProperties.Fps);
            if (FPS <= 0)
            {
                FPS = 30; // 万一取得できなければ30fpsにフォールバック
            }

            double durationSec = TotalFrames / FPS;

            // 3. ファイルシステムの属性（ファイルサイズ、更新日時など）を取得
            FileInfo file = new(rawPath);

            var now = DateTime.Now;
            // 現在時刻から「秒以下の端数（ミリ秒など）」を切り捨ててDB格納用に調整
            var result = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
            LastDate = result;
            RegistDate = result;

            // 4. ベースクラス(MovieCore)の各プロパティへ抽出・計算したメタ情報を流し込む
            MovieName = Path.GetFileNameWithoutExtension(rawPath);
            MoviePath = rawPath;
            MovieLength = (long)durationSec;
            MovieSize = file.Length;

            // 5. ハッシュ値の計算
            // ハッシュ計算は巨大ファイル相手だと時間がかかるため、noHashオプションでスキップできる設計。
            if (!noHash)
            {
                Hash = Tools.GetHashCRC32(rawPath);
            }

            // 万一のためにファイルの更新日時も、秒以下の端数を切り捨てて格納しておく
            var lastWrite = file.LastWriteTime;
            result = lastWrite.AddTicks(-(lastWrite.Ticks % TimeSpan.TicksPerSecond));
            FileDate = result;
        }
    }
}
