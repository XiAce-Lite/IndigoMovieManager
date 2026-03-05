using System.Reflection;
using System.Threading;

namespace IndigoMovieManager.Thumbnail
{
    // 動画サイズからレーン種別を決める共通分類器。
    internal static class ThumbnailLaneClassifier
    {
        private const string SettingsTypeName = "IndigoMovieManager.Properties.Settings";
        private const string SettingsAssemblyName = "IndigoMovieManager_fork";
        private const string PriorityLaneSettingName = "ThumbnailPriorityLaneMaxMb";
        private const string SlowLaneSettingName = "ThumbnailSlowLaneMinGb";
        private const int DefaultPriorityLaneMaxMb = 300;
        private const int DefaultSlowLaneMinGb = 3;
        private const int MinPriorityLaneMaxMb = 50;
        private const int MaxPriorityLaneMaxMb = 4096;
        private const int MinSlowLaneMinGb = 1;
        private const int MaxSlowLaneMinGb = 1024;
        private const long OneMbBytes = 1024L * 1024L;
        private const long OneGbBytes = 1024L * 1024L * 1024L;
        private static readonly object settingsLock = new();
        private static long lastSettingsReadUtcTicks;
        private static int cachedPriorityLaneMaxMb = DefaultPriorityLaneMaxMb;
        private static int cachedSlowLaneMinGb = DefaultSlowLaneMinGb;

        internal static ThumbnailExecutionLane ResolveLane(long movieSizeBytes)
        {
            long sizeBytes = movieSizeBytes < 0 ? 0 : movieSizeBytes;
            (long priorityLaneMaxBytes, long slowLaneMinBytes) = ResolveThresholdBytes();
            if (sizeBytes > 0 && sizeBytes <= priorityLaneMaxBytes)
            {
                return ThumbnailExecutionLane.Priority;
            }

            if (sizeBytes >= slowLaneMinBytes)
            {
                return ThumbnailExecutionLane.Slow;
            }

            return ThumbnailExecutionLane.Normal;
        }

        internal static int ResolveRank(ThumbnailExecutionLane lane)
        {
            return lane switch
            {
                ThumbnailExecutionLane.Priority => 0,
                ThumbnailExecutionLane.Normal => 1,
                ThumbnailExecutionLane.Slow => 2,
                _ => 1,
            };
        }

        // 設定値は短い間隔でキャッシュし、ジョブごとの反射コストを抑える。
        private static (long priorityLaneMaxBytes, long slowLaneMinBytes) ResolveThresholdBytes()
        {
            RefreshCachedSettingsIfNeeded();
            int priorityLaneMaxMb = cachedPriorityLaneMaxMb;
            int slowLaneMinGb = cachedSlowLaneMinGb;
            long priorityLaneMaxBytes = priorityLaneMaxMb * OneMbBytes;
            long slowLaneMinBytesFromSetting = slowLaneMinGb * OneGbBytes;
            long slowLaneMinBytes = Math.Max(
                slowLaneMinBytesFromSetting,
                priorityLaneMaxBytes + OneMbBytes
            );
            return (priorityLaneMaxBytes, slowLaneMinBytes);
        }

        private static void RefreshCachedSettingsIfNeeded()
        {
            long nowTicks = DateTime.UtcNow.Ticks;
            long lastTicks = Interlocked.Read(ref lastSettingsReadUtcTicks);
            if (nowTicks - lastTicks < TimeSpan.FromSeconds(1).Ticks)
            {
                return;
            }

            lock (settingsLock)
            {
                nowTicks = DateTime.UtcNow.Ticks;
                if (nowTicks - lastSettingsReadUtcTicks < TimeSpan.FromSeconds(1).Ticks)
                {
                    return;
                }

                cachedPriorityLaneMaxMb = ReadUserSettingInt(
                    PriorityLaneSettingName,
                    DefaultPriorityLaneMaxMb,
                    MinPriorityLaneMaxMb,
                    MaxPriorityLaneMaxMb
                );
                cachedSlowLaneMinGb = ReadUserSettingInt(
                    SlowLaneSettingName,
                    DefaultSlowLaneMinGb,
                    MinSlowLaneMinGb,
                    MaxSlowLaneMinGb
                );
                Interlocked.Exchange(ref lastSettingsReadUtcTicks, nowTicks);
            }
        }

        private static int ReadUserSettingInt(
            string settingName,
            int defaultValue,
            int minValue,
            int maxValue
        )
        {
            if (!TryReadUserSettingInt(settingName, out int configuredValue))
            {
                return defaultValue;
            }

            if (configuredValue < minValue || configuredValue > maxValue)
            {
                return defaultValue;
            }

            return configuredValue;
        }

        private static bool TryReadUserSettingInt(string settingName, out int value)
        {
            value = 0;
            object settings = GetSettingsDefaultInstance();
            if (settings == null)
            {
                return false;
            }

            try
            {
                PropertyInfo settingProperty = settings
                    .GetType()
                    .GetProperty(settingName, BindingFlags.Instance | BindingFlags.Public);
                if (settingProperty == null)
                {
                    return false;
                }

                object raw = settingProperty.GetValue(settings);
                if (raw is int intValue)
                {
                    value = intValue;
                    return true;
                }

                if (raw != null && int.TryParse(raw.ToString(), out int parsed))
                {
                    value = parsed;
                    return true;
                }
            }
            catch
            {
                // 設定取得失敗時は既定値へフォールバックする。
            }

            return false;
        }

        private static object GetSettingsDefaultInstance()
        {
            try
            {
                Type settingsType = ResolveSettingsType();
                if (settingsType == null)
                {
                    return null;
                }

                PropertyInfo defaultProperty = settingsType.GetProperty(
                    "Default",
                    BindingFlags.Static | BindingFlags.Public
                );
                return defaultProperty?.GetValue(null);
            }
            catch
            {
                return null;
            }
        }

        private static Type ResolveSettingsType()
        {
            Type resolved = Type.GetType($"{SettingsTypeName}, {SettingsAssemblyName}", false);
            if (resolved != null)
            {
                return resolved;
            }

            Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < loadedAssemblies.Length; i++)
            {
                Type found = loadedAssemblies[i].GetType(SettingsTypeName, false);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }

    internal enum ThumbnailExecutionLane
    {
        Priority = 0,
        Normal = 1,
        Slow = 2,
    }
}
