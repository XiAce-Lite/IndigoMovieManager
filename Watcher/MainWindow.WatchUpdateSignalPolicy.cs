using System;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // watch 1周分の更新量を、poll 間隔判断用の単一値へ潰す。
        private static int ComputeWatchUpdateCountForPoll(
            bool hasFolderUpdate,
            int enqueuedCount,
            int changedMovieCount
        )
        {
            int updateCount = Math.Max(enqueuedCount, changedMovieCount);
            if (hasFolderUpdate && updateCount < 1)
            {
                return 1;
            }

            return updateCount;
        }

        // poll 間隔へ渡す更新量は watch モード時だけ算出し、呼び出し側の分岐を薄くする。
        private static bool TryResolveWatchUpdateCountForPoll(
            CheckMode mode,
            bool hasFolderUpdate,
            int enqueuedCount,
            int changedMovieCount,
            out int updateCount
        )
        {
            if (mode != CheckMode.Watch)
            {
                updateCount = 0;
                return false;
            }

            updateCount = ComputeWatchUpdateCountForPoll(
                hasFolderUpdate,
                enqueuedCount,
                changedMovieCount
            );
            return true;
        }
    }
}
