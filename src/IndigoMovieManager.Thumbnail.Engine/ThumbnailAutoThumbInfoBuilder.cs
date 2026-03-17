namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 自動サムネのパネル秒決定と ThumbInfo 組み立てを切り離す。
    /// </summary>
    internal static class ThumbnailAutoThumbInfoBuilder
    {
        public static int ResolveSafeMaxCaptureSec(double durationSec)
        {
            if (durationSec <= 0 || double.IsNaN(durationSec) || double.IsInfinity(durationSec))
            {
                return 0;
            }

            // 端数や丸め誤差で末尾超えしないよう、わずかに手前へ寄せる。
            double safeEnd = Math.Max(0, durationSec - 0.001);
            return Math.Max(0, (int)Math.Floor(safeEnd));
        }

        public static ThumbInfo Build(ThumbnailLayoutProfile layoutProfile, double? durationSec)
        {
            int columns = Math.Max(1, layoutProfile?.Columns ?? 1);
            int rows = Math.Max(1, layoutProfile?.Rows ?? 1);
            int thumbCount = columns * rows;
            int divideSec = 1;
            int maxCaptureSec = int.MaxValue;
            if (durationSec.HasValue && durationSec.Value > 0)
            {
                divideSec = (int)(durationSec.Value / (thumbCount + 1));
                if (divideSec < 1)
                {
                    divideSec = 1;
                }

                // 短尺動画でも末尾超えしないよう、安全上限で丸める。
                maxCaptureSec = ResolveSafeMaxCaptureSec(durationSec.Value);
            }

            ThumbnailSheetSpec spec = new()
            {
                ThumbWidth = Math.Max(1, layoutProfile?.Width ?? 120),
                ThumbHeight = Math.Max(1, layoutProfile?.Height ?? 90),
                ThumbRows = rows,
                ThumbColumns = columns,
                ThumbCount = thumbCount,
            };

            for (int i = 1; i < thumbCount + 1; i++)
            {
                int sec = i * divideSec;
                if (sec > maxCaptureSec)
                {
                    sec = maxCaptureSec;
                }

                spec.CaptureSeconds.Add(sec);
            }

            return ThumbInfo.FromSheetSpec(spec);
        }
    }
}
