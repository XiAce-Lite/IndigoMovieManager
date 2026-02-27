using System;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// サムネイル関連の環境変数を一元管理する静的クラス。
    /// </summary>
    internal static class ThumbnailEnvConfig
    {
        // --- 環境変数名 ---
        public const string GpuDecodeMode = "IMM_THUMB_GPU_DECODE";
        public const string ThumbEngine = "IMM_THUMB_ENGINE";
        public const string FfmpegExePath = "IMM_FFMPEG_EXE_PATH";
        public const string ThumbDecoder = "IMM_THUMB_DECODER";
        public const string ThumbFileLog = "IMM_THUMB_FILE_LOG";

        // --- 読み取りヘルパー ---
        public static string GetGpuDecodeMode() =>
            Environment.GetEnvironmentVariable(GpuDecodeMode)?.Trim() ?? "";

        public static string GetThumbEngine() =>
            Environment.GetEnvironmentVariable(ThumbEngine)?.Trim() ?? "";

        public static string GetFfmpegExePath() =>
            Environment.GetEnvironmentVariable(FfmpegExePath);

        public static string GetThumbDecoder() =>
            Environment.GetEnvironmentVariable(ThumbDecoder)?.Trim() ?? "";

        public static bool IsThumbFileLogEnabled()
        {
            string mode = Environment.GetEnvironmentVariable(ThumbFileLog);
            if (string.IsNullOrWhiteSpace(mode))
                return false;
            string n = mode.Trim().ToLowerInvariant();
            return n is "1" or "true" or "on" or "yes";
        }
    }
}
