using System;
using System.Collections.Generic;
using System.IO;
using IndigoMovieManager.Converter;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.FailureDb;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private const int ExtensionDetailThumbnailTabIndex = 99;
        private const string ExtensionDetailPlaceholderFileName = "errorGrid.jpg";
        private static readonly string[] DetailLayoutFolderNames = [
            ThumbnailLayoutProfileResolver.DetailStandard.FolderName,
            ThumbnailLayoutProfileResolver.DetailWhiteBrowser.FolderName,
            ThumbnailLayoutProfileResolver.Small.FolderName,
            ThumbnailLayoutProfileResolver.Big.FolderName,
            ThumbnailLayoutProfileResolver.List.FolderName,
            ThumbnailLayoutProfileResolver.Big10.FolderName,
        ];

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
            EnsureMissingDetailThumbnailCreation(record);
            TryAutoRescueExtensionDetailThumbnail(record);
        }

        private void EnsureMissingDetailThumbnailCreation(MovieRecords record)
        {
            if (record == null || !record.IsExists)
            {
                return;
            }

            if (IsThumbnailErrorPlaceholderPath(record.ThumbDetail))
            {
                return;
            }

            if (HasOpenExtensionDetailRescueRequest(record))
            {
                return;
            }

            string expectedThumbnailPath = BuildExpectedExtensionDetailThumbnailPath(record);
            if (string.IsNullOrWhiteSpace(expectedThumbnailPath))
            {
                return;
            }

            if (Path.Exists(expectedThumbnailPath))
            {
                return;
            }

            // 既存の再試行待ちとは重複しても落ちないように、明示的な最優先要求として再投入。
            TryEnqueueMissingExtensionDetailThumbnailManualCreate(record);
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

            foreach (string outPath in EnumerateExtensionDetailCandidateOutPaths())
            {
                string thumbnailPath = ResolveExistingExtensionDetailThumbnailPathByOutPath(
                    outPath,
                    record
                );
                if (!string.IsNullOrWhiteSpace(thumbnailPath))
                {
                    return thumbnailPath;
                }
            }

            return "";
        }

        private IEnumerable<string> EnumerateExtensionDetailCandidateOutPaths()
        {
            List<string> candidates = [];
            HashSet<string> unique = new(StringComparer.OrdinalIgnoreCase);
            string currentOutPath = ResolveCurrentThumbnailOutPath(ExtensionDetailThumbnailTabIndex);
            AddCandidatePath(candidates, unique, currentOutPath);

            string configuredThumbRoot =
                ResolveCurrentThumbnailRoot();
            if (!string.IsNullOrWhiteSpace(configuredThumbRoot))
            {
                AddCandidatePath(candidates, unique, configuredThumbRoot);
            }

            string layoutRootCandidate = ResolveCurrentCompatibleLayoutRoot(configuredThumbRoot);
            if (!string.IsNullOrWhiteSpace(layoutRootCandidate))
            {
                foreach (string profileFolderName in DetailLayoutFolderNames)
                {
                    AddCandidatePath(
                        candidates,
                        unique,
                        Path.Combine(layoutRootCandidate, profileFolderName)
                    );
                }
            }

            return candidates;
        }

        private static string ResolveCurrentCompatibleLayoutRoot(string thumbRoot)
        {
            if (string.IsNullOrWhiteSpace(thumbRoot))
            {
                return "";
            }

            string normalizedThumbRoot = thumbRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            );
            if (string.IsNullOrWhiteSpace(normalizedThumbRoot))
            {
                return "";
            }

            string folderName = Path.GetFileName(normalizedThumbRoot);
            bool isLayoutFolder = !string.IsNullOrWhiteSpace(folderName)
                && KnownThumbnailLayoutFolderNames.Contains(folderName);
            if (!isLayoutFolder)
            {
                return "";
            }

            return Path.GetDirectoryName(normalizedThumbRoot) ?? "";
        }

        private static void AddCandidatePath(
            List<string> candidates,
            HashSet<string> unique,
            string value
        )
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (!unique.Add(value))
            {
                return;
            }

            if (Directory.Exists(value))
            {
                candidates.Add(value);
                return;
            }
        }

        private string ResolveExistingExtensionDetailThumbnailPathByOutPath(
            string outPath,
            MovieRecords record
        )
        {
            if (record == null || string.IsNullOrWhiteSpace(outPath))
            {
                return "";
            }

            string thumbnailPath = ThumbnailPathResolver.BuildThumbnailPath(
                outPath,
                record.Movie_Path,
                record.Hash
            );
            if (Path.Exists(thumbnailPath))
            {
                return thumbnailPath;
            }

            if (!string.IsNullOrWhiteSpace(record.Movie_Body))
            {
                string legacyThumbnailPath = ThumbnailPathResolver.BuildThumbnailPath(
                    outPath,
                    record.Movie_Body,
                    record.Hash
                );
                if (Path.Exists(legacyThumbnailPath))
                {
                    return legacyThumbnailPath;
                }
            }

            if (
                ThumbnailPathResolver.TryFindExistingSuccessThumbnailPath(
                    outPath,
                    record.Movie_Path,
                    out string existingByMoviePath
                )
            )
            {
                return existingByMoviePath;
            }

            if (
                !string.IsNullOrWhiteSpace(record.Movie_Body)
                && ThumbnailPathResolver.TryFindExistingSuccessThumbnailPath(
                    outPath,
                    record.Movie_Body,
                    out string existingByMovieBody
                )
            )
            {
                return existingByMovieBody;
            }

            if (
                TryFindExistingSuccessThumbnailPathByBodyScan(
                    outPath,
                    record.Movie_Path,
                    out string scannedByMoviePath
                )
            )
            {
                return scannedByMoviePath;
            }

            if (
                !string.IsNullOrWhiteSpace(record.Movie_Body)
                && TryFindExistingSuccessThumbnailPathByBodyScan(
                    outPath,
                    record.Movie_Body,
                    out string scannedByMovieBody
                )
            )
            {
                return scannedByMovieBody;
            }

            return "";
        }

        private static bool TryFindExistingSuccessThumbnailPathByBodyScan(
            string outPath,
            string movieNameOrPath,
            out string matchedPath
        )
        {
            matchedPath = "";
            if (string.IsNullOrWhiteSpace(outPath) || string.IsNullOrWhiteSpace(movieNameOrPath))
            {
                return false;
            }

            if (!Directory.Exists(outPath))
            {
                return false;
            }

            string targetBody = Path.GetFileNameWithoutExtension(movieNameOrPath) ?? "";
            if (string.IsNullOrWhiteSpace(targetBody))
            {
                return false;
            }

            try
            {
                DateTime newestWriteTimeUtc = DateTime.MinValue;
                long newestLength = -1;
                string newestPath = "";

                foreach (string thumbnailPath in Directory.EnumerateFiles(outPath, "*.jpg"))
                {
                    string fileNameWithoutExt = Path.GetFileNameWithoutExtension(thumbnailPath) ?? "";
                    if (string.IsNullOrWhiteSpace(fileNameWithoutExt))
                    {
                        continue;
                    }

                    int separatorIndex = fileNameWithoutExt.LastIndexOf(".#", StringComparison.Ordinal);
                    if (separatorIndex <= 0)
                    {
                        continue;
                    }

                    string body = fileNameWithoutExt[..separatorIndex];
                    if (!string.Equals(body, targetBody, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (ThumbnailPathResolver.IsErrorMarker(thumbnailPath))
                    {
                        continue;
                    }

                    FileInfo fileInfo = new(thumbnailPath);
                    if (!fileInfo.Exists || fileInfo.Length <= 0)
                    {
                        continue;
                    }

                    if (
                        newestPath.Length == 0
                        || fileInfo.LastWriteTimeUtc > newestWriteTimeUtc
                        || (fileInfo.LastWriteTimeUtc == newestWriteTimeUtc && fileInfo.Length > newestLength)
                    )
                    {
                        newestWriteTimeUtc = fileInfo.LastWriteTimeUtc;
                        newestLength = fileInfo.Length;
                        newestPath = thumbnailPath;
                    }
                }

                if (string.IsNullOrWhiteSpace(newestPath))
                {
                    return false;
                }

                matchedPath = newestPath;
                return true;
            }
            catch
            {
                return false;
            }
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

            string expectedThumbnailPath = BuildExpectedExtensionDetailThumbnailPath(record);
            NoLockImageConverter.InvalidateFilePath(expectedThumbnailPath);

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
