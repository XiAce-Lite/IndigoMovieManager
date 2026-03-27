using System.Reflection;

namespace IndigoMovieManager.Thumbnail
{
    // 実行時の保存先名や設定解決先はここへ集約し、名前切替時の修正点を最小化する。
    public static class AppIdentityRuntime
    {
        public const string MainAppIdentityName = "IndigoMovieManager";
        public const string ForkAppIdentityName = "IndigoMovieManager_fork";
        private const string IdentityMetadataKey = "ImmAppIdentityName";
        private const string SettingsTypeName = "IndigoMovieManager.Properties.Settings";

        public static string ResolveAppIdentityName()
        {
            string configuredIdentityName = ResolveMetadataValue(
                Assembly.GetEntryAssembly(),
                IdentityMetadataKey
            );
            if (IsSupportedIdentityName(configuredIdentityName))
            {
                return configuredIdentityName;
            }

            configuredIdentityName = ResolveMetadataValue(
                typeof(AppIdentityRuntime).Assembly,
                IdentityMetadataKey
            );
            if (IsSupportedIdentityName(configuredIdentityName))
            {
                return configuredIdentityName;
            }

            Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < loadedAssemblies.Length; i++)
            {
                configuredIdentityName = ResolveMetadataValue(
                    loadedAssemblies[i],
                    IdentityMetadataKey
                );
                if (IsSupportedIdentityName(configuredIdentityName))
                {
                    return configuredIdentityName;
                }
            }

            return MainAppIdentityName;
        }

        public static string ResolveStorageRootName()
        {
            return ResolveAppIdentityName();
        }

        public static string BuildLocalMutexName(string suffix)
        {
            string normalizedSuffix = suffix?.Trim() ?? "";
            return string.IsNullOrWhiteSpace(normalizedSuffix)
                ? $@"Local\{ResolveAppIdentityName()}"
                : $@"Local\{ResolveAppIdentityName()}_{normalizedSuffix}";
        }

        public static Type ResolveSettingsType()
        {
            Type resolved = Type.GetType(
                $"{SettingsTypeName}, {ResolveAppIdentityName()}",
                false
            );
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

        private static bool IsSupportedIdentityName(string value)
        {
            return string.Equals(value, MainAppIdentityName, StringComparison.Ordinal)
                || string.Equals(value, ForkAppIdentityName, StringComparison.Ordinal);
        }

        private static string ResolveMetadataValue(Assembly assembly, string key)
        {
            if (assembly == null || string.IsNullOrWhiteSpace(key))
            {
                return "";
            }

            try
            {
                AssemblyMetadataAttribute[] metadataAttributes = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                    .ToArray();
                for (int i = 0; i < metadataAttributes.Length; i++)
                {
                    AssemblyMetadataAttribute attribute = metadataAttributes[i];
                    if (string.Equals(attribute.Key, key, StringComparison.Ordinal))
                    {
                        return attribute.Value ?? "";
                    }
                }
            }
            catch
            {
                // メタデータ取得失敗時は既定名へ戻す。
            }

            return "";
        }
    }
}
