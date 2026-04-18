using System.Collections.Generic;
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
