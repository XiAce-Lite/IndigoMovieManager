using System.Collections.Concurrent;

namespace IndigoMovieManager.Skin.Runtime
{
    internal enum WhiteBrowserSkinProfileValueCacheState
    {
        Pending,
        Persisted,
        Faulted,
    }

    /// <summary>
    /// profile 値のセッション内 cache。
    /// API からは pending も見せるが、初期タブ復元は persisted だけを見る。
    /// </summary>
    internal static class WhiteBrowserSkinProfileValueCache
    {
        private static readonly ConcurrentDictionary<string, CacheEntry> Entries =
            new(StringComparer.Ordinal);

        internal static void ClearForTesting()
        {
            Entries.Clear();
        }

        internal static void RecordPending(string dbFullPath, string skinName, string key, string value)
        {
            string identityKey = BuildIdentityKey(dbFullPath, skinName, key);
            if (string.IsNullOrWhiteSpace(identityKey))
            {
                return;
            }

            Entries[identityKey] = new CacheEntry(
                WhiteBrowserSkinProfileValueCacheState.Pending,
                value ?? ""
            );
        }

        internal static void RecordPersisted(string dbFullPath, string skinName, string key, string value)
        {
            string identityKey = BuildIdentityKey(dbFullPath, skinName, key);
            if (string.IsNullOrWhiteSpace(identityKey))
            {
                return;
            }

            Entries[identityKey] = new CacheEntry(
                WhiteBrowserSkinProfileValueCacheState.Persisted,
                value ?? ""
            );
        }

        internal static void RecordFault(string dbFullPath, string skinName, string key)
        {
            string identityKey = BuildIdentityKey(dbFullPath, skinName, key);
            if (string.IsNullOrWhiteSpace(identityKey))
            {
                return;
            }

            Entries[identityKey] = new CacheEntry(WhiteBrowserSkinProfileValueCacheState.Faulted, "");
        }

        internal static bool TryGetApiVisibleValue(
            string dbFullPath,
            string skinName,
            string key,
            out string value
        )
        {
            value = "";
            string identityKey = BuildIdentityKey(dbFullPath, skinName, key);
            if (
                string.IsNullOrWhiteSpace(identityKey)
                || !Entries.TryGetValue(identityKey, out CacheEntry entry)
            )
            {
                return false;
            }

            if (entry.State == WhiteBrowserSkinProfileValueCacheState.Faulted)
            {
                return false;
            }

            value = entry.Value ?? "";
            return true;
        }

        internal static bool TryGetPersistedValue(
            string dbFullPath,
            string skinName,
            string key,
            out string value
        )
        {
            value = "";
            string identityKey = BuildIdentityKey(dbFullPath, skinName, key);
            if (
                string.IsNullOrWhiteSpace(identityKey)
                || !Entries.TryGetValue(identityKey, out CacheEntry entry)
                || entry.State != WhiteBrowserSkinProfileValueCacheState.Persisted
            )
            {
                return false;
            }

            value = entry.Value ?? "";
            return true;
        }

        private static string BuildIdentityKey(string dbFullPath, string skinName, string key)
        {
            string dbIdentity = WhiteBrowserSkinDbIdentity.Build(dbFullPath);
            string normalizedSkinName = skinName?.Trim() ?? "";
            string normalizedKey = key?.Trim() ?? "";
            if (
                string.IsNullOrWhiteSpace(dbIdentity)
                || string.IsNullOrWhiteSpace(normalizedSkinName)
                || string.IsNullOrWhiteSpace(normalizedKey)
            )
            {
                return "";
            }

            return $"{dbIdentity}:{normalizedSkinName}:{normalizedKey}";
        }

        private sealed class CacheEntry
        {
            internal CacheEntry(WhiteBrowserSkinProfileValueCacheState state, string value)
            {
                State = state;
                Value = value ?? "";
            }

            internal WhiteBrowserSkinProfileValueCacheState State { get; }
            internal string Value { get; }
        }
    }
}
