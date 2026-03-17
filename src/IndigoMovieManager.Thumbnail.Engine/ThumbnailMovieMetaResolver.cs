using System.Collections.Concurrent;
using System.IO;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 生成前に必要な hash / duration / codec / DRM 事前判定をまとめて解決する。
    /// </summary>
    internal sealed class ThumbnailMovieMetaResolver
    {
        private static readonly ConcurrentDictionary<string, CachedMovieMeta> MovieMetaCache = new(
            StringComparer.OrdinalIgnoreCase
        );
        private const int MovieMetaCacheMaxCount = 10000;
        private const int AsfDrmScanMaxBytes = 64 * 1024;
        private static readonly byte[] AsfContentEncryptionObjectGuid =
        [
            0xFB,
            0xB3,
            0x11,
            0x22,
            0x23,
            0xBD,
            0xD2,
            0x11,
            0xB4,
            0xB7,
            0x00,
            0xA0,
            0xC9,
            0x55,
            0xFC,
            0x6E,
        ];

        private readonly IVideoMetadataProvider videoMetadataProvider;

        public ThumbnailMovieMetaResolver(IVideoMetadataProvider videoMetadataProvider)
        {
            this.videoMetadataProvider =
                videoMetadataProvider
                ?? throw new ArgumentNullException(nameof(videoMetadataProvider));
        }

        public static string ResolveThumbnailOutPath(
            ThumbnailLayoutProfile layoutProfile,
            string dbName,
            string thumbFolder
        )
        {
            string thumbRoot = string.IsNullOrWhiteSpace(thumbFolder)
                ? ThumbRootResolver.GetDefaultThumbRoot(dbName)
                : thumbFolder;
            return layoutProfile.BuildOutPath(thumbRoot);
        }

        public CachedMovieMeta GetCachedMovieMeta(
            string movieFullPath,
            string hashHint,
            out string cacheKey
        )
        {
            cacheKey = BuildMovieMetaCacheKey(movieFullPath);
            return MovieMetaCache.GetOrAdd(
                cacheKey,
                _ =>
                {
                    string hash = ResolveMovieHash(movieFullPath, hashHint);
                    bool isDrmSuspected = false;
                    string drmDetail = "";
                    if (IsAsfFamilyFile(movieFullPath))
                    {
                        isDrmSuspected = TryDetectAsfDrmProtected(movieFullPath, out drmDetail);
                    }

                    return new CachedMovieMeta(hash, null, isDrmSuspected, drmDetail);
                }
            );
        }

        public double? ResolveDurationSec(
            string sourceMovieFullPath,
            string cacheKey,
            CachedMovieMeta currentMeta
        )
        {
            if (currentMeta?.DurationSec.HasValue == true && currentMeta.DurationSec.Value > 0)
            {
                return currentMeta.DurationSec.Value;
            }

            double? durationSec = null;
            if (
                videoMetadataProvider.TryGetDurationSec(
                    sourceMovieFullPath,
                    out double providedDurationSec
                )
                && providedDurationSec > 0
            )
            {
                durationSec = providedDurationSec;
            }
            else
            {
                durationSec = ThumbnailShellDurationResolver.TryGetDurationSec(sourceMovieFullPath);
            }

            CacheDuration(cacheKey, currentMeta, durationSec);
            return durationSec;
        }

        public long ResolveFileSizeBytes(string sourceMovieFullPath, long fileSizeHint)
        {
            if (fileSizeHint > 0)
            {
                return fileSizeHint;
            }

            try
            {
                return Math.Max(0, new FileInfo(sourceMovieFullPath).Length);
            }
            catch
            {
                return 0;
            }
        }

        public string ResolveVideoCodec(string sourceMovieFullPath)
        {
            if (
                videoMetadataProvider.TryGetVideoCodec(
                    sourceMovieFullPath,
                    out string providedVideoCodec
                )
                && !string.IsNullOrWhiteSpace(providedVideoCodec)
            )
            {
                return providedVideoCodec;
            }

            return "";
        }

        public void CacheDuration(string cacheKey, CachedMovieMeta currentMeta, double? durationSec)
        {
            if (!durationSec.HasValue || durationSec.Value <= 0)
            {
                return;
            }

            string hash = currentMeta?.Hash ?? "";
            bool isDrmSuspected = currentMeta?.IsDrmSuspected ?? false;
            string drmDetail = currentMeta?.DrmDetail ?? "";
            MovieMetaCache[cacheKey] = new CachedMovieMeta(
                hash,
                durationSec,
                isDrmSuspected,
                drmDetail
            );
            if (MovieMetaCache.Count > MovieMetaCacheMaxCount)
            {
                MovieMetaCache.Clear();
            }
        }

        private static string ResolveMovieHash(string movieFullPath, string hashHint)
        {
            if (!string.IsNullOrWhiteSpace(hashHint))
            {
                return hashHint;
            }

            return MovieHashCalculator.GetHashCrc32(movieFullPath);
        }

        private static string BuildMovieMetaCacheKey(string movieFullPath)
        {
            try
            {
                FileInfo fi = new(movieFullPath);
                if (!fi.Exists)
                {
                    return movieFullPath;
                }
                return $"{movieFullPath}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}";
            }
            catch
            {
                return movieFullPath;
            }
        }

        private static bool IsAsfFamilyFile(string movieFullPath)
        {
            if (string.IsNullOrWhiteSpace(movieFullPath))
            {
                return false;
            }

            string ext = Path.GetExtension(movieFullPath);
            return ext.Equals(".wmv", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".asf", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryDetectAsfDrmProtected(string movieFullPath, out string detail)
        {
            detail = "";
            if (!Path.Exists(movieFullPath))
            {
                detail = "file_not_found";
                return false;
            }

            try
            {
                using FileStream fs = new(
                    movieFullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite
                );
                int readLength = (int)Math.Min(AsfDrmScanMaxBytes, fs.Length);
                if (readLength < AsfContentEncryptionObjectGuid.Length)
                {
                    detail = "header_too_short";
                    return false;
                }

                byte[] buffer = new byte[readLength];
                int totalRead = 0;
                while (totalRead < readLength)
                {
                    int read = fs.Read(buffer, totalRead, readLength - totalRead);
                    if (read <= 0)
                    {
                        break;
                    }
                    totalRead += read;
                }

                int hitIndex = IndexOfBytes(
                    buffer,
                    totalRead,
                    AsfContentEncryptionObjectGuid
                );
                if (hitIndex >= 0)
                {
                    detail = $"drm_guid_found_offset={hitIndex}";
                    return true;
                }

                detail = "drm_guid_not_found";
                return false;
            }
            catch (Exception ex)
            {
                detail = $"scan_error:{ex.GetType().Name}";
                return false;
            }
        }

        private static int IndexOfBytes(byte[] source, int sourceLength, byte[] pattern)
        {
            if (
                source == null
                || pattern == null
                || sourceLength < pattern.Length
                || pattern.Length < 1
            )
            {
                return -1;
            }

            int last = sourceLength - pattern.Length;
            for (int i = 0; i <= last; i++)
            {
                bool matched = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (source[i + j] != pattern[j])
                    {
                        matched = false;
                        break;
                    }
                }

                if (matched)
                {
                    return i;
                }
            }

            return -1;
        }
    }

    internal sealed class CachedMovieMeta
    {
        public CachedMovieMeta(
            string hash,
            double? durationSec,
            bool isDrmSuspected,
            string drmDetail
        )
        {
            Hash = hash ?? "";
            DurationSec = durationSec;
            IsDrmSuspected = isDrmSuspected;
            DrmDetail = drmDetail ?? "";
        }

        public string Hash { get; }
        public double? DurationSec { get; }
        public bool IsDrmSuspected { get; }
        public string DrmDetail { get; }
    }
}
