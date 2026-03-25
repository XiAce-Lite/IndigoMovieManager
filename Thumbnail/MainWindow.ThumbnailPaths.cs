using System;
using System.Collections.Generic;
using System.IO;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private static readonly HashSet<string> KnownThumbnailLayoutFolderNames =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ThumbnailLayoutProfileResolver.Small.FolderName,
                ThumbnailLayoutProfileResolver.Big.FolderName,
                ThumbnailLayoutProfileResolver.Grid.FolderName,
                ThumbnailLayoutProfileResolver.List.FolderName,
                ThumbnailLayoutProfileResolver.Big10.FolderName,
                ThumbnailLayoutProfileResolver.DetailStandard.FolderName,
                ThumbnailLayoutProfileResolver.DetailWhiteBrowser.FolderName,
            };

        // MainWindow 配下の partial で同じ保存先規則を共有し、layout / path 解決を一箇所へ寄せる。
        private static ThumbnailLayoutProfile ResolveThumbnailLayoutProfile(int tabIndex)
        {
            return ThumbnailLayoutProfileResolver.Resolve(
                tabIndex,
                ThumbnailDetailModeRuntime.ReadRuntimeMode()
            );
        }

        // 現在の app 規約どおり、thumbFolder 未指定時は既定Thumb配下へ寄せる。
        private static string ResolveThumbnailOutPath(int tabIndex, string dbName, string thumbFolder)
        {
            ThumbnailLayoutProfile layoutProfile = ResolveThumbnailLayoutProfile(tabIndex);
            string thumbRoot = string.IsNullOrWhiteSpace(thumbFolder)
                ? ThumbRootResolver.GetDefaultThumbRoot(dbName)
                : thumbFolder;

            string resolvedRoot = ResolveCurrentCompatibleThumbRoot(
                thumbRoot,
                layoutProfile.FolderName
            );
            return layoutProfile.BuildOutPath(resolvedRoot);
        }

        // thumbFolder が「既知のレイアウト名フォルダ」直指定のまま来た場合は、
        // 上位側の BuildOutPath 側で二重結合しないよう吸収する。
        private static string ResolveCurrentCompatibleThumbRoot(
            string thumbRoot,
            string layoutFolderName
        )
        {
            if (string.IsNullOrWhiteSpace(thumbRoot))
            {
                return string.Empty;
            }

            string normalizedThumbRoot = thumbRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            );
            if (string.IsNullOrWhiteSpace(normalizedThumbRoot))
            {
                return string.Empty;
            }

            string candidateFolderName = Path.GetFileName(normalizedThumbRoot);
            if (
                !string.IsNullOrWhiteSpace(candidateFolderName)
                && KnownThumbnailLayoutFolderNames.Contains(candidateFolderName)
                && string.Equals(
                    candidateFolderName,
                    layoutFolderName,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                string parentFolder = Path.GetDirectoryName(normalizedThumbRoot);
                if (!string.IsNullOrWhiteSpace(parentFolder))
                {
                    return parentFolder;
                }
            }

            return normalizedThumbRoot;
        }

        // DB配置と個別設定から、実運用で使うサムネ根を一箇所で解決する。
        private static string ResolveRuntimeThumbnailRoot(
            string dbFullPath,
            string dbName,
            string thumbFolder,
            string defaultBaseDirectory = ""
        )
        {
            return ThumbRootResolver.ResolveRuntimeThumbRoot(
                dbFullPath,
                dbName,
                thumbFolder,
                defaultBaseDirectory
            );
        }

        private string ResolveCurrentThumbnailRoot()
        {
            return ResolveRuntimeThumbnailRoot(
                MainVM?.DbInfo?.DBFullPath ?? "",
                MainVM?.DbInfo?.DBName ?? "",
                MainVM?.DbInfo?.ThumbFolder ?? ""
            );
        }

        private string ResolveCurrentThumbnailOutPath(int tabIndex)
        {
            return ResolveThumbnailOutPath(
                tabIndex,
                MainVM?.DbInfo?.DBName ?? "",
                MainVM?.DbInfo?.ThumbFolder ?? ""
            );
        }

        private string BuildCurrentThumbnailPath(int tabIndex, string movieNameOrPath, string hash)
        {
            return ThumbnailPathResolver.BuildThumbnailPath(
                ResolveCurrentThumbnailOutPath(tabIndex),
                movieNameOrPath,
                hash
            );
        }
    }
}
