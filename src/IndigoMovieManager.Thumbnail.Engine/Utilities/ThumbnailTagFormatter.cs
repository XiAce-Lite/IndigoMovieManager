namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// タグ表示用の整形だけを切り出す。
    /// </summary>
    public static class ThumbnailTagFormatter
    {
        public static string ConvertTagsWithNewLine(IEnumerable<string> tags)
        {
            // タグ編集画面で扱いやすいように、重複排除して改行連結する。
            string tagWithNewLine = "";
            IEnumerable<string> distinctTags = tags?.Distinct() ?? [];

            foreach (string tagItem in distinctTags)
            {
                if (string.IsNullOrEmpty(tagWithNewLine))
                {
                    tagWithNewLine = tagItem;
                }
                else
                {
                    tagWithNewLine += Environment.NewLine + tagItem;
                }
            }

            return tagWithNewLine;
        }
    }
}
