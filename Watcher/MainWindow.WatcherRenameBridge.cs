using System.IO;
using System.Linq;
using System.Text;
using IndigoMovieManager.Data;
using IndigoMovieManager.Thumbnail;
using static IndigoMovieManager.DB.SQLite;

namespace IndigoMovieManager
{
    internal static class ThumbnailRenameAssetTransferHelper
    {
        internal readonly record struct ThumbnailRenameOperation(
            string SourcePath,
            string DestinationPath
        );

        // 表示中パスを軸にしつつ、legacy hash jpg も所有判定できる物だけ補完する。
        internal static List<ThumbnailRenameOperation> BuildRenameOperations(
            MovieRecords movie,
            string thumbnailRoot,
            string oldFullPath,
            string newFullPath,
            bool canRenameHashedThumbnailAssets,
            bool canRenameErrorMarkerAssets
        )
        {
            List<ThumbnailRenameOperation> operations = [];
            if (
                movie == null
                || string.IsNullOrWhiteSpace(thumbnailRoot)
                || !Directory.Exists(thumbnailRoot)
            )
            {
                return operations;
            }

            foreach (
                string sourcePath in EnumerateThumbnailSourcePaths(
                    movie,
                    thumbnailRoot,
                    oldFullPath,
                    canRenameHashedThumbnailAssets,
                    canRenameErrorMarkerAssets
                )
            )
            {
                string destinationPath = TryBuildRenamedThumbnailPath(
                    sourcePath,
                    oldFullPath,
                    newFullPath
                );
                if (
                    string.IsNullOrWhiteSpace(destinationPath)
                    || string.Equals(
                        sourcePath,
                        destinationPath,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    continue;
                }

                operations.Add(new ThumbnailRenameOperation(sourcePath, destinationPath));
            }

            return operations;
        }

        internal static string TryBuildRenamedThumbnailPath(
            string sourcePath,
            string oldFullPath,
            string newFullPath
        )
        {
            if (
                string.IsNullOrWhiteSpace(sourcePath)
                || string.IsNullOrWhiteSpace(oldFullPath)
                || string.IsNullOrWhiteSpace(newFullPath)
            )
            {
                return "";
            }

            string oldBody = Path.GetFileNameWithoutExtension(oldFullPath) ?? "";
            string newBody = Path.GetFileNameWithoutExtension(newFullPath) ?? "";
            if (string.IsNullOrWhiteSpace(oldBody) || string.IsNullOrWhiteSpace(newBody))
            {
                return "";
            }

            string directoryPath = Path.GetDirectoryName(sourcePath) ?? "";
            string extension = Path.GetExtension(sourcePath) ?? "";
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(sourcePath) ?? "";
            if (string.IsNullOrWhiteSpace(directoryPath) || string.IsNullOrWhiteSpace(extension))
            {
                return "";
            }

            string renamedFileNameWithoutExtension;
            if (string.Equals(fileNameWithoutExtension, oldBody, StringComparison.OrdinalIgnoreCase))
            {
                renamedFileNameWithoutExtension = newBody;
            }
            else if (
                fileNameWithoutExtension.StartsWith(
                    oldBody + ".#",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                renamedFileNameWithoutExtension =
                    newBody + fileNameWithoutExtension[oldBody.Length..];
            }
            else
            {
                return "";
            }

            return Path.Combine(directoryPath, renamedFileNameWithoutExtension + extension);
        }

        // まず表示中のパスを優先し、その後で exact 名と legacy hash jpg を安全側で補完する。
        private static IEnumerable<string> EnumerateThumbnailSourcePaths(
            MovieRecords movie,
            string thumbnailRoot,
            string oldFullPath,
            bool canRenameHashedThumbnailAssets,
            bool canRenameErrorMarkerAssets
        )
        {
            HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
            string oldBody = Path.GetFileNameWithoutExtension(oldFullPath) ?? "";

            TryAddThumbnailPath(
                paths,
                movie.ThumbPathSmall,
                thumbnailRoot,
                oldBody,
                movie.Hash ?? "",
                canRenameHashedThumbnailAssets,
                canRenameErrorMarkerAssets
            );
            TryAddThumbnailPath(
                paths,
                movie.ThumbPathBig,
                thumbnailRoot,
                oldBody,
                movie.Hash ?? "",
                canRenameHashedThumbnailAssets,
                canRenameErrorMarkerAssets
            );
            TryAddThumbnailPath(
                paths,
                movie.ThumbPathGrid,
                thumbnailRoot,
                oldBody,
                movie.Hash ?? "",
                canRenameHashedThumbnailAssets,
                canRenameErrorMarkerAssets
            );
            TryAddThumbnailPath(
                paths,
                movie.ThumbPathList,
                thumbnailRoot,
                oldBody,
                movie.Hash ?? "",
                canRenameHashedThumbnailAssets,
                canRenameErrorMarkerAssets
            );
            TryAddThumbnailPath(
                paths,
                movie.ThumbPathBig10,
                thumbnailRoot,
                oldBody,
                movie.Hash ?? "",
                canRenameHashedThumbnailAssets,
                canRenameErrorMarkerAssets
            );
            TryAddThumbnailPath(
                paths,
                movie.ThumbDetail,
                thumbnailRoot,
                oldBody,
                movie.Hash ?? "",
                canRenameHashedThumbnailAssets,
                canRenameErrorMarkerAssets
            );

            if (string.IsNullOrWhiteSpace(oldBody))
            {
                return paths;
            }

            DirectoryInfo thumbnailRootDirectory = new(thumbnailRoot);
            EnumerationOptions enumerationOptions = new() { RecurseSubdirectories = true };
            foreach (
                string exactFileName in EnumerateExpectedThumbnailFileNames(
                    oldFullPath,
                    movie.Hash ?? "",
                    canRenameHashedThumbnailAssets,
                    canRenameErrorMarkerAssets
                )
            )
            {
                foreach (
                    FileInfo thumbnailFile in thumbnailRootDirectory.EnumerateFiles(
                        exactFileName,
                        enumerationOptions
                    )
                )
                {
                    paths.Add(thumbnailFile.FullName);
                }
            }

            foreach (
                FileInfo thumbnailFile in EnumerateLegacyOwnedThumbnailFiles(
                    thumbnailRootDirectory,
                    enumerationOptions,
                    oldBody,
                    movie.Hash ?? "",
                    canRenameHashedThumbnailAssets
                )
            )
            {
                paths.Add(thumbnailFile.FullName);
            }

            return paths;
        }

        // 旧互換の *{body}.#{hash}*.jpg は、body/hash が一致し suffix 境界も確認できた物だけ拾う。
        private static IEnumerable<FileInfo> EnumerateLegacyOwnedThumbnailFiles(
            DirectoryInfo thumbnailRootDirectory,
            EnumerationOptions enumerationOptions,
            string oldBody,
            string hash,
            bool canRenameHashedThumbnailAssets
        )
        {
            if (
                !canRenameHashedThumbnailAssets
                || thumbnailRootDirectory == null
                || string.IsNullOrWhiteSpace(oldBody)
                || string.IsNullOrWhiteSpace(hash)
            )
            {
                yield break;
            }

            string searchPattern = $"{oldBody}.#{hash}*.jpg";
            foreach (
                FileInfo thumbnailFile in thumbnailRootDirectory.EnumerateFiles(
                    searchPattern,
                    enumerationOptions
                )
            )
            {
                if (!IsLegacyOwnedHashedThumbnailPath(thumbnailFile.FullName, oldBody, hash))
                {
                    continue;
                }

                yield return thumbnailFile;
            }
        }

        // hash の直後が終端か区切り記号なら、旧互換 suffix 付きでも現 movie の資産として扱う。
        internal static bool IsLegacyOwnedHashedThumbnailPath(
            string thumbnailPath,
            string oldBody,
            string hash
        )
        {
            if (
                string.IsNullOrWhiteSpace(thumbnailPath)
                || string.IsNullOrWhiteSpace(oldBody)
                || string.IsNullOrWhiteSpace(hash)
                || ThumbnailPathResolver.IsErrorMarker(thumbnailPath)
            )
            {
                return false;
            }

            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(thumbnailPath) ?? "";
            string expectedPrefix = oldBody + ".#" + hash;
            if (
                !fileNameWithoutExtension.StartsWith(
                    expectedPrefix,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return false;
            }

            if (fileNameWithoutExtension.Length == expectedPrefix.Length)
            {
                return true;
            }

            char suffixHead = fileNameWithoutExtension[expectedPrefix.Length];
            return !char.IsLetterOrDigit(suffixHead);
        }

        // hash付き実サムネに加えて ERROR マーカーも exact 名で列挙し、他動画を巻き込まない。
        private static IEnumerable<string> EnumerateExpectedThumbnailFileNames(
            string oldFullPath,
            string hash,
            bool canRenameHashedThumbnailAssets,
            bool canRenameErrorMarkerAssets
        )
        {
            if (canRenameHashedThumbnailAssets && !string.IsNullOrWhiteSpace(hash))
            {
                string thumbnailFileName = ThumbnailPathResolver.BuildThumbnailFileName(
                    oldFullPath,
                    hash
                );
                if (!string.IsNullOrWhiteSpace(thumbnailFileName))
                {
                    yield return thumbnailFileName;
                }
            }

            if (!canRenameErrorMarkerAssets)
            {
                yield break;
            }

            string errorMarkerFileName = ThumbnailPathResolver.BuildErrorMarkerFileName(oldFullPath);
            if (string.IsNullOrWhiteSpace(errorMarkerFileName))
            {
                yield break;
            }

            if (
                string.IsNullOrWhiteSpace(hash)
                || !string.Equals(
                    errorMarkerFileName,
                    ThumbnailPathResolver.BuildThumbnailFileName(oldFullPath, hash),
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                yield return errorMarkerFileName;
            }
        }

        private static void TryAddThumbnailPath(
            ISet<string> target,
            string thumbnailPath,
            string thumbnailRoot,
            string oldBody,
            string hash,
            bool canRenameHashedThumbnailAssets,
            bool canRenameErrorMarkerAssets
        )
        {
            if (
                string.IsNullOrWhiteSpace(thumbnailPath)
                || string.IsNullOrWhiteSpace(thumbnailRoot)
                || string.IsNullOrWhiteSpace(oldBody)
                || !File.Exists(thumbnailPath)
            )
            {
                return;
            }

            if (!IsPathUnderRoot(thumbnailPath, thumbnailRoot))
            {
                return;
            }

            if (ThumbnailPathResolver.IsErrorMarker(thumbnailPath))
            {
                if (!canRenameErrorMarkerAssets)
                {
                    return;
                }

                // UI が握る ERROR パスも、今の movie body に属する exact 名だけを採る。
                if (!IsOwnedErrorMarkerThumbnailPath(thumbnailPath, oldBody))
                {
                    return;
                }
            }
            else
            {
                if (!canRenameHashedThumbnailAssets)
                {
                    return;
                }

                // 接頭辞だけでなく body + hash が現 movie と一致する物だけ採る。
                if (!IsOwnedHashedThumbnailPath(thumbnailPath, oldBody, hash))
                {
                    return;
                }
            }

            target.Add(thumbnailPath);
        }

        // 既存 UI パスの正常 jpg は、exact hash 名か legacy 所有判定を通る物だけ採る。
        private static bool IsOwnedHashedThumbnailPath(string thumbnailPath, string oldBody, string hash)
        {
            if (
                string.IsNullOrWhiteSpace(thumbnailPath)
                || string.IsNullOrWhiteSpace(oldBody)
                || string.IsNullOrWhiteSpace(hash)
            )
            {
                return false;
            }

            string expectedFileName = ThumbnailPathResolver.BuildThumbnailFileName(oldBody, hash);
            if (
                string.Equals(
                    Path.GetFileName(thumbnailPath),
                    expectedFileName,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return true;
            }

            return IsLegacyOwnedHashedThumbnailPath(thumbnailPath, oldBody, hash);
        }

        // ERROR マーカーは固定 hash なので、旧 body の exact 名だけを所有物として扱う。
        private static bool IsOwnedErrorMarkerThumbnailPath(string thumbnailPath, string oldBody)
        {
            if (
                string.IsNullOrWhiteSpace(thumbnailPath)
                || string.IsNullOrWhiteSpace(oldBody)
                || !ThumbnailPathResolver.IsErrorMarker(thumbnailPath)
            )
            {
                return false;
            }

            return string.Equals(
                Path.GetFileName(thumbnailPath),
                ThumbnailPathResolver.BuildErrorMarkerFileName(oldBody),
                StringComparison.OrdinalIgnoreCase
            );
        }

        private static bool IsPathUnderRoot(string path, string root)
        {
            string fullPath = Path.GetFullPath(path);
            string fullRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        }

        // UI が握っている表示先パスも同時に差し替え、rename 後の見た目崩れを防ぐ。
        internal static void UpdateMovieThumbnailPath(
            MovieRecords movie,
            string sourcePath,
            string destinationPath
        )
        {
            if (string.Equals(movie.ThumbPathSmall, sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                movie.ThumbPathSmall = destinationPath;
            }
            if (string.Equals(movie.ThumbPathBig, sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                movie.ThumbPathBig = destinationPath;
            }
            if (string.Equals(movie.ThumbPathGrid, sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                movie.ThumbPathGrid = destinationPath;
            }
            if (string.Equals(movie.ThumbPathList, sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                movie.ThumbPathList = destinationPath;
            }
            if (string.Equals(movie.ThumbPathBig10, sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                movie.ThumbPathBig10 = destinationPath;
            }
            if (string.Equals(movie.ThumbDetail, sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                movie.ThumbDetail = destinationPath;
            }
        }
    }

    public partial class MainWindow
    {
        private sealed class RenameBridgeExecutionContext
        {
            public RenameBridgeExecutionContext(
                string snapshotDbFullPath,
                string thumbnailRoot,
                string bookmarkFolder,
                IReadOnlyList<MovieRecords> movieSnapshot,
                IReadOnlyList<MovieRecords> targets
            )
            {
                SnapshotDbFullPath = snapshotDbFullPath ?? "";
                ThumbnailRoot = thumbnailRoot ?? "";
                BookmarkFolder = bookmarkFolder ?? "";
                MovieSnapshot = movieSnapshot ?? [];
                Targets = targets ?? [];
            }

            public string SnapshotDbFullPath { get; }
            public string ThumbnailRoot { get; }
            public string BookmarkFolder { get; }
            public IReadOnlyList<MovieRecords> MovieSnapshot { get; }
            public IReadOnlyList<MovieRecords> Targets { get; }
        }

        private readonly record struct RenameBridgeMovieSnapshot(
            string MoviePath,
            string MovieName,
            string MovieBody
        );

        internal readonly record struct RenameBridgeOwnerCounts(
            int OtherOldMovieBodyOwnerCount,
            int OtherNewMovieBodyOwnerCount,
            int OtherOldThumbnailOwnerCount,
            int OtherNewThumbnailOwnerCount
        );

        private readonly record struct BookmarkRenameOperation(
            string SourcePath,
            string DestinationPath
        );

        // watch / manual の両方で必ずこの入口を通し、rename bridge の責務を一箇所へ寄せる。
        private void RenameThumb(string eFullPath, string oldFullPath)
        {
            RenameThumb(eFullPath, oldFullPath, canStartRenameBridge: null, logWatchMessage: null);
        }

        // watch rename は rename 本体へ入る直前でも stale guard し、MainVM 引き直し前で止める。
        private void RenameThumb(
            string eFullPath,
            string oldFullPath,
            Func<bool> canStartRenameBridge,
            Action<string> logWatchMessage
        )
        {
            if (string.IsNullOrWhiteSpace(eFullPath) || string.IsNullOrWhiteSpace(oldFullPath))
            {
                return;
            }

            // fallback 到達だけ観測し、本番の実行契約自体は変えない。
            RenamedWatchEventFallbackCallbackForTesting?.Invoke(
                eFullPath,
                oldFullPath,
                canStartRenameBridge,
                logWatchMessage
            );

            if (
                !TryBuildRenameBridgeExecutionContext(
                    eFullPath,
                    oldFullPath,
                    canStartRenameBridge,
                    logWatchMessage,
                    out RenameBridgeExecutionContext context
                )
            )
            {
                return;
            }

            bool shouldRefreshUi = false;
            try
            {
                foreach (MovieRecords movie in context.Targets)
                {
                    RenameSingleMovieBridge(movie, eFullPath, oldFullPath, context);
                    shouldRefreshUi = true;
                }
            }
            finally
            {
                if (shouldRefreshUi)
                {
                    RefreshRenameBridgeUi();
                }
            }
        }

        // 将来の await 呼び出し口が残っても、処理本体は同期入口のまま変えない。
        private Task RenameThumbAsync(string eFullPath, string oldFullPath)
        {
            RenameThumb(eFullPath, oldFullPath);
            return Task.CompletedTask;
        }

        // watch rename 側も manual rename 側と同じ入口へ寄せ、実処理の帯を分岐させない。
        internal static void ProcessRenamedWatchEventDirect(
            string eFullPath,
            string oldFullPath,
            Action<string, string> renameThumb
        )
        {
            if (
                string.IsNullOrWhiteSpace(eFullPath)
                || string.IsNullOrWhiteSpace(oldFullPath)
                || renameThumb == null
            )
            {
                return;
            }

            renameThumb(eFullPath, oldFullPath);
        }

        // watch 側だけ guard 付きの同期入口を通し、manual 側の入口はそのまま維持する。
        internal static void ProcessRenamedWatchEventDirect(
            string eFullPath,
            string oldFullPath,
            Action<string, string, Func<bool>, Action<string>> renameThumb,
            Func<bool> canStartRenameBridge,
            Action<string> logWatchMessage
        )
        {
            if (
                string.IsNullOrWhiteSpace(eFullPath)
                || string.IsNullOrWhiteSpace(oldFullPath)
                || renameThumb == null
            )
            {
                return;
            }

            renameThumb(eFullPath, oldFullPath, canStartRenameBridge, logWatchMessage);
        }

        // seam は観測だけに留め、本番 executor 契約は必ず同じ引数で流す。
        internal static void ProcessRenamedWatchEventDirect(
            string eFullPath,
            string oldFullPath,
            Action<string, string> callback,
            Action<string, string, Func<bool>, Action<string>> renameThumb,
            Func<bool> canStartRenameBridge,
            Action<string> logWatchMessage
        )
        {
            if (
                string.IsNullOrWhiteSpace(eFullPath)
                || string.IsNullOrWhiteSpace(oldFullPath)
                || renameThumb == null
            )
            {
                return;
            }

            callback?.Invoke(eFullPath, oldFullPath);
            renameThumb(eFullPath, oldFullPath, canStartRenameBridge, logWatchMessage);
        }

        // stale guard を rename 本体の最終入口へ置き、旧eventが新DB状態を引き直す前で止める。
        internal static bool TryEnterRenameBridgeForWatchScope(
            string newFullPath,
            string oldFullPath,
            Func<bool> canStartRenameBridge,
            Action<string> logWatchMessage
        )
        {
            if (canStartRenameBridge == null || canStartRenameBridge())
            {
                return true;
            }

            logWatchMessage?.Invoke(
                $"skip renamed movie by stale watch scope: old='{oldFullPath}' new='{newFullPath}'"
            );
            return false;
        }

        private bool TryBuildRenameBridgeExecutionContext(
            string newFullPath,
            string oldFullPath,
            Func<bool> canStartRenameBridge,
            Action<string> logWatchMessage,
            out RenameBridgeExecutionContext context
        )
        {
            context = null;
            if (
                !TryEnterRenameBridgeForWatchScope(
                    newFullPath,
                    oldFullPath,
                    canStartRenameBridge,
                    logWatchMessage
                )
            )
            {
                return false;
            }

            var mainVm = MainVM;
            string snapshotDbFullPath = mainVm?.DbInfo?.DBFullPath ?? "";
            if (string.IsNullOrWhiteSpace(snapshotDbFullPath))
            {
                return false;
            }

            List<MovieRecords> movieSnapshot =
                mainVm?.MovieRecs?.Where(movie => movie != null).ToList() ?? [];
            List<MovieRecords> targets = ResolveRenameBridgeTargets(movieSnapshot, oldFullPath);
            if (
                targets.Count < 1
                && TryResolveRenameBridgeFallbackMovie(
                    snapshotDbFullPath,
                    oldFullPath,
                    out MovieRecords fallbackMovie
                )
            )
            {
                movieSnapshot.Add(fallbackMovie);
                targets = [fallbackMovie];
                logWatchMessage?.Invoke(
                    $"rename bridge resolved movie by db fallback: old='{oldFullPath}' new='{newFullPath}'"
                );
            }

            if (targets.Count < 1)
            {
                return false;
            }

            string dbName = mainVm?.DbInfo?.DBName ?? "";
            string thumbFolder = mainVm?.DbInfo?.ThumbFolder ?? "";
            string bookmarkFolder = mainVm?.DbInfo?.BookmarkFolder ?? "";
            context = new RenameBridgeExecutionContext(
                snapshotDbFullPath,
                ResolveRuntimeThumbnailRoot(snapshotDbFullPath, dbName, thumbFolder),
                ResolveBookmarkFolderPathForRenameBridge(bookmarkFolder, dbName),
                movieSnapshot,
                targets
            );
            return true;
        }

        internal static List<MovieRecords> ResolveRenameBridgeTargets(
            IEnumerable<MovieRecords> movieSnapshot,
            string oldFullPath,
            MovieRecords fallbackMovie = null
        )
        {
            List<MovieRecords> targets =
                movieSnapshot?
                    .Where(movie =>
                        movie != null
                        && string.Equals(
                            movie.Movie_Path,
                            oldFullPath,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    .ToList() ?? [];
            if (targets.Count > 0)
            {
                return targets;
            }

            if (
                fallbackMovie == null
                || !string.Equals(
                    fallbackMovie.Movie_Path,
                    oldFullPath,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return targets;
            }

            targets.Add(fallbackMovie);
            return targets;
        }

        private bool TryResolveRenameBridgeFallbackMovie(
            string snapshotDbFullPath,
            string oldFullPath,
            out MovieRecords fallbackMovie
        )
        {
            fallbackMovie = null;
            if (
                !_mainDbMovieReadFacade.TryReadMovieByPath(
                    snapshotDbFullPath,
                    oldFullPath,
                    out MainDbMovieReadItemResult dbMovie
                )
            )
            {
                return false;
            }

            fallbackMovie = CreateRenameBridgeFallbackMovie(dbMovie);
            return fallbackMovie != null;
        }

        private static MovieRecords CreateRenameBridgeFallbackMovie(MainDbMovieReadItemResult source)
        {
            string moviePath = source.MoviePath ?? "";
            string ext = Path.GetExtension(moviePath) ?? "";

            return new MovieRecords
            {
                Movie_Id = source.MovieId,
                Movie_Name = ResolveMovieNameForRenameBridge(moviePath),
                Movie_Body = ResolveMovieBodyForRenameBridge(moviePath),
                Movie_Path = moviePath,
                Hash = source.Hash ?? "",
                Ext = ext,
                Drive = Path.GetPathRoot(moviePath) ?? "",
                Dir = Path.GetDirectoryName(moviePath) ?? "",
                IsExists = true,
            };
        }

        private void RenameSingleMovieBridge(
            MovieRecords movie,
            string newFullPath,
            string oldFullPath,
            RenameBridgeExecutionContext context
        )
        {
            if (
                movie == null
                || context == null
                || string.IsNullOrWhiteSpace(context.SnapshotDbFullPath)
            )
            {
                return;
            }

            string newMovieName = ResolveMovieNameForRenameBridge(newFullPath);
            string newMovieBody = ResolveMovieBodyForRenameBridge(newFullPath);
            string oldMovieBody = ResolveMovieBodyForRenameBridge(oldFullPath);
            RenameBridgeMovieSnapshot snapshot = new(
                movie.Movie_Path ?? "",
                movie.Movie_Name ?? "",
                movie.Movie_Body ?? ""
            );

            RenameBridgeOwnerCounts ownerCounts = ResolveRenameBridgeOwnerCounts(
                context.MovieSnapshot,
                context.SnapshotDbFullPath,
                _mainDbMovieReadFacade,
                oldMovieBody,
                newMovieBody,
                movie.Hash ?? "",
                oldFullPath
            );
            int otherOldMovieBodyOwnerCount = ownerCounts.OtherOldMovieBodyOwnerCount;
            int otherNewMovieBodyOwnerCount = ownerCounts.OtherNewMovieBodyOwnerCount;
            bool canRenameErrorMarkerAssets = CanRenameSharedOwnerAssetsSafely(
                otherOldMovieBodyOwnerCount,
                otherNewMovieBodyOwnerCount
            );
            bool canRenameHashedThumbnailAssets = CanRenameThumbnailAssetsSafely(
                ownerCounts.OtherOldThumbnailOwnerCount,
                ownerCounts.OtherNewThumbnailOwnerCount
            );

            List<ThumbnailRenameAssetTransferHelper.ThumbnailRenameOperation> thumbnailOperations =
                ThumbnailRenameAssetTransferHelper.BuildRenameOperations(
                    movie,
                    context.ThumbnailRoot,
                    oldFullPath,
                    newFullPath,
                    canRenameHashedThumbnailAssets,
                    canRenameErrorMarkerAssets
                );

            bool canRenameBookmarkAssets = CanRenameBookmarkAssetsSafely(
                otherOldMovieBodyOwnerCount,
                otherNewMovieBodyOwnerCount
            );
            bool shouldRenameBookmarkTable = ShouldRenameBookmarkTableEntries(
                canRenameBookmarkAssets,
                oldMovieBody,
                newMovieBody
            );
            List<BookmarkRenameOperation> bookmarkOperations =
                canRenameBookmarkAssets
                    ? BuildBookmarkRenameOperations(
                        context.BookmarkFolder,
                        oldMovieBody,
                        newMovieBody
                    )
                    : [];

            bool movieStateUpdated = false;
            bool moviePathUpdatedInDb = false;
            bool movieNameUpdatedInDb = false;
            bool bookmarkDbUpdated = false;
            List<ThumbnailRenameAssetTransferHelper.ThumbnailRenameOperation> movedThumbnails = [];
            List<BookmarkRenameOperation> movedBookmarks = [];

            try
            {
                // UI表示名は拡張子付き、DB保存名はbody-onlyとして分けて反映する。
                ApplyMovieRenameState(movie, newFullPath, newMovieName, newMovieBody);
                movieStateUpdated = true;

                _mainDbMovieMutationFacade.UpdateMoviePath(
                    context.SnapshotDbFullPath,
                    movie.Movie_Id,
                    newFullPath
                );
                moviePathUpdatedInDb = true;

                _mainDbMovieMutationFacade.UpdateMovieName(
                    context.SnapshotDbFullPath,
                    movie.Movie_Id,
                    newMovieBody.ToLowerInvariant()
                );
                movieNameUpdatedInDb = true;

                foreach (ThumbnailRenameAssetTransferHelper.ThumbnailRenameOperation operation in thumbnailOperations)
                {
                    MoveRenameAsset(operation.SourcePath, operation.DestinationPath);
                    ApplyThumbnailPathState(movie, operation.SourcePath, operation.DestinationPath);
                    if (!ThumbnailPathResolver.IsErrorMarker(operation.DestinationPath))
                    {
                        ThumbnailPathResolver.RememberSuccessThumbnailPath(operation.DestinationPath);
                    }

                    movedThumbnails.Add(operation);
                }

                foreach (BookmarkRenameOperation operation in bookmarkOperations)
                {
                    MoveRenameAsset(operation.SourcePath, operation.DestinationPath);
                    movedBookmarks.Add(operation);
                }

                if (shouldRenameBookmarkTable)
                {
                    RenameBookmarkTableEntries(
                        context.SnapshotDbFullPath,
                        oldMovieBody,
                        newMovieBody
                    );
                    bookmarkDbUpdated = true;
                }
            }
            catch (Exception ex)
            {
                // 途中まで進んだ段だけを逆順で戻し、未着手の段には触れない。
                List<Exception> rollbackFailures = ExecuteRenameBridgeRollbackSteps(
                    BuildRenameBridgeRollbackSteps(
                        bookmarkDbUpdated,
                        rollbackBookmarkDb: () =>
                            RenameBookmarkTableEntries(
                                context.SnapshotDbFullPath,
                                newMovieBody,
                                oldMovieBody
                            ),
                        bookmarkMoveRollbacks: movedBookmarks
                            .Select(operation => (Action)(() =>
                                RollbackRenameAsset(
                                    operation.SourcePath,
                                    operation.DestinationPath
                                )
                            ))
                            .ToArray(),
                        thumbnailMoveRollbacks: movedThumbnails
                            .Select(operation => (Action)(() =>
                            {
                                RollbackRenameAsset(
                                    operation.SourcePath,
                                    operation.DestinationPath
                                );
                                ApplyThumbnailPathState(
                                    movie,
                                    operation.DestinationPath,
                                    operation.SourcePath
                                );
                            }))
                            .ToArray(),
                        movieNameUpdatedInDb,
                        rollbackMovieName: () =>
                            _mainDbMovieMutationFacade.UpdateMovieName(
                                context.SnapshotDbFullPath,
                                movie.Movie_Id,
                                oldMovieBody.ToLowerInvariant()
                            ),
                        moviePathUpdatedInDb,
                        rollbackMoviePath: () =>
                            _mainDbMovieMutationFacade.UpdateMoviePath(
                                context.SnapshotDbFullPath,
                                movie.Movie_Id,
                                oldFullPath
                            ),
                        movieStateUpdated,
                        rollbackMovieState: () =>
                            ApplyMovieRenameState(
                                movie,
                                snapshot.MoviePath,
                                snapshot.MovieName,
                                snapshot.MovieBody
                            )
                    )
                );
                if (rollbackFailures.Count > 0)
                {
                    throw BuildRenameBridgeRollbackFailure(ex, rollbackFailures);
                }

                throw;
            }
        }

        // 既存 bookmark は動画名プレフィックスの完全一致だけを対象にし、部分一致を巻き込まない。
        internal static string TryBuildRenamedBookmarkAssetPath(
            string sourcePath,
            string bookmarkRoot,
            string oldMovieName,
            string newMovieName
        )
        {
            if (
                string.IsNullOrWhiteSpace(sourcePath)
                || string.IsNullOrWhiteSpace(bookmarkRoot)
                || string.IsNullOrWhiteSpace(oldMovieName)
                || string.IsNullOrWhiteSpace(newMovieName)
                || !File.Exists(sourcePath)
            )
            {
                return "";
            }

            string fullSourcePath = Path.GetFullPath(sourcePath);
            string fullBookmarkRoot = Path.GetFullPath(bookmarkRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!fullSourcePath.StartsWith(fullBookmarkRoot, StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }

            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(sourcePath) ?? "";
            string expectedPrefix = oldMovieName + "[(";
            if (
                !fileNameWithoutExtension.StartsWith(
                    expectedPrefix,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return "";
            }

            string suffix = fileNameWithoutExtension[oldMovieName.Length..];
            return Path.Combine(
                Path.GetDirectoryName(sourcePath) ?? bookmarkRoot,
                newMovieName + suffix + (Path.GetExtension(sourcePath) ?? "")
            );
        }

        // 同名共有がある資産は rename で巻き込まない。old/new どちらかに他所有者がいれば止める。
        internal static bool CanRenameSharedOwnerAssetsSafely(
            int otherOldNameOwnerCount,
            int otherNewNameOwnerCount
        )
        {
            return otherOldNameOwnerCount < 1 && otherNewNameOwnerCount < 1;
        }

        // bookmark は body 単位で共有されるので、同名共有がある時は自動改名を止める。
        internal static bool CanRenameBookmarkAssetsSafely(
            int otherOldNameOwnerCount,
            int otherNewNameOwnerCount
        )
        {
            return CanRenameSharedOwnerAssetsSafely(
                otherOldNameOwnerCount,
                otherNewNameOwnerCount
            );
        }

        // hash 付き jpg は body + hash を共有する別動画がいる時だけ自動改名を止める。
        internal static bool CanRenameThumbnailAssetsSafely(
            int otherOldThumbnailOwnerCount,
            int otherNewThumbnailOwnerCount
        )
        {
            return CanRenameSharedOwnerAssetsSafely(
                otherOldThumbnailOwnerCount,
                otherNewThumbnailOwnerCount
            );
        }

        // UIモデルの Movie_Name は表示名契約なので、拡張子込みのファイル名を返す。
        internal static string ResolveMovieNameForRenameBridge(string movieFullPath)
        {
            return Path.GetFileName(movieFullPath) ?? "";
        }

        // DB更新とbookmark判定は body-only 契約なので、表示名とは分けて扱う。
        internal static string ResolveMovieBodyForRenameBridge(string movieFullPath)
        {
            return Path.GetFileNameWithoutExtension(movieFullPath) ?? "";
        }

        // bookmark DB rename は jpg 有無に引きずらず、安全判定と実際の改名有無だけで決める。
        internal static bool ShouldRenameBookmarkTableEntries(
            bool canRenameBookmarkAssets,
            string oldMovieName,
            string newMovieName
        )
        {
            return canRenameBookmarkAssets
                && !string.IsNullOrWhiteSpace(oldMovieName)
                && !string.IsNullOrWhiteSpace(newMovieName)
                && !string.Equals(oldMovieName, newMovieName, StringComparison.OrdinalIgnoreCase);
        }

        internal static RenameBridgeOwnerCounts ResolveRenameBridgeOwnerCounts(
            IEnumerable<MovieRecords> movieSnapshot,
            string snapshotDbFullPath,
            IMainDbMovieReadFacade mainDbMovieReadFacade,
            string oldMovieBody,
            string newMovieBody,
            string hash,
            string excludedMoviePath
        )
        {
            RenameBridgeOwnerCounts snapshotCounts = new(
                CountOtherMovieBodyOwners(movieSnapshot, oldMovieBody, excludedMoviePath),
                CountOtherMovieBodyOwners(movieSnapshot, newMovieBody, excludedMoviePath),
                CountOtherThumbnailOwners(movieSnapshot, oldMovieBody, hash, excludedMoviePath),
                CountOtherThumbnailOwners(movieSnapshot, newMovieBody, hash, excludedMoviePath)
            );
            if (
                mainDbMovieReadFacade == null
                || string.IsNullOrWhiteSpace(snapshotDbFullPath)
                || !mainDbMovieReadFacade.TryReadRenameBridgeOwnerCounts(
                    snapshotDbFullPath,
                    excludedMoviePath,
                    oldMovieBody,
                    newMovieBody,
                    hash,
                    out MainDbRenameBridgeOwnerCountsResult dbCounts
                )
            )
            {
                return snapshotCounts;
            }

            // UI snapshot が partial でも DB の hidden owner 数を優先し、共有資産 rename を安全側に倒す。
            return new RenameBridgeOwnerCounts(
                OtherOldMovieBodyOwnerCount: Math.Max(
                    snapshotCounts.OtherOldMovieBodyOwnerCount,
                    dbCounts.OtherOldMovieBodyOwnerCount
                ),
                OtherNewMovieBodyOwnerCount: Math.Max(
                    snapshotCounts.OtherNewMovieBodyOwnerCount,
                    dbCounts.OtherNewMovieBodyOwnerCount
                ),
                OtherOldThumbnailOwnerCount: Math.Max(
                    snapshotCounts.OtherOldThumbnailOwnerCount,
                    dbCounts.OtherOldThumbnailOwnerCount
                ),
                OtherNewThumbnailOwnerCount: Math.Max(
                    snapshotCounts.OtherNewThumbnailOwnerCount,
                    dbCounts.OtherNewThumbnailOwnerCount
                )
            );
        }

        private static int CountOtherMovieBodyOwners(
            IEnumerable<MovieRecords> movieSnapshot,
            string movieBody,
            string excludedMoviePath
        )
        {
            if (string.IsNullOrWhiteSpace(movieBody) || movieSnapshot == null)
            {
                return 0;
            }

            return movieSnapshot
                .Where(movie =>
                    movie != null
                    && !string.Equals(
                        movie.Movie_Path,
                        excludedMoviePath,
                        StringComparison.OrdinalIgnoreCase
                    )
                    && string.Equals(
                        Path.GetFileNameWithoutExtension(movie.Movie_Path) ?? "",
                        movieBody,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                .Count();
        }

        // hash無しは共有実体を特定しづらいので、body共有が見えたら安全側で止める。
        private static int CountOtherThumbnailOwners(
            IEnumerable<MovieRecords> movieSnapshot,
            string movieBody,
            string hash,
            string excludedMoviePath
        )
        {
            if (string.IsNullOrWhiteSpace(movieBody) || movieSnapshot == null)
            {
                return 0;
            }

            if (string.IsNullOrWhiteSpace(hash))
            {
                return CountOtherMovieBodyOwners(movieSnapshot, movieBody, excludedMoviePath);
            }

            return movieSnapshot
                .Where(movie =>
                    movie != null
                    && !string.Equals(
                        movie.Movie_Path,
                        excludedMoviePath,
                        StringComparison.OrdinalIgnoreCase
                    )
                    && string.Equals(
                        Path.GetFileNameWithoutExtension(movie.Movie_Path) ?? "",
                        movieBody,
                        StringComparison.OrdinalIgnoreCase
                    )
                    && string.Equals(movie.Hash ?? "", hash, StringComparison.OrdinalIgnoreCase)
                )
                .Count();
        }

        // bookmark パスも context へ閉じ、rename 中に別DB設定へ引き直さない。
        private static string ResolveBookmarkFolderPathForRenameBridge(
            string bookmarkFolder,
            string dbName
        )
        {
            if (string.IsNullOrWhiteSpace(dbName))
            {
                return bookmarkFolder ?? "";
            }

            string defaultBookmarkFolder = Path.Combine(
                Directory.GetCurrentDirectory(),
                "bookmark",
                dbName
            );
            return string.IsNullOrWhiteSpace(bookmarkFolder)
                ? defaultBookmarkFolder
                : bookmarkFolder;
        }

        private static List<BookmarkRenameOperation> BuildBookmarkRenameOperations(
            string bookmarkFolder,
            string oldMovieName,
            string newMovieName
        )
        {
            List<BookmarkRenameOperation> operations = [];
            if (string.IsNullOrWhiteSpace(bookmarkFolder) || !Directory.Exists(bookmarkFolder))
            {
                return operations;
            }

            foreach (string sourcePath in Directory.EnumerateFiles(bookmarkFolder, "*.jpg"))
            {
                string destinationPath = TryBuildRenamedBookmarkAssetPath(
                    sourcePath,
                    bookmarkFolder,
                    oldMovieName,
                    newMovieName
                );
                if (
                    string.IsNullOrWhiteSpace(destinationPath)
                    || string.Equals(
                        sourcePath,
                        destinationPath,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    continue;
                }

                operations.Add(new BookmarkRenameOperation(sourcePath, destinationPath));
            }

            return operations;
        }

        // bookmark DB も prefix 一致の行だけを更新し、LIKE wildcard を必ず無害化する。
        internal static void RenameBookmarkTableEntries(
            string dbFullPath,
            string oldMovieName,
            string newMovieName
        )
        {
            if (
                string.IsNullOrWhiteSpace(dbFullPath)
                || string.IsNullOrWhiteSpace(oldMovieName)
                || string.IsNullOrWhiteSpace(newMovieName)
            )
            {
                return;
            }

            string oldPrefix = (oldMovieName + "[(").ToLowerInvariant();
            string newPrefix = (newMovieName + "[(").ToLowerInvariant();
            string escapedOldPrefix = EscapeSqlLikeValue(oldPrefix);
            using System.Data.SQLite.SQLiteConnection connection = new($"Data Source={dbFullPath}");
            connection.Open();

            using System.Data.SQLite.SQLiteTransaction transaction = connection.BeginTransaction();
            using System.Data.SQLite.SQLiteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                "update bookmark set "
                + "movie_name = @newPrefix || substr(lower(movie_name), length(@oldPrefix) + 1), "
                + "movie_path = @newPrefix || substr(lower(movie_path), length(@oldPrefix) + 1) "
                + "where lower(movie_name) like @namePattern escape '\\' "
                + "and lower(movie_path) like @pathPattern escape '\\'";
            command.Parameters.Add(new System.Data.SQLite.SQLiteParameter("@newPrefix", newPrefix));
            command.Parameters.Add(new System.Data.SQLite.SQLiteParameter("@oldPrefix", oldPrefix));
            command.Parameters.Add(
                new System.Data.SQLite.SQLiteParameter("@namePattern", escapedOldPrefix + "%")
            );
            command.Parameters.Add(
                new System.Data.SQLite.SQLiteParameter("@pathPattern", escapedOldPrefix + "%")
            );
            command.ExecuteNonQuery();
            transaction.Commit();
        }

        // SQLite LIKE の wildcard 文字は前置 escape を付け、prefix 判定を文字どおりへ戻す。
        internal static string EscapeSqlLikeValue(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }

            StringBuilder builder = new(value.Length + 8);
            foreach (char current in value)
            {
                if (current is '\\' or '%' or '_' or '[')
                {
                    builder.Append('\\');
                }

                builder.Append(current);
            }

            return builder.ToString();
        }

        private static void MoveRenameAsset(string sourcePath, string destinationPath)
        {
            if (
                string.IsNullOrWhiteSpace(sourcePath)
                || string.IsNullOrWhiteSpace(destinationPath)
                || string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase)
            )
            {
                return;
            }

            if (!File.Exists(sourcePath))
            {
                return;
            }

            if (File.Exists(destinationPath))
            {
                throw new IOException(
                    $"rename bridge destination already exists: '{destinationPath}'"
                );
            }

            string destinationDirectory = Path.GetDirectoryName(destinationPath) ?? "";
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            File.Move(sourcePath, destinationPath);
        }

        // rollback は try 内で完了した段だけを積み、catch ではその順序どおりに実行する。
        internal static List<Action> BuildRenameBridgeRollbackSteps(
            bool bookmarkDbUpdated,
            Action rollbackBookmarkDb,
            IEnumerable<Action> bookmarkMoveRollbacks,
            IEnumerable<Action> thumbnailMoveRollbacks,
            bool movieNameUpdatedInDb,
            Action rollbackMovieName,
            bool moviePathUpdatedInDb,
            Action rollbackMoviePath,
            bool movieStateUpdated,
            Action rollbackMovieState
        )
        {
            List<Action> rollbackSteps = [];

            if (bookmarkDbUpdated)
            {
                rollbackSteps.Add(rollbackBookmarkDb);
            }

            AppendRollbackStepsInReverse(rollbackSteps, bookmarkMoveRollbacks);
            AppendRollbackStepsInReverse(rollbackSteps, thumbnailMoveRollbacks);

            if (movieNameUpdatedInDb)
            {
                rollbackSteps.Add(rollbackMovieName);
            }

            if (moviePathUpdatedInDb)
            {
                rollbackSteps.Add(rollbackMoviePath);
            }

            if (movieStateUpdated)
            {
                rollbackSteps.Add(rollbackMovieState);
            }

            return rollbackSteps;
        }

        internal static List<Exception> ExecuteRenameBridgeRollbackSteps(
            IEnumerable<Action> rollbackSteps
        )
        {
            List<Exception> failures = [];
            if (rollbackSteps == null)
            {
                return failures;
            }

            foreach (Action rollbackStep in rollbackSteps)
            {
                if (rollbackStep == null)
                {
                    continue;
                }

                try
                {
                    rollbackStep();
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                }
            }

            return failures;
        }

        internal static AggregateException BuildRenameBridgeRollbackFailure(
            Exception originalException,
            IEnumerable<Exception> rollbackFailures
        )
        {
            List<Exception> failures = [];
            if (originalException != null)
            {
                failures.Add(originalException);
            }

            if (rollbackFailures != null)
            {
                failures.AddRange(rollbackFailures.Where(ex => ex != null));
            }

            return new AggregateException(
                "rename bridge failed and rollback reported additional failures.",
                failures
            );
        }

        private static void AppendRollbackStepsInReverse(
            ICollection<Action> rollbackSteps,
            IEnumerable<Action> stepsToAppend
        )
        {
            foreach (Action rollbackStep in stepsToAppend.Reverse())
            {
                rollbackSteps.Add(rollbackStep);
            }
        }

        private static void RollbackBookmarkMoves(IEnumerable<BookmarkRenameOperation> operations)
        {
            foreach (BookmarkRenameOperation operation in operations.Reverse())
            {
                RollbackRenameAsset(operation.SourcePath, operation.DestinationPath);
            }
        }

        private void RollbackThumbnailMoves(
            MovieRecords movie,
            IEnumerable<ThumbnailRenameAssetTransferHelper.ThumbnailRenameOperation> operations
        )
        {
            foreach (ThumbnailRenameAssetTransferHelper.ThumbnailRenameOperation operation in operations.Reverse())
            {
                RollbackRenameAsset(operation.SourcePath, operation.DestinationPath);
                ApplyThumbnailPathState(movie, operation.DestinationPath, operation.SourcePath);
            }
        }

        private static void RollbackRenameAsset(string sourcePath, string destinationPath)
        {
            if (!File.Exists(destinationPath) || File.Exists(sourcePath))
            {
                return;
            }

            string sourceDirectory = Path.GetDirectoryName(sourcePath) ?? "";
            if (!string.IsNullOrWhiteSpace(sourceDirectory))
            {
                Directory.CreateDirectory(sourceDirectory);
            }

            File.Move(destinationPath, sourcePath);
        }

        private void RefreshRenameBridgeUi()
        {
            void RefreshCore()
            {
                ReloadBookmarkTabData();
                FilterAndSort(MainVM?.DbInfo?.Sort ?? "", true);
                Refresh();
            }

            if (Dispatcher.CheckAccess())
            {
                RefreshCore();
                return;
            }

            Dispatcher.Invoke(RefreshCore, System.Windows.Threading.DispatcherPriority.Background);
        }

        // rename 後の in-memory モデルは path 派生メタも同時に揃え、再読込なしでも整合を保つ。
        internal static void ApplyMovieRenameStateCore(
            MovieRecords movie,
            string moviePath,
            string movieName,
            string movieBody
        )
        {
            if (movie == null)
            {
                return;
            }

            movie.Movie_Path = moviePath;
            movie.Movie_Name = movieName;
            movie.Movie_Body = movieBody;
            movie.Ext = Path.GetExtension(moviePath) ?? "";
            movie.Drive = Path.GetPathRoot(moviePath) ?? "";
            movie.Dir = Path.GetDirectoryName(moviePath) ?? "";
        }

        private void ApplyMovieRenameState(
            MovieRecords movie,
            string moviePath,
            string movieName,
            string movieBody
        )
        {
            void Apply()
            {
                ApplyMovieRenameStateCore(movie, moviePath, movieName, movieBody);
            }

            if (Dispatcher.CheckAccess())
            {
                Apply();
                return;
            }

            Dispatcher.Invoke(Apply, System.Windows.Threading.DispatcherPriority.Background);
        }

        private void ApplyThumbnailPathState(
            MovieRecords movie,
            string sourcePath,
            string destinationPath
        )
        {
            void Apply()
            {
                ThumbnailRenameAssetTransferHelper.UpdateMovieThumbnailPath(
                    movie,
                    sourcePath,
                    destinationPath
                );
            }

            if (Dispatcher.CheckAccess())
            {
                Apply();
                return;
            }

            Dispatcher.Invoke(Apply, System.Windows.Threading.DispatcherPriority.Background);
        }
    }
}
