using System;
using System.IO;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.FailureDb;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private const int ExtensionDetailThumbnailTabIndex = 99;
        private const string ExtensionDetailPlaceholderFileName = "errorGrid.jpg";

        private void InitializeDetailThumbnailModeRuntime()
        {
            // 詳細サムネの表示モードは子プロセスへも引き継ぎたいので、まずプロセスへ反映する。
            ThumbnailDetailModeRuntime.ApplyToProcess(ReadConfiguredDetailThumbnailMode());
        }

        internal void ChangeExtensionDetailThumbnailMode(string mode)
        {
            string normalizedMode = ThumbnailDetailModeRuntime.Normalize(mode);
            if (
                !string.Equals(
                    ReadConfiguredDetailThumbnailMode(),
                    normalizedMode,
                    StringComparison.Ordinal
                )
            )
            {
                Properties.Settings.Default.DetailThumbnailMode = normalizedMode;
                Properties.Settings.Default.Save();
            }

            ThumbnailDetailModeRuntime.ApplyToProcess(normalizedMode);
            ExtensionTabViewHost?.ApplyConfiguredDetailThumbnailMode();

            if (!IsExtensionTabVisibleOrSelected())
            {
                MarkExtensionTabDirty();
                return;
            }

            MovieRecords record = GetSelectedItemByTabIndex();
            if (record == null)
            {
                return;
            }

            EnsureActiveExtensionDetailThumbnail(record);
            RefreshActiveExtensionDetailTab(record);
        }

        private string ReadConfiguredDetailThumbnailMode()
        {
            return ThumbnailDetailModeRuntime.Normalize(
                Properties.Settings.Default.DetailThumbnailMode
            );
        }

        private void PrepareExtensionDetailThumbnail(MovieRecords record, bool enqueueIfMissing)
        {
            if (record == null)
            {
                return;
            }

            string existingThumbnailPath = ResolveExistingExtensionDetailThumbnailPath(record);
            if (!string.IsNullOrWhiteSpace(existingThumbnailPath))
            {
                if (
                    !string.Equals(
                        record.ThumbDetail,
                        existingThumbnailPath,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    record.ThumbDetail = existingThumbnailPath;
                }

                return;
            }

            if (HasExtensionDetailErrorMarker(record))
            {
                string placeholderPath = GetExtensionDetailPlaceholderPath();
                if (
                    !string.Equals(
                        record.ThumbDetail,
                        placeholderPath,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    record.ThumbDetail = placeholderPath;
                }

                return;
            }

            if (HasOpenExtensionDetailRescueRequest(record))
            {
                string placeholderPath = GetExtensionDetailPlaceholderPath();
                if (
                    !string.Equals(
                        record.ThumbDetail,
                        placeholderPath,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    // 救済待ち/救済中は通常キューへ戻さず、詳細はエラーplaceholderのまま待機する。
                    record.ThumbDetail = placeholderPath;
                }

                return;
            }

            string expectedThumbnailPath = BuildExpectedExtensionDetailThumbnailPath(record);
            if (
                !string.IsNullOrWhiteSpace(expectedThumbnailPath)
                && !string.Equals(
                    record.ThumbDetail,
                    expectedThumbnailPath,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                // まだ未生成の時は、これから作る予定のパスを先に持たせておく。
                record.ThumbDetail = expectedThumbnailPath;
            }

            if (!enqueueIfMissing || !record.IsExists)
            {
                return;
            }

            TryEnqueueMissingExtensionDetailThumbnailManualCreate(record);
        }

        private void EnsureActiveExtensionDetailThumbnail(MovieRecords record)
        {
            if (record == null)
            {
                return;
            }

            // 詳細タブで今見せる画像が無ければ、その場で現在選択モードの作成要求まで進める。
            PrepareExtensionDetailThumbnail(record, enqueueIfMissing: true);
            TryAutoRescueExtensionDetailThumbnail(record);
        }

        internal void ReevaluateActiveExtensionDetailThumbnail()
        {
            if (!IsExtensionTabVisibleOrSelected())
            {
                return;
            }

            MovieRecords record = GetSelectedItemByTabIndex();
            if (record == null)
            {
                return;
            }

            EnsureActiveExtensionDetailThumbnail(record);
            RefreshActiveExtensionDetailTab(record);
        }

        private void TryAutoRescueExtensionDetailThumbnail(MovieRecords record)
        {
            if (record == null || !IsThumbnailErrorPlaceholderPath(record.ThumbDetail))
            {
                return;
            }

            if (HasOpenExtensionDetailRescueRequest(record))
            {
                return;
            }

            _ = TryEnqueueThumbnailDisplayErrorRescueJob(
                new QueueObj
                {
                    MovieId = record.Movie_Id,
                    MovieFullPath = record.Movie_Path,
                    Hash = record.Hash,
                    Tabindex = ExtensionDetailThumbnailTabIndex,
                    Priority = ThumbnailQueuePriority.Preferred,
                },
                reason: "detail-error-placeholder",
                requiresIdle: false
            );
        }

        private string ResolveExistingExtensionDetailThumbnailPath(MovieRecords record)
        {
            if (record == null)
            {
                return "";
            }

            string thumbnailPath = BuildCurrentThumbnailPath(
                ExtensionDetailThumbnailTabIndex,
                record.Movie_Path,
                record.Hash
            );
            if (Path.Exists(thumbnailPath))
            {
                return thumbnailPath;
            }

            if (!string.IsNullOrWhiteSpace(record.Movie_Body))
            {
                string legacyThumbnailPath = BuildCurrentThumbnailPath(
                    ExtensionDetailThumbnailTabIndex,
                    record.Movie_Body,
                    record.Hash
                );
                if (Path.Exists(legacyThumbnailPath))
                {
                    return legacyThumbnailPath;
                }
            }

            return "";
        }

        private bool HasExtensionDetailErrorMarker(MovieRecords record)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.Movie_Path))
            {
                return false;
            }

            string errorMarkerPath = ThumbnailPathResolver.BuildErrorMarkerPath(
                ResolveCurrentThumbnailOutPath(ExtensionDetailThumbnailTabIndex),
                record.Movie_Path
            );
            return Path.Exists(errorMarkerPath);
        }

        private bool HasOpenExtensionDetailRescueRequest(MovieRecords record)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.Movie_Path))
            {
                return false;
            }

            ThumbnailFailureDbService failureDbService = ResolveCurrentThumbnailFailureDbService();
            if (failureDbService == null)
            {
                return false;
            }

            string moviePathKey = ThumbnailFailureDbPathResolver.CreateMoviePathKey(
                record.Movie_Path
            );
            return failureDbService.HasOpenRescueRequest(
                moviePathKey,
                ExtensionDetailThumbnailTabIndex
            );
        }

        private string BuildExpectedExtensionDetailThumbnailPath(MovieRecords record)
        {
            if (record == null)
            {
                return "";
            }

            return BuildCurrentThumbnailPath(
                ExtensionDetailThumbnailTabIndex,
                record.Movie_Path,
                record.Hash
            );
        }

        private static string GetExtensionDetailPlaceholderPath()
        {
            return Path.Combine(
                Directory.GetCurrentDirectory(),
                "Images",
                ExtensionDetailPlaceholderFileName
            );
        }

        private void TryEnqueueMissingExtensionDetailThumbnailManualCreate(MovieRecords record)
        {
            if (record == null || !record.IsExists)
            {
                return;
            }

            // ここで必要なのは「既存サムネ差し替え用 manual」ではなく、
            // 明示要求として通常生成を優先投入する経路。
            // tab=99 は上側タブ gate を受けないので、そのまま preferred で即投入する。
            _ = TryEnqueueThumbnailJob(
                new QueueObj
                {
                    MovieId = record.Movie_Id,
                    MovieFullPath = record.Movie_Path,
                    Hash = record.Hash,
                    Tabindex = ExtensionDetailThumbnailTabIndex,
                    Priority = ThumbnailQueuePriority.Preferred,
                },
                bypassDebounce: true
            );
        }
    }
}
