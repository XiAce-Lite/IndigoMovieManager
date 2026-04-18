namespace IndigoMovieManager.Infrastructure
{
    /// <summary>
    /// 検索で使う派生列の作り方を 1 か所へ寄せる。
    /// SearchService と MovieRecords のキャッシュが同じ規則を使うための正本。
    /// </summary>
    internal static class SearchRecordProjection
    {
        internal static string[] BuildSearchFields(MovieRecords item)
        {
            string kana = ResolveSearchKana(item);
            string katakanaKana = JapaneseKanaProvider.ConvertToKatakana(kana);
            string roma = ResolveSearchRoma(item, kana);

            return
            [
                item?.Movie_Name ?? "",
                item?.Movie_Path ?? "",
                item?.Tags ?? "",
                item?.Comment1 ?? "",
                item?.Comment2 ?? "",
                item?.Comment3 ?? "",
                kana,
                katakanaKana,
                roma,
            ];
        }

        internal static string[] BuildNormalizedTags(MovieRecords item)
        {
            return TagTextParser.SplitDistinct(item?.Tags, StringComparer.CurrentCultureIgnoreCase);
        }

        private static string ResolveSearchKana(MovieRecords item)
        {
            if (!string.IsNullOrWhiteSpace(item?.Kana))
            {
                return JapaneseKanaProvider.NormalizeToHiragana(item.Kana);
            }

            if (item == null)
            {
                return "";
            }

            return JapaneseKanaProvider.GetKana(item.Movie_Name, item.Movie_Path);
        }

        private static string ResolveSearchRoma(MovieRecords item, string kana)
        {
            if (!string.IsNullOrWhiteSpace(item?.Roma))
            {
                return item.Roma;
            }

            if (!string.IsNullOrWhiteSpace(kana))
            {
                return JapaneseKanaProvider.GetRomaFromKana(kana);
            }

            if (item == null)
            {
                return "";
            }

            return JapaneseKanaProvider.GetRoma(item.Movie_Name, item.Movie_Path);
        }
    }
}
