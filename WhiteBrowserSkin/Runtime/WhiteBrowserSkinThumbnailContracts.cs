using System;
using System.IO;
using System.Text;

namespace IndigoMovieManager.Skin.Runtime
{
    /// <summary>
    /// WebView2 契約で使う source kind 文字列を一箇所へ寄せる。
    /// JSON へそのまま流すので、enum ではなく固定文字列で扱う。
    /// </summary>
    public static class WhiteBrowserSkinThumbnailSourceKinds
    {
        public const string ManagedThumbnail = "managed-thumbnail";
        public const string SourceImageDirect = "source-image-direct";
        public const string SourceImageImported = "source-image-imported";
        public const string ErrorPlaceholder = "error-placeholder";
        public const string MissingFilePlaceholder = "missing-file-placeholder";
    }

    /// <summary>
    /// managed thumb 配下と external / placeholder ファイルの URL 変換規約を固定する。
    /// 実際の配信は RuntimeBridge 側が受け持ち、ここは thum.local へ寄せるだけにする。
    /// </summary>
    public static class WhiteBrowserSkinThumbnailUrlCodec
    {
        public const string ExternalRoutePrefix = "__external";

        public static string BuildThumbUrl(
            string thumbPath,
            string managedThumbnailRootPath,
            string thumbRevision
        )
        {
            string normalizedThumbPath = NormalizePath(thumbPath);
            if (string.IsNullOrWhiteSpace(normalizedThumbPath))
            {
                return "";
            }

            string relativePath = TryBuildManagedRelativePath(
                normalizedThumbPath,
                managedThumbnailRootPath
            );
            string url = string.IsNullOrWhiteSpace(relativePath)
                ? BuildExternalUrl(normalizedThumbPath)
                : BuildVirtualHostUrl(relativePath);
            return AppendRevisionQuery(url, thumbRevision);
        }

        public static bool TryResolveThumbPath(
            string thumbUrl,
            string managedThumbnailRootPath,
            out string thumbPath
        )
        {
            thumbPath = "";
            if (!Uri.TryCreate(thumbUrl, UriKind.Absolute, out Uri uri))
            {
                return false;
            }

            if (
                !string.Equals(
                    uri.Host,
                    WhiteBrowserSkinHostPaths.ThumbnailVirtualHostName,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return false;
            }

            string relativePath = uri.AbsolutePath.Trim('/');
            if (relativePath.StartsWith($"{ExternalRoutePrefix}/", StringComparison.OrdinalIgnoreCase))
            {
                string[] segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length < 2)
                {
                    return false;
                }

                thumbPath = DecodeExternalPathToken(segments[1]);
                return !string.IsNullOrWhiteSpace(thumbPath);
            }

            string normalizedRoot = NormalizePath(managedThumbnailRootPath);
            if (string.IsNullOrWhiteSpace(normalizedRoot))
            {
                return false;
            }

            string decodedRelativePath = Uri.UnescapeDataString(relativePath).Replace('/', '\\');
            if (
                string.IsNullOrWhiteSpace(decodedRelativePath)
                || Path.IsPathRooted(decodedRelativePath)
            )
            {
                return false;
            }

            string candidatePath = NormalizePath(Path.Combine(normalizedRoot, decodedRelativePath));
            if (!IsUnderManagedRoot(candidatePath, normalizedRoot))
            {
                return false;
            }

            thumbPath = candidatePath;
            return !string.IsNullOrWhiteSpace(thumbPath);
        }

        private static string TryBuildManagedRelativePath(string thumbPath, string managedRootPath)
        {
            string normalizedRoot = NormalizePath(managedRootPath);
            if (string.IsNullOrWhiteSpace(normalizedRoot))
            {
                return "";
            }

            string rootWithSeparator = EnsureTrailingSeparator(normalizedRoot);
            if (!thumbPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }

            return Path.GetRelativePath(normalizedRoot, thumbPath).Replace('\\', '/');
        }

        private static string BuildExternalUrl(string thumbPath)
        {
            string fileName = Path.GetFileName(thumbPath) ?? "thumb";
            string token = EncodeExternalPathToken(thumbPath);
            string relativePath =
                $"{ExternalRoutePrefix}/{token}/{Uri.EscapeDataString(fileName)}";
            return BuildVirtualHostUrl(relativePath);
        }

        private static string BuildVirtualHostUrl(string relativePath)
        {
            UriBuilder builder = new("https", WhiteBrowserSkinHostPaths.ThumbnailVirtualHostName)
            {
                Path = (relativePath ?? "").Replace('\\', '/'),
            };
            return builder.Uri.AbsoluteUri;
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

        private static string EncodeExternalPathToken(string path)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(path ?? "");
            return Convert
                .ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static string DecodeExternalPathToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return "";
            }

            string padded = token.Replace('-', '+').Replace('_', '/');
            int paddingLength = (4 - (padded.Length % 4)) % 4;
            padded = padded.PadRight(padded.Length + paddingLength, '=');
            try
            {
                byte[] bytes = Convert.FromBase64String(padded);
                return NormalizePath(Encoding.UTF8.GetString(bytes));
            }
            catch
            {
                return "";
            }
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "";
            }

            try
            {
                return Path.GetFullPath(path.Trim().Trim('"')).Replace('/', '\\');
            }
            catch
            {
                return path.Trim().Trim('"').Replace('/', '\\');
            }
        }

        private static string EnsureTrailingSeparator(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "";
            }

            return path.EndsWith('\\') ? path : $"{path}\\";
        }

        private static bool IsUnderManagedRoot(string candidatePath, string managedRootPath)
        {
            if (
                string.IsNullOrWhiteSpace(candidatePath)
                || string.IsNullOrWhiteSpace(managedRootPath)
            )
            {
                return false;
            }

            string rootWithSeparator = EnsureTrailingSeparator(managedRootPath);
            return candidatePath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// サムネイル契約 DTO を組み立てる時の入力をまとめる。
    /// 表示層の事情をここに持ち込まず、解決に必要な最小情報だけを受け取る。
    /// </summary>
    public sealed record WhiteBrowserSkinThumbnailResolveContext
    {
        public string DbFullPath { get; init; } = "";

        public string ManagedThumbnailRootPath { get; init; } = "";

        public int DisplayTabIndex { get; init; }

        public long? SelectedMovieId { get; init; }

        /// <summary>
        /// thumbUrl の最終変換を外から差し替えたい時の逃げ道。
        /// 未指定なら、この side で既定の thum.local URL 変換を使う。
        /// </summary>
        public Func<string, string> ThumbUrlResolver { get; init; }
    }

    /// <summary>
    /// WebView2 へ返す、解決済みのサムネイル契約 DTO。
    /// </summary>
    public sealed class WhiteBrowserSkinThumbnailContractDto
    {
        public string DbIdentity { get; init; } = "";

        public long MovieId { get; init; }

        public string RecordKey { get; init; } = "";

        public string MovieName { get; init; } = "";

        public string MoviePath { get; init; } = "";

        internal string ThumbPath { get; init; } = "";

        public string ThumbUrl { get; init; } = "";

        public string ThumbRevision { get; init; } = "";

        public string ThumbSourceKind { get; init; } = "";

        public int ThumbNaturalWidth { get; init; }

        public int ThumbNaturalHeight { get; init; }

        public int ThumbSheetColumns { get; init; } = 1;

        public int ThumbSheetRows { get; init; } = 1;

        public long MovieSize { get; init; }

        public string Length { get; init; } = "";

        public bool Exists { get; init; }

        public bool Selected { get; init; }
    }
}
