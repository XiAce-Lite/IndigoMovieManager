using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IndigoMovieManager.Properties;

namespace IndigoMovieManager.Converter
{
    /// <summary>
    /// ファイルロック無しで画像を読み込む。
    /// 同一ファイルの再評価時はキャッシュを返してI/Oを抑える。
    /// </summary>
    internal class NoLockImageConverter : IValueConverter
    {
        internal const int MinImageCacheEntries = 256;
        internal const int MaxImageCacheEntries = 4096;
        internal const int DefaultImageCacheEntries = 1024;
        private const int MinMetadataCacheEntries = 1024;
        private const int MaxMetadataCacheEntries = 16384;
        private static readonly TimeSpan MetadataCacheLifetime = TimeSpan.FromSeconds(5);
        private static readonly object CacheGate = new();
        private static readonly Dictionary<string, CacheEntry> Cache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly LinkedList<string> CacheLru = new();
        private static readonly Dictionary<string, MetadataCacheEntry> MetadataCache =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly LinkedList<string> MetadataCacheLru = new();
        private static int _imageCacheHitCount;
        private static int _imageCacheMissCount;
        private static int _metadataCacheHitCount;
        private static int _metadataCacheMissCount;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return ConvertWithOptions(value as string, ParseOptions(parameter));
        }

        public object ConvertBack(
            object value,
            Type targetType,
            object parameter,
            CultureInfo culture
        )
        {
            throw new NotImplementedException();
        }

        internal static object ConvertFilePath(
            string filePath,
            bool isExists,
            int decodePixelHeight = 0
        )
        {
            return ConvertWithOptions(
                filePath,
                new ConvertOptions
                {
                    UseGray = !isExists,
                    DecodePixelHeight = decodePixelHeight,
                }
            );
        }

        internal static int ResolveDecodePixelHeight(object parameter)
        {
            return TryReadDecodePixelHeight(parameter, out int decodePixelHeight)
                ? decodePixelHeight
                : 0;
        }

