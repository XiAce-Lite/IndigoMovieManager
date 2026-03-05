namespace IndigoMovieManager.Watcher
{
    /// <summary>
    /// 設定値からIFileIndexProviderを決定し、Facadeを生成する。
    /// </summary>
    internal static class FileIndexProviderFactory
    {
        public const string ProviderEverything = "everything";
        public const string ProviderEverythingLite = "everythinglite";

        public static IIndexProviderFacade CreateFacade()
        {
            string providerKey = ResolveProviderKey();
            IFileIndexProvider provider = CreateProvider(providerKey);
            return new IndexProviderFacade(provider);
        }

        // 文字列揺れを吸収し、未知値は everything に丸める。
        private static string ResolveProviderKey()
        {
            string raw = (Properties.Settings.Default.FileIndexProvider ?? "").Trim();
            return NormalizeProviderKey(raw);
        }

        // UI保存時と生成時で同じ正規化ルールを共有する。
        internal static string NormalizeProviderKey(string raw)
        {
            if (string.Equals(raw?.Trim(), ProviderEverythingLite, StringComparison.OrdinalIgnoreCase))
            {
                return ProviderEverythingLite;
            }

            return ProviderEverything;
        }

        private static IFileIndexProvider CreateProvider(string providerKey)
        {
            return providerKey switch
            {
                ProviderEverythingLite => new EverythingLiteProvider(),
                _ => new EverythingProvider(),
            };
        }
    }
}
