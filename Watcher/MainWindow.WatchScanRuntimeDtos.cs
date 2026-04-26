using System;
using System.Collections.Generic;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        /// <summary>
        /// フォルダ走査結果の情報をひとまとめにして返すための軽量DTO。
        /// </summary>
        private sealed class FolderScanWithStrategyResult
        {
            public FolderScanWithStrategyResult(
                FolderScanResult scanResult,
                string strategy,
                string detail,
                bool hasIncrementalCursor
            )
            {
                ScanResult = scanResult;
                Strategy = strategy;
                Detail = detail;
                HasIncrementalCursor = hasIncrementalCursor;
            }

            public FolderScanResult ScanResult { get; }
            public string Strategy { get; }
            public string Detail { get; }
            public bool HasIncrementalCursor { get; }
        }

        // deferred state を先読みする時は、可変Queueの実体を外へ漏らさず値で扱う。
        private readonly record struct DeferredWatchScanStateSnapshot(
            List<string> PendingPaths,
            DateTime? DeferredCursorUtc
        );

        // 1回で処理しきれない watch 候補は、フォルダ単位で次回以降へ持ち越す。
        private sealed class DeferredWatchScanState
        {
            public DeferredWatchScanState(IEnumerable<string> pendingPaths, DateTime? deferredCursorUtc)
            {
                PendingPaths = new Queue<string>(pendingPaths ?? []);
                DeferredCursorUtc = deferredCursorUtc;
            }

            public Queue<string> PendingPaths { get; }
            public DateTime? DeferredCursorUtc { get; }
        }

        /// <summary>
        /// フォルダ走査結果の情報をひとまとめにして返すための軽量DTO。
        /// </summary>
        private sealed class FolderScanResult
        {
            public FolderScanResult(int scannedCount, List<string> newMoviePaths)
            {
                ScannedCount = scannedCount;
                NewMoviePaths = newMoviePaths;
            }

            public int ScannedCount { get; }
            public List<string> NewMoviePaths { get; }
        }
    }
}
