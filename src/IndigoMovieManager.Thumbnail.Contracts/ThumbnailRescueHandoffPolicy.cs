using System.IO;
using IndigoMovieManager.Thumbnail.FailureDb;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// Queue 失敗から rescue handoff へ渡す材料をここへ寄せる。
    /// ResolveLaneName だけは UI からも参照されるため公開面を維持する。
    /// </summary>
    public static class ThumbnailRescueHandoffPolicy
    {
        private static readonly string[] BrokenMp4CodecNgExtensions = [".mp4", ".mov", ".m4v"];

        public static string ResolveLaneName(bool isSlowLane)
        {
            return isSlowLane ? "slow" : "normal";
        }

        public static string NormalizeMainLaneName(string laneName)
        {
            return string.Equals(laneName, "slow", StringComparison.OrdinalIgnoreCase)
                ? "slow"
                : "normal";
        }

        // timeout handoff と通常 failure handoff を同じ規則で見分ける。
        public static string ResolveHandoffType(Exception ex, string failureReasonOverride = "")
        {
            if (ex is TimeoutException)
            {
                return "timeout";
            }
            if (ex is OperationCanceledException)
            {
                return "canceled";
            }

            string normalized = NormalizeFailureReason(ex, failureReasonOverride);
            if (
                normalized.Contains("thumbnail normal lane timeout")
                || normalized.Contains("engine attempt timeout")
            )
            {
                return "timeout";
            }

            return "failure";
        }

        // rescue worker 側の再分類と、main 側の親行分類を同じ規則へ揃える。
        public static ThumbnailFailureKind ResolveFailureKind(
            Exception ex,
            string moviePath,
            string failureReasonOverride = ""
        )
        {
            if (ex is TimeoutException)
            {
                return ThumbnailFailureKind.HangSuspected;
            }

            if (ex is FileNotFoundException)
            {
                return ThumbnailFailureKind.FileMissing;
            }

            if (!string.IsNullOrWhiteSpace(moviePath))
            {
                try
                {
                    if (File.Exists(moviePath))
                    {
                        if (new FileInfo(moviePath).Length <= 0)
                        {
                            return ThumbnailFailureKind.ZeroByteFile;
                        }
                    }
                    else
                    {
                        return ThumbnailFailureKind.FileMissing;
                    }
                }
                catch
                {
                    // ファイル状態が読めない時は文言判定へフォールバックする。
                }
            }

            string normalized = NormalizeFailureReason(ex, failureReasonOverride);
            if (
                normalized.Contains("thumbnail normal lane timeout")
                || normalized.Contains("engine attempt timeout")
            )
            {
                return ThumbnailFailureKind.HangSuspected;
            }
            if (normalized.Contains("drm"))
            {
                return ThumbnailFailureKind.DrmProtected;
            }
            if (
                normalized.Contains("unsupported codec")
                || ShouldTreatAsBrokenMp4CodecNg(moviePath, normalized)
            )
            {
                return ThumbnailFailureKind.UnsupportedCodec;
            }
            if (
                normalized.Contains("moov atom not found")
                || normalized.Contains("invalid data found")
                || normalized.Contains("find stream info failed")
                || normalized.Contains("stream info failed")
                || normalized.Contains("avformat_open_input failed")
                || normalized.Contains("avformat_find_stream_info failed")
                || normalized.Contains("frame decode failed")
                || normalized.Contains("partial file")
                || normalized.Contains("broken index")
            )
            {
                return ThumbnailFailureKind.IndexCorruption;
            }
            if (
                normalized.Contains("video stream is missing")
                || normalized.Contains("no video stream")
                || normalized.Contains("video stream not found")
            )
            {
                return ThumbnailFailureKind.NoVideoStream;
            }
            if (
                normalized.Contains("no frames decoded")
                || normalized.Contains("ffmpeg one-pass failed")
            )
            {
                return ThumbnailFailureKind.TransientDecodeFailure;
            }
            if (
                normalized.Contains("being used by another process")
                || normalized.Contains("file is locked")
                || normalized.Contains("locked")
            )
            {
                return ThumbnailFailureKind.FileLocked;
            }

            return ThumbnailFailureKind.Unknown;
        }

        // MP4 系で ffprobe/ffmpeg がヘッダ崩壊を返した個体は、通常救済へ回しても戻りにくい。
        // ここでは heavy な全体走査はせず、拡張子と致命文言で CODEC NG 固定へ倒す。
        internal static bool ShouldTreatAsBrokenMp4CodecNg(
            string moviePath,
            string normalizedFailureReason
        )
        {
            string extension = Path.GetExtension(moviePath ?? "");
            if (
                !BrokenMp4CodecNgExtensions.Contains(
                    extension,
                    StringComparer.OrdinalIgnoreCase
                )
            )
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(normalizedFailureReason))
            {
                return false;
            }

            return normalizedFailureReason.Contains("moov atom not found")
                || normalizedFailureReason.Contains("invalid data found when processing input")
                || normalizedFailureReason.Contains("error reading header")
                || normalizedFailureReason.Contains("end of file")
                || normalizedFailureReason.Contains("trun track id unknown")
                || normalizedFailureReason.Contains("could not find corresponding trex");
        }

        private static string NormalizeFailureReason(Exception ex, string failureReasonOverride)
        {
            return string.IsNullOrWhiteSpace(failureReasonOverride)
                ? (ex?.Message ?? "").Trim().ToLowerInvariant()
                : failureReasonOverride.Trim().ToLowerInvariant();
        }
    }
}
