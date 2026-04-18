using System.Collections.Generic;
using System.IO;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.FailureDb;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        internal enum MissingThumbnailAutoEnqueueBlockReason
        {
            None = 0,
            ErrorMarkerExists = 1,
            OpenRescueRequestExists = 2,
        }

        internal enum MissingThumbnailRescueGuardAction
        {
            Continue = 0,
            DeferByUiSuppression = 1,
            DropStaleScope = 2,
        }

        // 自動監視中は通常キューを優先し、手動実行時は欠損救済を優先する。
        internal static bool ShouldSkipMissingThumbnailRescueForBusyQueue(
            bool isManualRequest,
            int activeCount,
            int busyThreshold
        )
        {
            return !isManualRequest && activeCount >= busyThreshold;
        }

        // Watch由来の欠損救済は通常キュー完走を優先し、アイドル時だけ許可する。
        internal static int ResolveMissingThumbnailRescueBusyThreshold(
            bool isWatchRequest,
            int defaultBusyThreshold
        )
        {
            return isWatchRequest ? 1 : Math.Max(1, defaultBusyThreshold);
        }

        // watch起点の通常サムネ自動投入は、実サムネを持つ上側タブ(0..4)だけへ限定する。
        internal static int? ResolveWatchMissingThumbnailTabIndex(int currentTabIndex)
        {
            return IsUpperThumbnailTabIndex(currentTabIndex) ? currentTabIndex : null;
        }

        // 現在のMainDBとタブを1つのスコープキーへ正規化する。
        private static string BuildMissingThumbnailRescueScopeKey(string dbFullPath, int tabIndex)
        {
            string normalized = dbFullPath ?? "";
            try
            {
                if (!string.IsNullOrWhiteSpace(normalized) && Path.IsPathFullyQualified(normalized))
                {
                    normalized = Path.GetFullPath(normalized);
                }
            }
            catch
            {
                // 正規化に失敗しても、元文字列をキーとして扱って処理継続する。
            }

            return $"{normalized.Trim().ToLowerInvariant()}|tab={tabIndex}";
        }

        internal static MissingThumbnailRescueGuardAction ResolveMissingThumbnailRescueGuardAction(
            bool isWatchMode,
            bool isWatchSuppressedByUi,
            bool isCurrentWatchScope
        )
        {
            if (!isWatchMode)
            {
                return MissingThumbnailRescueGuardAction.Continue;
            }

            if (!isCurrentWatchScope)
            {
                return MissingThumbnailRescueGuardAction.DropStaleScope;
            }

            return ShouldSuppressWatchWorkByUi(isWatchSuppressedByUi, true)
                ? MissingThumbnailRescueGuardAction.DeferByUiSuppression
                : MissingThumbnailRescueGuardAction.Continue;
        }

        // 欠損サムネの自動再投入は、失敗マーカーか救済待ちが残っている間は止める。
        internal static MissingThumbnailAutoEnqueueBlockReason ResolveMissingThumbnailAutoEnqueueBlockReason(
            string movieFullPath,
            int tabIndex,
            HashSet<string> existingThumbnailFileNames,
            HashSet<string> openRescueRequestKeys
        )
        {
            if (string.IsNullOrWhiteSpace(movieFullPath))
            {
                return MissingThumbnailAutoEnqueueBlockReason.None;
            }

            string errorMarkerFileName = ThumbnailPathResolver.BuildErrorMarkerFileName(
                movieFullPath
            );
            if (
                existingThumbnailFileNames != null
                && !string.IsNullOrWhiteSpace(errorMarkerFileName)
                && existingThumbnailFileNames.Contains(errorMarkerFileName)
            )
            {
                return MissingThumbnailAutoEnqueueBlockReason.ErrorMarkerExists;
            }

            if (openRescueRequestKeys == null || openRescueRequestKeys.Count < 1)
            {
                return MissingThumbnailAutoEnqueueBlockReason.None;
            }

            string rescueRequestKey = BuildMissingThumbnailRescueBlockKey(movieFullPath, tabIndex);
            if (
                !string.IsNullOrWhiteSpace(rescueRequestKey)
                && openRescueRequestKeys.Contains(rescueRequestKey)
            )
            {
                return MissingThumbnailAutoEnqueueBlockReason.OpenRescueRequestExists;
            }

            return MissingThumbnailAutoEnqueueBlockReason.None;
        }

        // Watcher側でも moviePathKey + tab で揃え、FailureDb の open rescue 集合と突き合わせる。
        private static string BuildMissingThumbnailRescueBlockKey(string movieFullPath, int tabIndex)
        {
            string moviePathKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(movieFullPath);
            if (string.IsNullOrWhiteSpace(moviePathKey))
            {
                return "";
            }

            return $"{moviePathKey}|{tabIndex}";
        }

        private static string DescribeMissingThumbnailAutoEnqueueBlockReason(
            MissingThumbnailAutoEnqueueBlockReason reason
        )
        {
            return reason switch
            {
                MissingThumbnailAutoEnqueueBlockReason.ErrorMarkerExists => "error-marker",
                MissingThumbnailAutoEnqueueBlockReason.OpenRescueRequestExists =>
                    "failuredb-open-rescue",
                _ => "",
            };
        }

        private MissingThumbnailRescueGuardAction GetMissingThumbnailRescueGuardAction(
            bool isWatchMode,
            string snapshotDbFullPath,
            long requestScopeStamp
        )
        {
            bool isCurrentWatchScope = !isWatchMode
                || IsCurrentWatchScanScope(snapshotDbFullPath, requestScopeStamp);
            return ResolveMissingThumbnailRescueGuardAction(
                isWatchMode,
                isWatchMode && IsWatchSuppressedByUi(),
                isCurrentWatchScope
            );
        }
    }
}
