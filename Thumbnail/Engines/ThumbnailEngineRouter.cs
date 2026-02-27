using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IndigoMovieManager.Thumbnail.Engines
{
    /// <summary>
    /// サムネイルエンジンの振り分けルール（2026/02/26版）。
    /// ThumbnailJobContext の属性に基づいて最適なエンジンを選択する。
    /// </summary>
    internal sealed class ThumbnailEngineRouter
    {
        private const long FiftMB = 50L * 1024 * 1024;

        private readonly IReadOnlyList<IThumbnailGenerationEngine> engines;

        public ThumbnailEngineRouter(IReadOnlyList<IThumbnailGenerationEngine> engines)
        {
            this.engines = engines ?? throw new ArgumentNullException(nameof(engines));
        }

        /// <summary>
        /// ブックマーク用は既定で ffmediatoolkit を返す。
        /// </summary>
        public IThumbnailGenerationEngine ResolveForBookmark()
        {
            return FindEngine("ffmediatoolkit") ?? engines[0];
        }

        /// <summary>
        /// 通常サムネイル生成時のエンジン選択。上から順に評価する。
        /// </summary>
        public IThumbnailGenerationEngine ResolveForThumbnail(ThumbnailJobContext context)
        {
            // 1. 強制設定
            IThumbnailGenerationEngine forced = TryResolveForcedEngine();
            if (forced != null)
                return forced;

            // 2. 手動更新 or 1パネル → ffmediatoolkit
            if (context.IsManual || context.PanelCount == 1)
            {
                return FindEngine("ffmediatoolkit") ?? engines[0];
            }

            string ext =
                System.IO.Path.GetExtension(context.MovieFullPath)?.ToLowerInvariant() ?? "";

            // 3. AV1 コーデック → ffmpeg1pass >> ffmediatoolkit
            if (
                !string.IsNullOrEmpty(context.VideoCodec)
                && context.VideoCodec.IndexOf("av1", StringComparison.OrdinalIgnoreCase) >= 0
            )
            {
                return TryResolveMulti("ffmpeg1pass", "ffmediatoolkit");
            }

            // 4. MOV → ffmpeg1pass
            if (ext is ".mov")
            {
                return FindEngine("ffmpeg1pass") ?? engines[0];
            }

            // 5. 絵文字パス → ffmediatoolkit > ffmpeg1pass（opencv 禁止）
            if (context.HasEmojiPath)
            {
                return TryResolveMulti("ffmediatoolkit", "ffmpeg1pass");
            }

            // 6. 50MB以下の .mp4 → opencv > ffmediatoolkit > ffmpeg1pass
            if (ext is ".mp4" && context.FileSizeBytes <= FiftMB)
            {
                return TryResolveMulti("opencv", "ffmediatoolkit", "ffmpeg1pass");
            }

            // 7. 50MB以下の .wmv → opencv > ffmediatoolkit >>> ffmpeg1pass
            if (ext is ".wmv" && context.FileSizeBytes <= FiftMB)
            {
                return TryResolveMulti("opencv", "ffmediatoolkit", "ffmpeg1pass");
            }

            // 8. 50MBより大きい .wmv → ffmediatoolkit >> opencv >>>> ffmpeg1pass
            if (ext is ".wmv" && context.FileSizeBytes > FiftMB)
            {
                return TryResolveMulti("ffmediatoolkit", "opencv", "ffmpeg1pass");
            }

            // 9. パネル10以上 → ffmpeg1pass >> ffmediatoolkit
            if (context.PanelCount >= 10)
            {
                return TryResolveMulti("ffmpeg1pass", "ffmediatoolkit");
            }

            // 10. デフォルト → ffmpeg1pass >> ffmediatoolkit >> opencv
            return TryResolveMulti("ffmpeg1pass", "ffmediatoolkit", "opencv");
        }

        /// <summary>
        /// パスに ANSI（shift_jis 等）にマッピングできない Unicode 文字が含まれるかを判定する。
        /// 絵文字やサロゲートペアなどが該当する。
        /// </summary>
        public static bool HasUnmappableAnsiChar(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            // Shift_JIS (codepage 932) でラウンドトリップできるか検査する。
            Encoding sjis;
            try
            {
                sjis = Encoding.GetEncoding(932);
            }
            catch
            {
                // CodePagesProvider 未登録時は判定不能なので false。
                return false;
            }

            byte[] bytes = sjis.GetBytes(path);
            string roundTripped = sjis.GetString(bytes);
            return !string.Equals(path, roundTripped, StringComparison.Ordinal);
        }

        // ── private helpers ──

        /// <summary>
        /// 環境変数 IMM_THUMB_ENGINE で強制指定されたエンジンを返す。未設定なら null。
        /// </summary>
        private IThumbnailGenerationEngine TryResolveForcedEngine()
        {
            string envValue = ThumbnailEnvConfig.GetThumbEngine();
            if (string.IsNullOrEmpty(envValue))
                return null;
            return FindEngine(envValue);
        }

        /// <summary>
        /// 優先順に列挙されたエンジンIDの中から最初に見つかったものを返す。
        /// </summary>
        private IThumbnailGenerationEngine TryResolveMulti(params string[] engineIds)
        {
            foreach (string id in engineIds)
            {
                IThumbnailGenerationEngine found = FindEngine(id);
                if (found != null)
                    return found;
            }
            return engines[0];
        }

        private IThumbnailGenerationEngine FindEngine(string engineId)
        {
            return engines.FirstOrDefault(e =>
                string.Equals(e.EngineId, engineId, StringComparison.OrdinalIgnoreCase)
            );
        }
    }
}
