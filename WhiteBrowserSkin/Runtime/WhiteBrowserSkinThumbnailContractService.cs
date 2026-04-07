using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using IndigoMovieManager;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager.Skin.Runtime
{
    /// <summary>
    /// WhiteBrowser 互換スキンへ渡すサムネ契約を、解決済み DTO として組み立てる。
    /// 表示そのものは持たず、サムネ実体・revision・URL・寸法の正本供給に徹する。
    /// </summary>
    public sealed class WhiteBrowserSkinThumbnailContractService
    {
        public WhiteBrowserSkinThumbnailContractDto Create(
            MovieRecords movie,
            WhiteBrowserSkinThumbnailResolveContext context
        )
        {
            ArgumentNullException.ThrowIfNull(movie);

            WhiteBrowserSkinThumbnailResolveContext normalizedContext = context ?? new();
            string dbIdentity = BuildDbIdentity(normalizedContext.DbFullPath);
            string recordKey = BuildRecordKey(dbIdentity, movie.Movie_Id);
            string resolvedThumbPath = ResolveThumbPath(movie, normalizedContext);
            string sourceKind = ResolveSourceKind(movie, resolvedThumbPath);
            WhiteBrowserSkinThumbnailSizeInfo sizeInfo = ResolveSizeInfo(
                resolvedThumbPath,
                sourceKind
            );
            string thumbRevision = BuildThumbRevision(resolvedThumbPath, sourceKind);

            return new WhiteBrowserSkinThumbnailContractDto
            {
                DbIdentity = dbIdentity,
                MovieId = movie.Movie_Id,
                RecordKey = recordKey,
                MovieName = movie.Movie_Name ?? "",
                MoviePath = movie.Movie_Path ?? "",
                ThumbPath = resolvedThumbPath,
                ThumbUrl = ResolveThumbUrl(
                    resolvedThumbPath,
                    normalizedContext.ManagedThumbnailRootPath,
                    normalizedContext.ThumbUrlResolver,
                    thumbRevision
                ),
                ThumbRevision = thumbRevision,
                ThumbSourceKind = sourceKind,
                ThumbNaturalWidth = sizeInfo.NaturalWidth,
                ThumbNaturalHeight = sizeInfo.NaturalHeight,
                ThumbSheetColumns = sizeInfo.SheetColumns,
                ThumbSheetRows = sizeInfo.SheetRows,
                MovieSize = movie.Movie_Size,
                Length = movie.Movie_Length ?? "",
                Exists = movie.IsExists,
                Selected =
                    normalizedContext.SelectedMovieId.HasValue
                    && normalizedContext.SelectedMovieId.Value == movie.Movie_Id,
            };
        }

        public IReadOnlyList<WhiteBrowserSkinThumbnailContractDto> CreateRange(
            IEnumerable<MovieRecords> movies,
            WhiteBrowserSkinThumbnailResolveContext context
        )
        {
            if (movies == null)
            {
                return [];
            }

            List<WhiteBrowserSkinThumbnailContractDto> items = [];
            foreach (MovieRecords movie in movies)
            {
                if (movie == null)
                {
                    continue;
                }

                items.Add(Create(movie, context));
            }

            return items;
        }

        public static string BuildDbIdentity(string dbFullPath)
        {
            return WhiteBrowserSkinDbIdentity.Build(dbFullPath);
        }

        public static string BuildRecordKey(string dbIdentity, long movieId)
        {
            return WhiteBrowserSkinDbIdentity.BuildRecordKey(dbIdentity, movieId);
        }

        public static string BuildThumbRevision(string resolvedThumbPath, string sourceKind)
        {
            string normalizedThumbPath = NormalizePath(resolvedThumbPath);
            if (string.IsNullOrWhiteSpace(normalizedThumbPath))
            {
                return "0";
            }

            try
            {
                FileInfo fileInfo = new(normalizedThumbPath);
                if (!fileInfo.Exists)
                {
                    return "0";
                }

                string fingerprint =
                    $"{sourceKind ?? ""}|{normalizedThumbPath}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}";
                byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint));
                return Convert.ToHexString(hash).ToLowerInvariant();
            }
            catch
            {
                return "0";
            }
        }

        private static string ResolveThumbPath(
            MovieRecords movie,
            WhiteBrowserSkinThumbnailResolveContext context
        )
        {
            string candidatePath = GetThumbPathForTab(movie, context.DisplayTabIndex);
            if (IsUsablePath(candidatePath))
            {
                return candidatePath;
            }

            if (
                ThumbnailSourceImagePathResolver.TryResolveSameNameThumbnailSourceImagePath(
                    movie.Movie_Path,
                    out string sourceImagePath
                )
            )
            {
                return sourceImagePath;
            }

            return ResolveMissingPlaceholderPath(context.DisplayTabIndex);
        }

        private static string ResolveSourceKind(MovieRecords movie, string resolvedThumbPath)
        {
            if (string.IsNullOrWhiteSpace(resolvedThumbPath))
            {
                return WhiteBrowserSkinThumbnailSourceKinds.MissingFilePlaceholder;
            }

            if (IsMissingMoviePlaceholderPath(resolvedThumbPath) || !Path.Exists(resolvedThumbPath))
            {
                return WhiteBrowserSkinThumbnailSourceKinds.MissingFilePlaceholder;
            }

            if (
                ThumbnailErrorPlaceholderHelper.IsPlaceholderPath(resolvedThumbPath)
                || ThumbnailPathResolver.IsErrorMarker(resolvedThumbPath)
            )
            {
                return WhiteBrowserSkinThumbnailSourceKinds.ErrorPlaceholder;
            }

            if (
                ThumbnailSourceImagePathResolver.TryResolveSameNameThumbnailSourceImagePath(
                    movie.Movie_Path,
                    out string sourceImagePath
                )
                && PathsEqual(resolvedThumbPath, sourceImagePath)
            )
            {
                return WhiteBrowserSkinThumbnailSourceKinds.SourceImageDirect;
            }

            if (ThumbnailSourceImageImportMarkerHelper.HasMarker(resolvedThumbPath))
            {
                return WhiteBrowserSkinThumbnailSourceKinds.SourceImageImported;
            }

            return WhiteBrowserSkinThumbnailSourceKinds.ManagedThumbnail;
        }

        private static WhiteBrowserSkinThumbnailSizeInfo ResolveSizeInfo(
            string resolvedThumbPath,
            string sourceKind
        )
        {
            if (string.IsNullOrWhiteSpace(resolvedThumbPath))
            {
                return new WhiteBrowserSkinThumbnailSizeInfo(0, 0, 1, 1);
            }

            if (
                string.Equals(
                    sourceKind,
                    WhiteBrowserSkinThumbnailSourceKinds.ManagedThumbnail,
                    StringComparison.Ordinal
                )
                || string.Equals(
                    sourceKind,
                    WhiteBrowserSkinThumbnailSourceKinds.SourceImageImported,
                    StringComparison.Ordinal
                )
            )
            {
                ThumbInfo thumbInfo = new();
                thumbInfo.GetThumbInfo(resolvedThumbPath);
                if (thumbInfo.IsThumbnail)
                {
                    int naturalWidth = thumbInfo.TotalWidth;
                    int naturalHeight = thumbInfo.TotalHeight;
                    if (TryReadImageSize(resolvedThumbPath, out int actualWidth, out int actualHeight))
                    {
                        naturalWidth = actualWidth;
                        naturalHeight = actualHeight;
                    }

                    return new WhiteBrowserSkinThumbnailSizeInfo(
                        naturalWidth,
                        naturalHeight,
                        Math.Max(1, thumbInfo.ThumbColumns),
                        Math.Max(1, thumbInfo.ThumbRows)
                    );
                }
            }

            if (TryReadImageSize(resolvedThumbPath, out int width, out int height))
            {
                return new WhiteBrowserSkinThumbnailSizeInfo(width, height, 1, 1);
            }

            return new WhiteBrowserSkinThumbnailSizeInfo(0, 0, 1, 1);
        }

        private static string ResolveThumbUrl(
            string resolvedThumbPath,
            string managedThumbnailRootPath,
            Func<string, string> thumbUrlResolver,
            string thumbRevision
        )
        {
            if (string.IsNullOrWhiteSpace(resolvedThumbPath))
            {
                return "";
            }

            string customUrl = thumbUrlResolver?.Invoke(resolvedThumbPath) ?? "";
            if (!string.IsNullOrWhiteSpace(customUrl))
            {
                return AppendRevisionQuery(customUrl, thumbRevision);
            }

            return WhiteBrowserSkinThumbnailUrlCodec.BuildThumbUrl(
                resolvedThumbPath,
                managedThumbnailRootPath,
                thumbRevision
            );
        }

        private static string GetThumbPathForTab(MovieRecords movie, int displayTabIndex)
        {
            return displayTabIndex switch
            {
                0 => movie?.ThumbPathSmall ?? "",
                1 => movie?.ThumbPathBig ?? "",
                2 => movie?.ThumbPathGrid ?? "",
                3 => movie?.ThumbPathList ?? "",
                4 => movie?.ThumbPathBig10 ?? "",
                99 => movie?.ThumbDetail ?? "",
                _ => movie?.ThumbDetail ?? "",
            };
        }

        private static string ResolveMissingPlaceholderPath(int displayTabIndex)
        {
            int hostTabIndex = displayTabIndex switch
            {
                1 => 1,
                2 => 2,
                3 => 3,
                4 => 4,
                99 => 99,
                _ => 0,
            };

            return DefaultThumbnailCreationHostRuntime.Instance.ResolveMissingMoviePlaceholderPath(
                hostTabIndex
            );
        }

        private static bool TryReadImageSize(string path, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (string.IsNullOrWhiteSpace(path) || !Path.Exists(path))
            {
                return false;
            }

            try
            {
                using FileStream stream = new(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete
                );
                using Image image = Image.FromStream(
                    stream,
                    useEmbeddedColorManagement: false,
                    validateImageData: true
                );
                width = image.Width;
                height = image.Height;
                return width > 0 && height > 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool PathsEqual(string left, string right)
        {
            string normalizedLeft = NormalizePath(left);
            string normalizedRight = NormalizePath(right);
            if (string.IsNullOrWhiteSpace(normalizedLeft) || string.IsNullOrWhiteSpace(normalizedRight))
            {
                return false;
            }

            return string.Equals(
                normalizedLeft,
                normalizedRight,
                StringComparison.OrdinalIgnoreCase
            );
        }

        private static bool IsUsablePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                FileInfo fileInfo = new(path);
                return fileInfo.Exists && fileInfo.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsMissingMoviePlaceholderPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string fileName = Path.GetFileName(path).Trim();
            return fileName.StartsWith("noFile", StringComparison.OrdinalIgnoreCase)
                || fileName.StartsWith("nofile", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "";
            }

            try
            {
                return Path.GetFullPath(path.Trim().Trim('"'))
                    .Replace('/', '\\')
                    .ToLowerInvariant();
            }
            catch
            {
                return path.Trim().Trim('"').Replace('/', '\\').ToLowerInvariant();
            }
        }

        private static string AppendRevisionQuery(string thumbUrl, string thumbRevision)
        {
            if (string.IsNullOrWhiteSpace(thumbUrl) || string.IsNullOrWhiteSpace(thumbRevision))
            {
                return thumbUrl ?? "";
            }

            if (thumbUrl.Contains("rev=", StringComparison.OrdinalIgnoreCase))
            {
                return thumbUrl;
            }

            string separator = thumbUrl.Contains('?') ? "&" : "?";
            return $"{thumbUrl}{separator}rev={Uri.EscapeDataString(thumbRevision)}";
        }

        private static bool TryBuildManagedRelativePath(string thumbPath, string managedRootPath)
        {
            string normalizedThumbPath = NormalizePath(thumbPath);
            string normalizedRoot = NormalizePath(managedRootPath);
            if (
                string.IsNullOrWhiteSpace(normalizedThumbPath)
                || string.IsNullOrWhiteSpace(normalizedRoot)
            )
            {
                return false;
            }

            string rootWithSeparator = normalizedRoot.EndsWith('\\')
                ? normalizedRoot
                : $"{normalizedRoot}\\";
            return normalizedThumbPath.StartsWith(
                rootWithSeparator,
                StringComparison.OrdinalIgnoreCase
            );
        }

        private readonly record struct WhiteBrowserSkinThumbnailSizeInfo(
            int NaturalWidth,
            int NaturalHeight,
            int SheetColumns,
            int SheetRows
        );
    }
}
