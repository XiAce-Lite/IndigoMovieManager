using System.Collections.Generic;
using IndigoMovieManager.Thumbnail.QueueDb;

namespace IndigoMovieManager.UpperTabs.Common
{
    /// <summary>
    /// 上側タブの画像更新を、選択中タブだけへ絞るための最小ヘルパ。
    /// </summary>
    public static class UpperTabActivationGate
    {
        private static readonly object PreferredMoviePathKeysGate = new();
        private static readonly HashSet<string> PreferredMoviePathKeys =
            new(StringComparer.OrdinalIgnoreCase);

        public static bool ShouldApplyImageUpdate(object isSelectedValue)
        {
            return ShouldApplyImageUpdate(isSelectedValue, null);
        }

        public static bool ShouldApplyImageUpdate(object isSelectedValue, object moviePathValue)
        {
            // TabItem の祖先解決が遅い瞬間は UnsetValue になることがある。
            // 表示不能を避けるため、明確に false の時だけ止める。
            if (isSelectedValue is bool isSelected && !isSelected)
            {
                return false;
            }

            // 可視近傍キーが入っている時だけ、off-screen の画像再評価を止める。
            if (moviePathValue is not string moviePath || string.IsNullOrWhiteSpace(moviePath))
            {
                return true;
            }

            string moviePathKey = QueueDbPathResolver.CreateMoviePathKey(moviePath);
            if (string.IsNullOrWhiteSpace(moviePathKey))
            {
                return true;
            }

            lock (PreferredMoviePathKeysGate)
            {
                return PreferredMoviePathKeys.Count < 1
                    || PreferredMoviePathKeys.Contains(moviePathKey);
            }
        }

        public static void UpdatePreferredMoviePathKeys(IEnumerable<string> moviePathKeys)
        {
            lock (PreferredMoviePathKeysGate)
            {
                PreferredMoviePathKeys.Clear();
                if (moviePathKeys == null)
                {
                    return;
                }

                foreach (string moviePathKey in moviePathKeys)
                {
                    if (string.IsNullOrWhiteSpace(moviePathKey))
                    {
                        continue;
                    }

                    _ = PreferredMoviePathKeys.Add(moviePathKey);
                }
            }
        }

        public static void ClearPreferredMoviePathKeys()
        {
            lock (PreferredMoviePathKeysGate)
            {
                PreferredMoviePathKeys.Clear();
            }
        }
    }
}