        private static object ConvertWithOptions(string filePath, ConvertOptions options)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return Binding.DoNothing;
            }

            try
            {
                string fullPath = Path.GetFullPath(filePath);
                if (!TryGetFileStamp(fullPath, out FileStamp fileStamp))
                {
                    return Binding.DoNothing;
                }

                string cacheKey = BuildCacheKey(
                    fullPath,
                    options.UseGray,
                    options.DecodePixelHeight
                );

                if (
                    TryGetCachedImage(
                        cacheKey,
                        fileStamp.LastWriteTicks,
                        fileStamp.FileLength,
                        out ImageSource cachedImage
                    )
                )
                {
                    return cachedImage;
                }

                BitmapSource bitmap = LoadBitmapNoLock(fullPath, options.DecodePixelHeight);
                if (bitmap is null)
                {
                    return Binding.DoNothing;
                }

                BitmapSource result = options.UseGray ? ConvertToGray(bitmap) : bitmap;
                result.Freeze();
                StoreCachedImage(cacheKey, fileStamp.LastWriteTicks, fileStamp.FileLength, result);
                return result;
            }
            catch (Exception)
            {
                return Binding.DoNothing;
            }
        }

        private static ConvertOptions ParseOptions(object parameter)
        {
            ConvertOptions options = new();
            if (parameter is bool exists)
            {
                options.UseGray = !exists;
                return options;
            }

            options.DecodePixelHeight = ResolveDecodePixelHeight(parameter);

            return options;
        }

        private static bool TryReadDecodePixelHeight(object parameter, out int decodePixelHeight)
        {
            decodePixelHeight = 0;
            if (parameter == null)
            {
                return false;
            }

            if (parameter is int intValue && intValue > 0)
            {
                decodePixelHeight = intValue;
                return true;
            }

            if (parameter is long longValue && longValue > 0 && longValue <= int.MaxValue)
            {
                decodePixelHeight = (int)longValue;
                return true;
            }

            if (parameter is double doubleValue && doubleValue > 0)
            {
                decodePixelHeight = (int)Math.Round(doubleValue);
                return decodePixelHeight > 0;
            }

            if (parameter is string text && !string.IsNullOrWhiteSpace(text))
            {
                string trimmed = text.Trim();
                if (int.TryParse(trimmed, out int parsed) && parsed > 0)
                {
                    decodePixelHeight = parsed;
                    return true;
                }
            }

            return false;
        }

        private static string BuildCacheKey(string fullPath, bool useGray, int decodePixelHeight)
        {
            string colorKey = useGray ? "G" : "C";
            int safeDecodePixelHeight = decodePixelHeight > 0 ? decodePixelHeight : 0;
            return $"{fullPath}|{colorKey}|H{safeDecodePixelHeight}";
        }

        internal static int ClampImageCacheEntryLimit(int value)
        {
            if (value < MinImageCacheEntries)
            {
                return MinImageCacheEntries;
            }
            if (value > MaxImageCacheEntries)
            {
                return MaxImageCacheEntries;
            }
            return value;
        }

        internal static void InvalidateFilePath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            try
            {
                string fullPath = Path.GetFullPath(filePath);
                lock (CacheGate)
                {
                    if (MetadataCache.TryGetValue(fullPath, out MetadataCacheEntry metadataEntry))
                    {
                        RemoveMetadataCacheEntry(fullPath, metadataEntry);
                    }

                    List<string> cacheKeysToRemove = [];
                    foreach (KeyValuePair<string, CacheEntry> pair in Cache)
                    {
                        if (
                            pair.Key.StartsWith(
                                $"{fullPath}|",
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        {
                            cacheKeysToRemove.Add(pair.Key);
                        }
                    }

                    foreach (string cacheKey in cacheKeysToRemove)
                    {
                        if (Cache.TryGetValue(cacheKey, out CacheEntry cacheEntry))
                        {
                            RemoveCacheEntry(cacheKey, cacheEntry);
                        }
                    }
                }
            }
            catch
            {
                // 画像更新の補助処理なので、無効化失敗で本流を止めない。
            }
        }

        private static int ResolveConfiguredImageCacheEntryLimit()
        {
            return ClampImageCacheEntryLimit(Settings.Default.UpperTabImageCacheMaxEntries);
        }

        private static int ResolveMetadataCacheEntryLimit()
        {
            int candidate = ResolveConfiguredImageCacheEntryLimit() * 2;
            if (candidate < MinMetadataCacheEntries)
            {
                return MinMetadataCacheEntries;
            }
            if (candidate > MaxMetadataCacheEntries)
            {
                return MaxMetadataCacheEntries;
            }
            return candidate;
        }

        // 往復スクロール中は短時間だけ file metadata を保持して stat を減らす。
        private static bool TryGetFileStamp(string fullPath, out FileStamp fileStamp)
        {
            long nowUtcTicks = DateTime.UtcNow.Ticks;
            lock (CacheGate)
            {
                TrimMetadataCacheLocked(nowUtcTicks);
                if (MetadataCache.TryGetValue(fullPath, out MetadataCacheEntry cachedEntry))
                {
                    if (cachedEntry.ExpiresAtUtcTicks >= nowUtcTicks)
                    {
                        NoteCacheMetric(ref _metadataCacheHitCount, "image metadata cache hit");
                        TouchMetadataCacheEntry(cachedEntry);
                        if (cachedEntry.Exists)
                        {
                            fileStamp = new FileStamp(
                                cachedEntry.LastWriteTicks,
                                cachedEntry.FileLength
                            );
                            return true;
                        }

                        fileStamp = default;
                        return false;
                    }

                    RemoveMetadataCacheEntry(fullPath, cachedEntry);
                }
            }

            NoteCacheMetric(ref _metadataCacheMissCount, "image metadata cache miss");
            FileInfo fileInfo = new(fullPath);
            bool exists = fileInfo.Exists;
            FileStamp nextStamp = exists
                ? new FileStamp(fileInfo.LastWriteTimeUtc.Ticks, fileInfo.Length)
                : default;
            StoreMetadataCacheEntry(
                fullPath,
                exists,
                nextStamp.LastWriteTicks,
                nextStamp.FileLength,
                nowUtcTicks + MetadataCacheLifetime.Ticks
            );
            fileStamp = nextStamp;
            return exists;
        }

        private static bool TryGetCachedImage(string cacheKey, long lastWriteTicks, long fileLength, out ImageSource image)
        {
            lock (CacheGate)
            {
                TrimImageCacheLocked();
                if (Cache.TryGetValue(cacheKey, out CacheEntry entry))
                {
                    if (entry.LastWriteTicks == lastWriteTicks && entry.FileLength == fileLength)
                    {
                        NoteCacheMetric(ref _imageCacheHitCount, "image cache hit");
                        TouchCacheEntry(entry);
                        image = entry.Image;
                        return true;
                    }

                    RemoveCacheEntry(cacheKey, entry);
                }
            }

            NoteCacheMetric(ref _imageCacheMissCount, "image cache miss");
            image = null;
            return false;
        }

        private static void StoreCachedImage(string cacheKey, long lastWriteTicks, long fileLength, ImageSource image)
        {
            lock (CacheGate)
            {
                TrimImageCacheLocked();
                if (Cache.TryGetValue(cacheKey, out CacheEntry existing))
                {
                    RemoveCacheEntry(cacheKey, existing);
                }

                LinkedListNode<string> node = CacheLru.AddFirst(cacheKey);
                Cache[cacheKey] = new CacheEntry(lastWriteTicks, fileLength, image, node);
                TrimImageCacheLocked();
            }
        }

        private static void TrimImageCacheLocked()
        {
            int maxCacheEntries = ResolveConfiguredImageCacheEntryLimit();
            while (Cache.Count > maxCacheEntries)
            {
                LinkedListNode<string> staleNode = CacheLru.Last;
                if (staleNode is null)
                {
                    break;
                }

                string staleKey = staleNode.Value;
                if (Cache.TryGetValue(staleKey, out CacheEntry staleEntry))
                {
                    RemoveCacheEntry(staleKey, staleEntry);
                }
                else
                {
                    CacheLru.Remove(staleNode);
                }
            }
        }

        private static void TouchCacheEntry(CacheEntry entry)
        {
            CacheLru.Remove(entry.Node);
            CacheLru.AddFirst(entry.Node);
        }

        private static void RemoveCacheEntry(string cacheKey, CacheEntry entry)
        {
            Cache.Remove(cacheKey);
            CacheLru.Remove(entry.Node);
        }

        private static void StoreMetadataCacheEntry(
            string fullPath,
            bool exists,
            long lastWriteTicks,
            long fileLength,
            long expiresAtUtcTicks
        )
        {
            lock (CacheGate)
            {
                TrimMetadataCacheLocked(DateTime.UtcNow.Ticks);
                if (MetadataCache.TryGetValue(fullPath, out MetadataCacheEntry existing))
                {
                    RemoveMetadataCacheEntry(fullPath, existing);
                }

                LinkedListNode<string> node = MetadataCacheLru.AddFirst(fullPath);
                MetadataCache[fullPath] = new MetadataCacheEntry(
                    exists,
                    lastWriteTicks,
                    fileLength,
                    expiresAtUtcTicks,
                    node
                );
                TrimMetadataCacheLocked(DateTime.UtcNow.Ticks);
            }
        }

        private static void TrimMetadataCacheLocked(long nowUtcTicks)
        {
            while (MetadataCacheLru.Last is LinkedListNode<string> tailNode)
            {
                string tailKey = tailNode.Value;
                if (
                    MetadataCache.TryGetValue(tailKey, out MetadataCacheEntry tailEntry)
                    && tailEntry.ExpiresAtUtcTicks >= nowUtcTicks
                )
                {
                    break;
                }

                if (MetadataCache.TryGetValue(tailKey, out MetadataCacheEntry expiredEntry))
                {
                    RemoveMetadataCacheEntry(tailKey, expiredEntry);
                }
                else
                {
                    MetadataCacheLru.Remove(tailNode);
                }
            }

            int metadataCacheLimit = ResolveMetadataCacheEntryLimit();
            while (MetadataCache.Count > metadataCacheLimit)
            {
                LinkedListNode<string> staleNode = MetadataCacheLru.Last;
                if (staleNode is null)
                {
                    break;
                }

                string staleKey = staleNode.Value;
                if (MetadataCache.TryGetValue(staleKey, out MetadataCacheEntry staleEntry))
                {
                    RemoveMetadataCacheEntry(staleKey, staleEntry);
                }
                else
                {
                    MetadataCacheLru.Remove(staleNode);
                }
            }
        }

        private static void TouchMetadataCacheEntry(MetadataCacheEntry entry)
        {
            MetadataCacheLru.Remove(entry.Node);
            MetadataCacheLru.AddFirst(entry.Node);
        }

        private static void RemoveMetadataCacheEntry(string fullPath, MetadataCacheEntry entry)
        {
            MetadataCache.Remove(fullPath);
            MetadataCacheLru.Remove(entry.Node);
        }

        // 毎回ログすると熱いので、一定件数ごとに集計だけ出す。
        private static void NoteCacheMetric(ref int counter, string metricName)
        {
            int count = Interlocked.Increment(ref counter);
            if (count % 256 != 0)
            {
                return;
            }

            DebugRuntimeLog.Write(
                "ui-tempo",
                $"{metricName}: count={count} image_limit={ResolveConfiguredImageCacheEntryLimit()}"
            );
        }

        private static BitmapSource LoadBitmapNoLock(string filePath, int decodePixelHeight)
        {
            const int maxAttempts = 3;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    using FileStream fs = new(
                        filePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete
                    );
                    if (decodePixelHeight > 0)
                    {
                        BitmapImage bitmapImage = new();
                        bitmapImage.BeginInit();
                        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                        bitmapImage.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                        bitmapImage.DecodePixelHeight = decodePixelHeight;
                        bitmapImage.StreamSource = fs;
                        bitmapImage.EndInit();
                        if (bitmapImage.PixelWidth < 1 || bitmapImage.PixelHeight < 1)
                        {
                            return null;
                        }

                        bitmapImage.Freeze();
                        return bitmapImage;
                    }

                    BitmapDecoder decoder = BitmapDecoder.Create(
                        fs,
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad
                    );
                    if (decoder.Frames.Count < 1)
                    {
                        return null;
                    }

                    BitmapFrame frame = decoder.Frames[0];
                    frame.Freeze();
                    return frame;
                }
                catch (IOException) when (attempt < maxAttempts - 1)
                {
                    Thread.Sleep(20);
                }
                catch (UnauthorizedAccessException) when (attempt < maxAttempts - 1)
                {
                    Thread.Sleep(20);
                }
            }

            return null;
        }

        private static BitmapSource ConvertToGray(BitmapSource source)
        {
            FormatConvertedBitmap gray = new();
            gray.BeginInit();
            gray.Source = source;
            gray.DestinationFormat = PixelFormats.Gray8;
            gray.EndInit();
            gray.Freeze();
            return gray;
        }

        private sealed class CacheEntry
        {
            public CacheEntry(long lastWriteTicks, long fileLength, ImageSource image, LinkedListNode<string> node)
            {
                LastWriteTicks = lastWriteTicks;
                FileLength = fileLength;
                Image = image;
                Node = node;
            }

            public long LastWriteTicks { get; }
            public long FileLength { get; }
            public ImageSource Image { get; }
            public LinkedListNode<string> Node { get; }
        }

        private readonly record struct FileStamp(long LastWriteTicks, long FileLength);

        private sealed class MetadataCacheEntry
        {
            public MetadataCacheEntry(
                bool exists,
                long lastWriteTicks,
                long fileLength,
                long expiresAtUtcTicks,
                LinkedListNode<string> node
            )
            {
                Exists = exists;
                LastWriteTicks = lastWriteTicks;
                FileLength = fileLength;
                ExpiresAtUtcTicks = expiresAtUtcTicks;
                Node = node;
            }

            public bool Exists { get; }
            public long LastWriteTicks { get; }
            public long FileLength { get; }
            public long ExpiresAtUtcTicks { get; }
            public LinkedListNode<string> Node { get; }
        }

        private sealed class ConvertOptions
        {
            public bool UseGray { get; set; }
            public int DecodePixelHeight { get; set; }
        }
    }
}
