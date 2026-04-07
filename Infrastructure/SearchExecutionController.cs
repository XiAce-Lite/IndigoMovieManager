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
        private readonly Action restartThumbnailTask;
        private readonly Func<string, bool, Task> filterAndSortAsync;
        private readonly Action selectFirstItem;

        public SearchExecutionController(
            Func<string> getDbFullPath,
            Func<string> getSortId,
            Action<string> setSearchKeyword,
            Action<string> syncSearchBoxText,
            Action restartThumbnailTask,
            Func<string, bool, Task> filterAndSortAsync,
            Action selectFirstItem
        )
        {
            this.getDbFullPath = getDbFullPath;
            this.getSortId = getSortId;
            this.setSearchKeyword = setSearchKeyword;
            this.syncSearchBoxText = syncSearchBoxText;
            this.restartThumbnailTask = restartThumbnailTask;
            this.filterAndSortAsync = filterAndSortAsync;
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

            setSearchKeyword(normalizedText);
            restartThumbnailTask();
            await filterAndSortAsync(getSortId(), true);
            selectFirstItem();
            return true;
        }
    }
}
