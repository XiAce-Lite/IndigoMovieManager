namespace IndigoMovieManager.Watcher
{
    /// <summary>
    /// Everything 系の増分同期で使うカーソル判定を共通化する。
    /// </summary>
    internal static class FileIndexIncrementalSyncPolicy
    {
        // 同一更新時刻の項目を毎回拾わないよう、増分同期は strictly newer のみ通す。
        internal static bool ShouldIncludeItem(DateTime itemChangedUtc, DateTime? changedSinceUtc)
        {
            if (!changedSinceUtc.HasValue)
            {
                return true;
            }

            DateTime normalizedItemUtc = NormalizeToUtc(itemChangedUtc);
            DateTime baselineUtc = NormalizeToUtc(changedSinceUtc.Value);
            return normalizedItemUtc > baselineUtc;
        }

        // 保存済みカーソルより先へ進んだ時だけ high-water mark を更新する。
        internal static bool ShouldAdvanceCursor(
            DateTime? maxObservedChangedUtc,
            DateTime? changedSinceUtc
        )
        {
            if (!maxObservedChangedUtc.HasValue)
            {
                return false;
            }

            if (!changedSinceUtc.HasValue)
            {
                return true;
            }

            DateTime observedUtc = NormalizeToUtc(maxObservedChangedUtc.Value);
            DateTime baselineUtc = NormalizeToUtc(changedSinceUtc.Value);
            return observedUtc > baselineUtc;
        }

        internal static DateTime NormalizeToUtc(DateTime value)
        {
            return value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime(),
            };
        }
    }
}
