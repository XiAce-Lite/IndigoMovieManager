using System.IO;
using FFMediaToolkit;
using FFMediaToolkit.Decoding;
using IndigoMovieManager.Thumbnail;
using OpenCvSharp;

namespace IndigoMovieManager
{
    /// <summary>
    /// 動画ファイルに直接突撃してメタ情報（FPS・尺・サイズ・ハッシュ等）をもぎ取ってくる「生のデータ」の入れ物だ！📦
    /// （※DBから読むんじゃなく、ローカルファイルから這いずり回って集めた新鮮な情報の器だぜ！）
    /// </summary>
    public class MovieInfo : MovieCore
    {
        private const double DefaultFps = 30;
        private static readonly object FfmpegLoadSync = new();
        private static bool ffmpegLoadAttempted;
        private static bool ffmpegLoadSucceeded;

        /// <summary>
        /// 旧実装リスペクト！既存コードが「Tag」名で呼んでるところへの救済エイリアス（別名）だ！🤝
        /// </summary>
        public string Tag => Tags;

        /// <summary>
        /// 誕生の瞬間！指定された動画ファイルを徹底解剖し、基本情報を自ら（MovieCore）の血肉に変えるぜ！🧬
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
            string videoCodec = "";
            bool readByFfMediaToolkit =
                EnsureFfMediaToolkitLoaded()
                && TryReadByFfMediaToolkit(
                    rawPath,
                    out fps,
                    out totalFrames,
                    out durationSec,
                    out videoCodec
                );
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

            VideoCodec = videoCodec;

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

        /// <summary>
        /// FFMediaToolkitのロード一発勝負！プロセス中で1回だけ試し、ダメなら潔く諦める武士の鑑だ！🏯
        /// </summary>
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
                        try
                        {
                            FFmpegLoader.LoadFFmpeg();
                        }
                        catch (InvalidOperationException)
                        {
                            // 他のスレッド等で既にロードされている場合は無視する
                        }
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

        /// <summary>
        /// ベンチマークで鍛え上げられた最強ロジック！「AvgFrameRate / NumberOfFrames / Duration」の黄金のトライアングルでメタ値をひねり出すぜ！📐
        /// </summary>
        private static bool TryReadByFfMediaToolkit(
            string inputPath,
            out double fps,
            out double totalFrames,
            out double durationSec,
            out string videoCodec
        )
        {
            fps = DefaultFps;
            totalFrames = 0;
            durationSec = 0;
            videoCodec = "";

            try
            {
                // 音声のみファイルでも Duration と stream有無を判定できるように AudioVideo で開く。
                MediaOptions options = new() { StreamsToLoad = MediaMode.AudioVideo };
                using var mediaFile = MediaFile.Open(inputPath, options);
                if (mediaFile == null)
                {
                    return false;
                }

                bool hasVideo = mediaFile.HasVideo && mediaFile.VideoStreams.Any();
                bool hasAudio = mediaFile.HasAudio && mediaFile.AudioStreams.Any();

                // Duration はコンテナ情報から取得できるので、映像ストリームの有無に関係なく先に確定する。
                durationSec = mediaFile.Info.Duration.TotalSeconds;

                // MovieInfo の FPS / TotalFrames は映像前提の値なので、映像がある時だけ埋める。
                if (hasVideo)
                {
                    var videoInfo = mediaFile.VideoStreams.First().Info;
                    fps = NormalizeFps(videoInfo.AvgFrameRate);
                    totalFrames =
                        videoInfo.NumberOfFrames
                        ?? Math.Truncate(NormalizeDurationSec(durationSec, totalFrames, fps) * fps);

                    videoCodec = videoInfo.CodecName ?? "";
                }
                else
                {
                    // 音声のみ等は映像メタを持たないため、FPS/Frames は既定値のまま保持する。
                    fps = DefaultFps;
                    totalFrames = 0;
                }

                durationSec = NormalizeDurationSec(durationSec, totalFrames, fps);
                if (!hasVideo && !hasAudio && !IsFinitePositive(durationSec))
                {
                    // stream情報もDurationも得られないケースだけ失敗扱いにして後段へフォールバックする。
                    return false;
                }

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

        /// <summary>
        /// FFMediaToolkitが倒れた時の頼れる切り札、OpenCVによる後方互換特攻ルート！🛡️
        /// </summary>
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

        /// <summary>
        /// 伝説の5つの秘宝（FFmpegの必須DLL群）が全て揃っているかを見極める審美眼！💎✨
        /// 一つでも欠けていたら容赦なく突き返す厳しいチェックだ！
        /// </summary>
        private static bool HasRequiredSharedDllSet(string dir)
        {
            return HasDll(dir, "avcodec*.dll")
                && HasDll(dir, "avformat*.dll")
                && HasDll(dir, "avutil*.dll")
                && HasDll(dir, "swscale*.dll")
                && HasDll(dir, "swresample*.dll");
        }

        /// <summary>
        /// 指定されたパターンのファイルがその地に眠っているかを探り当てるダウジングマシン！🪙
        /// </summary>
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

        /// <summary>
        /// 狂ったFPS値を叩き直し、健全で真っ当な数値へと更生させる生活指導員！👊
        /// </summary>
        private static double NormalizeFps(double fps)
        {
            return IsFinitePositive(fps) ? fps : DefaultFps;
        }

        /// <summary>
        /// 浮ついた小数点以下のフレーム数を容赦なく切り捨て、地に足のついた総フレーム数へと鍛え直す！🪓
        /// </summary>
        private static double NormalizeTotalFrames(double totalFrames)
        {
            return IsFinitePositive(totalFrames) ? Math.Truncate(totalFrames) : 0;
        }

        /// <summary>
        /// 真の再生時間(Duration)を導き出す最終アンサー！⏳
        /// コンテナ由来の時間がアテにならなければ、総フレーム数とFPSから執念で計算し直すサバイバル特化のメソッドだ！🔥
        /// </summary>
        private static double NormalizeDurationSec(
            double durationSec,
            double totalFrames,
            double fps
        )
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

        /// <summary>
        /// NaNやInfinityといった混沌(カオス)を退け、この世の理にかなった「正の有限値」だけを通す絶対の門番！🚪🛡️
        /// </summary>
        private static bool IsFinitePositive(double value)
        {
            return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
