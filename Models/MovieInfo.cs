using OpenCvSharp;
using IndigoMovieManager.Thumbnail;
using System.IO;

namespace IndigoMovieManager
{
    /// <summary>
    /// 「ファイルから取得した生メタ情報」の入れ物
    /// </summary>
    public class MovieInfo : MovieCore
    {
        // 旧実装互換: 既存コードは Tag 名で参照しているため、Tags への別名を残す。
        public string Tag => Tags;

        public MovieInfo(string fileFullPath, bool noHash = false)
        {
            // 生パスと正規化パスを両方保持する。
            // 生パス: DB/UI/Queueで扱う元の値
            // 正規化: OpenCV等のライブラリへ渡す値
            string rawPath = fileFullPath ?? "";
            string normalizedPath = NormalizeMoviePath(fileFullPath);

            // 動画ファイルから、DB登録に必要な最小メタ情報を組み立てる。
            // noHash=true は重いハッシュ計算を省略したい場面（例: bookmark）で使う。
            // OpenCVはパス文字列に癖があるため、ここだけ正規化パスを使う。
            using var capture = new VideoCapture(normalizedPath);
            // なんか、Grabしないと遅いって話をどっかで見たので。
            capture.Grab();
            TotalFrames = capture.Get(VideoCaptureProperties.FrameCount);
            FPS = capture.Get(VideoCaptureProperties.Fps);
            if (FPS <= 0)
            {
                FPS = 30;
            }

            double durationSec = TotalFrames / FPS;
            FileInfo file = new(rawPath);

            var now = DateTime.Now;
            var result = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
            LastDate = result;
            RegistDate = result;

            MovieName = Path.GetFileNameWithoutExtension(rawPath);
            MoviePath = rawPath;
            MovieLength = (long)durationSec;
            MovieSize = file.Length;
            if (!noHash)
            {
                Hash = Tools.GetHashCRC32(rawPath);
            }

            var lastWrite = file.LastWriteTime;
            result = lastWrite.AddTicks(-(lastWrite.Ticks % TimeSpan.TicksPerSecond));
            FileDate = result;
        }
    }
}
