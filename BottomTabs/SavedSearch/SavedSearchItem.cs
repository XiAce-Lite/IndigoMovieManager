namespace IndigoMovieManager.BottomTabs.SavedSearch
{
    /// <summary>
    /// tagbar テーブル 1 行を、保存済み検索条件タブで扱いやすい形へ寄せる。
    /// </summary>
    public sealed class SavedSearchItem
    {
        public long ItemId { get; init; }

        public long ParentId { get; init; }

        public long OrderId { get; init; }

        public long GroupId { get; init; }

        public string Title { get; init; } = "";

        public string Contents { get; init; } = "";

        public string DisplayTitle =>
            string.IsNullOrWhiteSpace(Title)
                ? (string.IsNullOrWhiteSpace(Contents) ? "(名称未設定)" : Contents)
                : Title;

        public bool CanExecute => !string.IsNullOrWhiteSpace(Contents);
    }
}
