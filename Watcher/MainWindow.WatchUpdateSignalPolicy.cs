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
    }
}
