using System;
using System.Threading.Tasks;

namespace IndigoMovieManager.Infrastructure
{
    /// <summary>
    /// 検索 UI が増えても、検索実行そのものは本体の 1 つの流れへ揃える。
    /// MainWindow は必要な副作用だけ注入し、ここは実行順序の正本を担う。
    /// </summary>
    public sealed class SearchExecutionController
    {
        private readonly Func<string> getDbFullPath;
        private readonly Func<string> getSortId;
        private readonly Action<string> setSearchKeyword;
        private readonly Action<string> syncSearchBoxText;
        private readonly Action<string> beginUserPriorityWork;
        private readonly Action<string> endUserPriorityWork;
        private readonly Action restartThumbnailTask;
        private readonly Func<string, Task> refreshSearchResultsAsync;
        private readonly Action selectFirstItem;

        public SearchExecutionController(
            Func<string> getDbFullPath,
            Func<string> getSortId,
            Action<string> setSearchKeyword,
            Action<string> syncSearchBoxText,
            Action<string> beginUserPriorityWork,
            Action<string> endUserPriorityWork,
            Action restartThumbnailTask,
            Func<string, Task> refreshSearchResultsAsync,
            Action selectFirstItem
        )
        {
            this.getDbFullPath = getDbFullPath;
            this.getSortId = getSortId;
            this.setSearchKeyword = setSearchKeyword;
            this.syncSearchBoxText = syncSearchBoxText;
            this.beginUserPriorityWork = beginUserPriorityWork;
            this.endUserPriorityWork = endUserPriorityWork;
            this.restartThumbnailTask = restartThumbnailTask;
            this.refreshSearchResultsAsync = refreshSearchResultsAsync;
            this.selectFirstItem = selectFirstItem;
        }

        public async Task<bool> ExecuteAsync(string text, bool syncSearchText)
        {
            if (string.IsNullOrEmpty(getDbFullPath()))
            {
                return false;
            }

            string normalizedText = text ?? "";
            if (syncSearchText)
            {
                syncSearchBoxText(normalizedText);
            }

            beginUserPriorityWork("search");
            try
            {
                setSearchKeyword(normalizedText);
                restartThumbnailTask();
                // 検索実行後の再描画は注入側に委ね、通常時は query-only、
                // 起動直後の部分ロード中だけ full reload へ切り替えられるようにする。
                await refreshSearchResultsAsync(getSortId());
                selectFirstItem();
                return true;
            }
            finally
            {
                endUserPriorityWork("search");
            }
        }
    }
}
