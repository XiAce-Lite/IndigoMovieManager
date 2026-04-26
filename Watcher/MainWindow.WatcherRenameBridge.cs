using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using IndigoMovieManager.Thumbnail;
using static IndigoMovieManager.DB.SQLite;

namespace IndigoMovieManager
{
    internal static class ThumbnailRenameAssetTransferHelper
    {
        // 現在表示中の実サムネと、旧命名で残っているjpgをまとめて新名へ寄せる。
        internal static void RenameThumbnailFiles(
            MovieRecords movie,
            string thumbnailRoot,
            string oldFullPath,
            string newFullPath
        )
        {
            if (movie == null || string.IsNullOrWhiteSpace(thumbnailRoot) || !Directory.Exists(thumbnailRoot))
            {
                return;
            }

            foreach (
                string sourcePath in EnumerateThumbnailSourcePaths(
                    movie,
                    thumbnailRoot,
                    oldFullPath
                )
            )
            {
                string destinationPath = TryBuildRenamedThumbnailPath(
                    sourcePath,
                    oldFullPath,
                    newFullPath
                );
                if (string.IsNullOrWhiteSpace(destinationPath))
                {
                    continue;
                }

                UpdateMovieThumbnailPath(movie, sourcePath, destinationPath);
                if (string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                FileInfo thumbnailFile = new(sourcePath);
                if (!thumbnailFile.Exists)
                {
                    continue;
                }

                thumbnailFile.MoveTo(destinationPath, true);
                if (!ThumbnailPathResolver.IsErrorMarker(destinationPath))
                {
                    ThumbnailPathResolver.RememberSuccessThumbnailPath(destinationPath);
                }
            }
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

        // まず表示中のパスを尊重し、その後で旧命名の取りこぼしだけを追加で拾う。
        private static IEnumerable<string> EnumerateThumbnailSourcePaths(
            MovieRecords movie,
            string thumbnailRoot,
            string oldFullPath
        )
        {
            HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
            string oldBody = Path.GetFileNameWithoutExtension(oldFullPath) ?? "";

            TryAddThumbnailPath(paths, movie.ThumbPathSmall, thumbnailRoot, oldBody);
            TryAddThumbnailPath(paths, movie.ThumbPathBig, thumbnailRoot, oldBody);
            TryAddThumbnailPath(paths, movie.ThumbPathGrid, thumbnailRoot, oldBody);
            TryAddThumbnailPath(paths, movie.ThumbPathList, thumbnailRoot, oldBody);
            TryAddThumbnailPath(paths, movie.ThumbPathBig10, thumbnailRoot, oldBody);
            TryAddThumbnailPath(paths, movie.ThumbDetail, thumbnailRoot, oldBody);

            if (string.IsNullOrWhiteSpace(oldBody))
            {
                return paths;
            }

            DirectoryInfo thumbnailRootDirectory = new(thumbnailRoot);
            EnumerationOptions enumerationOptions = new() { RecurseSubdirectories = true };
            foreach (string searchPattern in EnumerateLegacySearchPatterns(oldBody))
            {
                foreach (
                    FileInfo thumbnailFile in thumbnailRootDirectory.EnumerateFiles(
                        searchPattern,
                        enumerationOptions
                    )
                )
                {
                    paths.Add(thumbnailFile.FullName);
                }
            }

            return paths;
        }

        private static IEnumerable<string> EnumerateLegacySearchPatterns(string oldBody)
        {
            yield return oldBody + ".jpg";
            yield return oldBody + ".#*.jpg";
        }

        private static void TryAddThumbnailPath(
            ISet<string> target,
            string thumbnailPath,
            string thumbnailRoot,
            string oldBody
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

            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(thumbnailPath) ?? "";
            if (
                !string.Equals(fileNameWithoutExtension, oldBody, StringComparison.OrdinalIgnoreCase)
                && !fileNameWithoutExtension.StartsWith(
                    oldBody + ".#",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return;
            }

            target.Add(thumbnailPath);
        }

        private static bool IsPathUnderRoot(string path, string root)
        {
            string fullPath = Path.GetFullPath(path);
            string fullRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        }

        // UIが握っている各表示先パスも同時に差し替え、リロード前の見た目崩れを防ぐ。
        private static void UpdateMovieThumbnailPath(
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
        // テスト/既存呼び出し側向けに、rename 入口の薄い橋渡しを維持する。
        internal static void ProcessRenamedWatchEventDirect(
            string eFullPath,
            string oldFullPath,
            Action<string, string> onRenamedWatch
        )
        {
            Action<string, string, Func<bool>, Action<string>> renameAction =
                (_, _, _, _) => onRenamedWatch?.Invoke(eFullPath, oldFullPath);
            ProcessRenamedWatchEventDirect(
                eFullPath,
                oldFullPath,
                renameAction,
                () => true,
                null
            );
        }

        // rename 本体直前でのガード付き入口を、既存シーケンスを崩さず再現する。
        internal static void ProcessRenamedWatchEventDirect(
            string eFullPath,
            string oldFullPath,
            Action<string, string, Func<bool>, Action<string>> renameAction,
            Func<bool> canStartRenameBridge,
            Action<string> logWatchMessage
        )
        {
            Func<bool> canStartRenameBridgeOrDefault = canStartRenameBridge ?? (() => true);
            renameAction?.Invoke(eFullPath, oldFullPath, canStartRenameBridgeOrDefault, logWatchMessage);
        }

        // callback を先に受けてから rename 本体を呼ぶ古い順序を維持する。
        internal static void ProcessRenamedWatchEventDirect(
            string eFullPath,
            string oldFullPath,
            Action<string, string> onRenamedWatch,
            Action<string, string, Func<bool>, Action<string>> renameAction,
            Func<bool> canStartRenameBridge,
            Action<string> logWatchMessage
        )
        {
            onRenamedWatch?.Invoke(eFullPath, oldFullPath);
            ProcessRenamedWatchEventDirect(
                eFullPath,
                oldFullPath,
                renameAction,
                canStartRenameBridge,
                logWatchMessage
            );
        }

        // 既存UI操作との互換のため、従来名は fire-and-forget の薄い入口として残す。
        private void RenameThumb(string eFullPath, string oldFullPath)
        {
            _ = RenameThumbAsync(eFullPath, oldFullPath);
        }

        /// <summary>
        /// リネームイベントを検知！DB・サムネ・ブックマークの全方位に「名前変わったぞ！」と号令をかけて回る怒涛の追従処理！🏃‍♂️💨
        /// </summary>
        private async Task RenameThumbAsync(string eFullPath, string oldFullPath)
        {
            try
            {
                List<WatchChangedMovie> changedMovies = [];
                foreach (
                    var item in MainVM.MovieRecs.Where(x =>
                        IsMoviePathMatchForRename(x?.Movie_Path, oldFullPath)
                    )
                )
                {
                    item.Movie_Path = eFullPath;
                    item.Movie_Name = Path.GetFileNameWithoutExtension(eFullPath).ToLower();
                    string persistedKana = JapaneseKanaProvider.GetKanaForPersistence(
                        item.Movie_Name,
                        item.Movie_Path
                    );
                    string persistedRoma = JapaneseKanaProvider.GetRomaFromKanaForPersistence(
                        persistedKana
                    );
                    item.Kana = persistedKana;
                    item.Roma = persistedRoma;

                    // DB更新は rename bridge 側へ寄せ、watch イベントと同じ責務領域で扱う。
                    _mainDbMovieMutationFacade.UpdateMoviePath(
                        MainVM.DbInfo.DBFullPath,
                        item.Movie_Id,
                        item.Movie_Path
                    );
                    _mainDbMovieMutationFacade.UpdateMovieName(
                        MainVM.DbInfo.DBFullPath,
                        item.Movie_Id,
                        item.Movie_Name
                    );
                    _mainDbMovieMutationFacade.UpdateKana(
                        MainVM.DbInfo.DBFullPath,
                        item.Movie_Id,
                        persistedKana
                    );
                    _mainDbMovieMutationFacade.UpdateRoma(
                        MainVM.DbInfo.DBFullPath,
                        item.Movie_Id,
                        persistedRoma
                    );
                    changedMovies.Add(
                        new WatchChangedMovie(
                            item.Movie_Path,
                            WatchMovieChangeKind.None,
                            WatchMovieDirtyFields.MovieName
                                | WatchMovieDirtyFields.MoviePath
                                | WatchMovieDirtyFields.Kana
                        )
                    );

                    var checkFileName = Path.GetFileNameWithoutExtension(oldFullPath);
                    string thumbFolder = ResolveCurrentThumbnailRoot();

                    ThumbnailRenameAssetTransferHelper.RenameThumbnailFiles(
                        item,
                        thumbFolder,
                        oldFullPath,
                        eFullPath
                    );

                    string bookmarkFolder = ResolveBookmarkFolderPath();

                    if (Path.Exists(bookmarkFolder))
                    {
                        var di = new DirectoryInfo(bookmarkFolder);
                        EnumerationOptions enumOption = new() { RecurseSubdirectories = true };
                        IEnumerable<FileInfo> ssFiles = di.EnumerateFiles(
                            $"*{checkFileName}*.jpg",
                            enumOption
                        );
                        foreach (var bookMarkJpg in ssFiles)
                        {
                            string dstFile = BuildBookmarkRenameDestinationPath(
                                bookMarkJpg.FullName,
                                checkFileName,
                                item.Movie_Name
                            );
                            if (
                                !string.IsNullOrWhiteSpace(dstFile)
                                && !string.Equals(
                                    bookMarkJpg.FullName,
                                    dstFile,
                                    StringComparison.OrdinalIgnoreCase
                                )
                            )
                            {
                                File.Move(bookMarkJpg.FullName, dstFile, true);
                            }
                        }

                        UpdateBookmarkRename(
                            MainVM.DbInfo.DBFullPath,
                            checkFileName,
                            item.Movie_Name
                        );
                    }
                }

                // Created 直後に rename されて旧パスが未登録だった場合は、
                // rename だけでは取り込めないため watch scan へ再合流して最終整合を回収する。
                if (changedMovies.Count < 1)
                {
                    TryQueueWatchScanForUntrackedRename(eFullPath, oldFullPath);
                    return;
                }

                string currentSort = MainVM?.DbInfo?.Sort ?? "";
                await Dispatcher.InvokeAsync(() => ReloadBookmarkTabData());
                await RefreshMovieViewAfterRenameAsync(currentSort, changedMovies);
            }
            catch (Exception) { }
        }

        // 旧パス未登録の rename は scan 本流へ戻し、Created -> Renamed 連鎖の取りこぼしを防ぐ。
        private void TryQueueWatchScanForUntrackedRename(string newFullPath, string oldFullPath)
        {
            if (!ShouldQueueWatchScanForUntrackedRename(newFullPath, oldFullPath))
            {
                return;
            }

            DebugRuntimeLog.Write(
                "watch",
                $"rename without tracked movie rerouted to queued watch scan: old='{oldFullPath}' new='{newFullPath}'"
            );
            _ = QueueCheckFolderAsync(CheckMode.Watch, $"renamed-untracked:{newFullPath}");
        }

        internal static bool ShouldQueueWatchScanForUntrackedRename(
            string newFullPath,
            string oldFullPath
        )
        {
            if (
                string.IsNullOrWhiteSpace(newFullPath)
                || string.IsNullOrWhiteSpace(oldFullPath)
                || string.Equals(newFullPath, oldFullPath, StringComparison.OrdinalIgnoreCase)
            )
            {
                return false;
            }

            return File.Exists(newFullPath);
        }

        // Windows の rename は大文字小文字違いだけでも飛んでくるため、比較は大文字小文字を無視する。
        internal static bool IsMoviePathMatchForRename(string currentMoviePath, string oldFullPath)
        {
            if (string.IsNullOrWhiteSpace(currentMoviePath) || string.IsNullOrWhiteSpace(oldFullPath))
            {
                return false;
            }

            return string.Equals(
                currentMoviePath,
                oldFullPath,
                StringComparison.OrdinalIgnoreCase
            );
        }

        // bookmark の rename はファイル名部分だけを差し替え、親フォルダまで巻き込まない。
        internal static string BuildBookmarkRenameDestinationPath(
            string bookmarkFilePath,
            string oldFileName,
            string newMovieName
        )
        {
            if (
                string.IsNullOrWhiteSpace(bookmarkFilePath)
                || string.IsNullOrWhiteSpace(oldFileName)
                || string.IsNullOrWhiteSpace(newMovieName)
            )
            {
                return bookmarkFilePath ?? "";
            }

            string directoryPath = Path.GetDirectoryName(bookmarkFilePath) ?? "";
            string fileName = Path.GetFileName(bookmarkFilePath) ?? "";
            string renamedFileName = fileName.Replace(
                oldFileName,
                newMovieName,
                StringComparison.OrdinalIgnoreCase
            );

            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                return renamedFileName;
            }

            return Path.Combine(directoryPath, renamedFileName);
        }
    }
}
