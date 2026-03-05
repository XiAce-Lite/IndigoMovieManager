using System.Windows;
using System.Windows.Media;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// ミニパネル向けメモリプレビューを保持する軽量キャッシュ。
    /// </summary>
    internal sealed class ThumbnailPreviewCache
    {
        private const int MaxEntries = 256;
        private readonly object gate = new();
        private readonly Dictionary<string, CacheEntry> entries = new(StringComparer.OrdinalIgnoreCase);
        private readonly LinkedList<string> lru = new();

        public static ThumbnailPreviewCache Shared { get; } = new();

        // 同一キー更新時はリビジョンを増やし、UI再評価トリガーとして使う。
        public long Store(string cacheKey, ImageSource imageSource)
        {
            if (string.IsNullOrWhiteSpace(cacheKey) || imageSource == null)
            {
                return 0;
            }

            FreezeIfPossible(imageSource);

            lock (gate)
            {
                if (entries.TryGetValue(cacheKey, out CacheEntry existing))
                {
                    existing.Image = imageSource;
                    existing.Revision++;
                    Touch(existing);
                    return existing.Revision;
                }

                LinkedListNode<string> node = lru.AddFirst(cacheKey);
                entries[cacheKey] = new CacheEntry
                {
                    Image = imageSource,
                    Node = node,
                    Revision = 1,
                };
                TrimIfNeeded();
                return 1;
            }
        }

        public bool TryGet(string cacheKey, out ImageSource imageSource)
        {
            lock (gate)
            {
                if (entries.TryGetValue(cacheKey ?? "", out CacheEntry entry))
                {
                    Touch(entry);
                    imageSource = entry.Image;
                    return imageSource != null;
                }
            }

            imageSource = null;
            return false;
        }

        public void Clear()
        {
            lock (gate)
            {
                entries.Clear();
                lru.Clear();
            }
        }

        private static void FreezeIfPossible(ImageSource imageSource)
        {
            if (imageSource is not Freezable freezable)
            {
                return;
            }

            if (freezable.IsFrozen || !freezable.CanFreeze)
            {
                return;
            }

            freezable.Freeze();
        }

        private void TrimIfNeeded()
        {
            while (entries.Count > MaxEntries)
            {
                LinkedListNode<string> staleNode = lru.Last;
                if (staleNode == null)
                {
                    return;
                }

                string staleKey = staleNode.Value;
                lru.RemoveLast();
                _ = entries.Remove(staleKey);
            }
        }

        private void Touch(CacheEntry entry)
        {
            if (entry.Node == null)
            {
                return;
            }

            lru.Remove(entry.Node);
            lru.AddFirst(entry.Node);
        }

        private sealed class CacheEntry
        {
            public ImageSource Image { get; set; }
            public LinkedListNode<string> Node { get; set; }
            public long Revision { get; set; }
        }
    }
}
