using System.IO;
using FFMediaToolkit;
using FFMediaToolkit.Decoding;
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
        private const double DefaultFps = 30;
        private static readonly object FfmpegLoadSync = new();
        private static bool ffmpegLoadAttempted;
        private static bool ffmpegLoadSucceeded;

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

            // 2. 動画の長さを取得する（FFMediaToolkit優先、失敗時のみOpenCV）
            // ベンチで検証した取得式（AvgFrameRate / NumberOfFrames / Duration）をそのまま流用する。
            double fps = DefaultFps;
            double totalFrames = 0;
            double durationSec = 0;
            bool readByFfMediaToolkit =
                EnsureFfMediaToolkitLoaded()
                && TryReadByFfMediaToolkit(rawPath, out fps, out totalFrames, out durationSec);
            if (!readByFfMediaToolkit)
            {
                _ = TryReadByOpenCv(normalizedPath, out fps, out totalFrames, out durationSec);
            }

            FPS = NormalizeFps(fps);
            TotalFrames = NormalizeTotalFrames(totalFrames);
            MovieLength = (long)NormalizeDurationSec(durationSec, TotalFrames, FPS);

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

        // FFMediaToolkitのロードはプロセス中1回だけ試し、失敗時は毎回再試行しない。
        private static bool EnsureFfMediaToolkitLoaded()
        {
            lock (FfmpegLoadSync)
            {
                if (ffmpegLoadAttempted)
                {
                    return ffmpegLoadSucceeded;
                }

                ffmpegLoadAttempted = true;
                string ffmpegSharedDir = Path.Combine(
                    AppContext.BaseDirectory,
                    "tools",
                    "ffmpeg-shared"
                );
                string lastError = "";
                try
                {
                    if (!Directory.Exists(ffmpegSharedDir))
                    {
                        lastError = "tools/ffmpeg-shared folder not found";
                    }
                    else if (!HasRequiredSharedDllSet(ffmpegSharedDir))
                    {
                        lastError = "required shared dll set is incomplete";
                    }
                    else
                    {
                        FFmpegLoader.FFmpegPath = ffmpegSharedDir;
                        FFmpegLoader.LoadFFmpeg();
                        ffmpegLoadSucceeded = true;
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                }

                if (ffmpegLoadSucceeded)
                {
                    DebugRuntimeLog.Write(
                        "movieinfo",
                        $"ffmediatoolkit init ok: dir='{ffmpegSharedDir}'"
                    );
                }
                else
                {
                    string detail = string.IsNullOrWhiteSpace(lastError)
                        ? "shared dll not found"
                        : lastError;
                    DebugRuntimeLog.Write(
                        "movieinfo",
                        $"ffmediatoolkit init fallback to opencv: {detail}"
                    );
                }

                return ffmpegLoadSucceeded;
            }
        }

        // ベンチ済みロジック: AvgFrameRate / NumberOfFrames / Duration を使ってメタ値を作る。
        private static bool TryReadByFfMediaToolkit(
            string inputPath,
            out double fps,
            out double totalFrames,
            out double durationSec
        )
        {
            fps = DefaultFps;
            totalFrames = 0;
            durationSec = 0;

            try
            {
                MediaOptions options = new() { StreamsToLoad = MediaMode.Video };
                using var mediaFile = MediaFile.Open(inputPath, options);
                var videoInfo = mediaFile.Video.Info;

                fps = NormalizeFps(videoInfo.AvgFrameRate);
                totalFrames =
                    videoInfo.NumberOfFrames
                    ?? Math.Truncate(videoInfo.Duration.TotalSeconds * fps);
                durationSec = NormalizeDurationSec(videoInfo.Duration.TotalSeconds, totalFrames, fps);
                return true;
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "movieinfo",
                    $"ffmediatoolkit read failed: path='{inputPath}', reason={ex.GetType().Name}"
                );
                return false;
            }
        }

        // FFMediaToolkitが使えない時の後方互換経路。
        private static bool TryReadByOpenCv(
            string inputPath,
            out double fps,
            out double totalFrames,
            out double durationSec
        )
        {
            fps = DefaultFps;
            totalFrames = 0;
            durationSec = 0;

            try
            {
                using var capture = new VideoCapture(inputPath);
                if (!capture.IsOpened())
                {
                    return false;
                }

                capture.Grab();
                totalFrames = capture.Get(VideoCaptureProperties.FrameCount);
                fps = NormalizeFps(capture.Get(VideoCaptureProperties.Fps));
                durationSec = NormalizeDurationSec(totalFrames / fps, totalFrames, fps);
                return true;
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "movieinfo",
                    $"opencv read failed: path='{inputPath}', reason={ex.GetType().Name}"
                );
                return false;
            }
        }

        private static bool HasRequiredSharedDllSet(string dir)
        {
            return HasDll(dir, "avcodec*.dll")
                && HasDll(dir, "avformat*.dll")
                && HasDll(dir, "avutil*.dll")
                && HasDll(dir, "swscale*.dll")
                && HasDll(dir, "swresample*.dll");
        }

        private static bool HasDll(string dir, string pattern)
        {
            try
            {
                return Directory.EnumerateFiles(dir, pattern, SearchOption.TopDirectoryOnly).Any();
            }
            catch
            {
                return false;
            }
        }

        private static double NormalizeFps(double fps)
        {
            return IsFinitePositive(fps) ? fps : DefaultFps;
        }

        private static double NormalizeTotalFrames(double totalFrames)
        {
            return IsFinitePositive(totalFrames) ? Math.Truncate(totalFrames) : 0;
        }

        private static double NormalizeDurationSec(double durationSec, double totalFrames, double fps)
        {
            if (IsFinitePositive(durationSec))
            {
                return durationSec;
            }

            if (IsFinitePositive(totalFrames) && IsFinitePositive(fps))
            {
                return totalFrames / fps;
            }

            return 0;
        }

        private static bool IsFinitePositive(double value)
        {
            return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
