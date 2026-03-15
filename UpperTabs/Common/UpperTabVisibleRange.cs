namespace IndigoMovieManager.UpperTabs.Common
{
    /// <summary>
    /// 上側タブの visible 範囲と、その前後の先読み範囲を保持する。
    /// </summary>
    public readonly record struct UpperTabVisibleRange(
        int FirstVisibleIndex,
        int LastVisibleIndex,
        int FirstNearVisibleIndex,
        int LastNearVisibleIndex
    )
    {
        public static UpperTabVisibleRange Empty => new(-1, -1, -1, -1);

        public bool HasVisibleItems => FirstVisibleIndex >= 0 && LastVisibleIndex >= FirstVisibleIndex;

        public static UpperTabVisibleRange Create(
            int firstVisibleIndex,
            int lastVisibleIndex,
            int totalCount,
            int overscanItemCount
        )
        {
            if (totalCount < 1 || firstVisibleIndex < 0 || lastVisibleIndex < firstVisibleIndex)
            {
                return Empty;
            }

            int safeLastIndex = Math.Min(totalCount - 1, lastVisibleIndex);
            int safeOverscan = Math.Max(0, overscanItemCount);
            int firstNearVisibleIndex = Math.Max(0, firstVisibleIndex - safeOverscan);
            int lastNearVisibleIndex = Math.Min(totalCount - 1, safeLastIndex + safeOverscan);

            return new UpperTabVisibleRange(
                firstVisibleIndex,
                safeLastIndex,
                firstNearVisibleIndex,
                lastNearVisibleIndex
            );
        }
    }
}
