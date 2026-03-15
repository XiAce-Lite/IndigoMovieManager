using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IndigoMovieManager.Converter
{
    /// <summary>
    /// ファイルロック無しで画像を読み込む。
    /// 同一ファイルの再評価時はキャッシュを返してI/Oを抑える。
    /// </summary>
    internal class NoLockImageConverter : IValueConverter
    {
        private const int MaxCacheEntries = 256;
        private static readonly object CacheGate = new();
        private static readonly Dictionary<string, CacheEntry> Cache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly LinkedList<string> CacheLru = new();

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
                if (!Path.Exists(filePath))
                {
                    return Binding.DoNothing;
                }

                string fullPath = Path.GetFullPath(filePath);
                FileInfo fileInfo = new(fullPath);
                long lastWriteTicks = fileInfo.LastWriteTimeUtc.Ticks;
                long fileLength = fileInfo.Length;
                string cacheKey = BuildCacheKey(
                    fullPath,
                    options.UseGray,
                    options.DecodePixelHeight
                );

                if (TryGetCachedImage(cacheKey, lastWriteTicks, fileLength, out ImageSource cachedImage))
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
                StoreCachedImage(cacheKey, lastWriteTicks, fileLength, result);
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

        private static bool TryGetCachedImage(string cacheKey, long lastWriteTicks, long fileLength, out ImageSource image)
        {
            lock (CacheGate)
            {
                if (Cache.TryGetValue(cacheKey, out CacheEntry entry))
                {
                    if (entry.LastWriteTicks == lastWriteTicks && entry.FileLength == fileLength)
                    {
                        TouchCacheEntry(entry);
                        image = entry.Image;
                        return true;
                    }

                    RemoveCacheEntry(cacheKey, entry);
                }
            }

            image = null;
            return false;
        }

        private static void StoreCachedImage(string cacheKey, long lastWriteTicks, long fileLength, ImageSource image)
        {
            lock (CacheGate)
            {
                if (Cache.TryGetValue(cacheKey, out CacheEntry existing))
                {
                    RemoveCacheEntry(cacheKey, existing);
                }

                LinkedListNode<string> node = CacheLru.AddFirst(cacheKey);
                Cache[cacheKey] = new CacheEntry(lastWriteTicks, fileLength, image, node);

                while (Cache.Count > MaxCacheEntries)
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

        private sealed class ConvertOptions
        {
            public bool UseGray { get; set; }
            public int DecodePixelHeight { get; set; }
        }
    }
}
