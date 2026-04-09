using IndigoMovieManager.Infrastructure;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class SearchExecutionControllerTests
{
    [Test]
    public async Task ExecuteAsync_DB未選択なら何もしない()
    {
        int filterCallCount = 0;
        SearchExecutionController controller = new(
            getDbFullPath: () => "",
            getSortId: () => "1",
            setSearchKeyword: _ => Assert.Fail("DB未選択では検索語を更新しないはずです。"),
            syncSearchBoxText: _ => Assert.Fail("DB未選択ではSearchBox同期しないはずです。"),
            restartThumbnailTask: () => Assert.Fail("DB未選択では再起動しないはずです。"),
            filterAndSortAsync: (_, _) =>
            {
                filterCallCount++;
                return Task.CompletedTask;
            },
            selectFirstItem: () => Assert.Fail("DB未選択では選択変更しないはずです。")
        );

        bool actual = await controller.ExecuteAsync("target", true);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.False);
            Assert.That(filterCallCount, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task ExecuteAsync_検索語更新とFilterAndSortを順に実行する()
    {
        string searchKeyword = "";
        string searchBoxText = "";
        string sortId = "";
        bool isGetNew = false;
        int restartCallCount = 0;
        int selectCallCount = 0;

        SearchExecutionController controller = new(
            getDbFullPath: () => @"C:\temp\sample.wb",
            getSortId: () => "7",
            setSearchKeyword: keyword => searchKeyword = keyword,
            syncSearchBoxText: text => searchBoxText = text,
            restartThumbnailTask: () => restartCallCount++,
            filterAndSortAsync: (resolvedSortId, resolvedIsGetNew) =>
            {
                sortId = resolvedSortId;
                isGetNew = resolvedIsGetNew;
                return Task.CompletedTask;
            },
            selectFirstItem: () => selectCallCount++
        );

        bool actual = await controller.ExecuteAsync("tokyo", true);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.True);
            Assert.That(searchKeyword, Is.EqualTo("tokyo"));
            Assert.That(searchBoxText, Is.EqualTo("tokyo"));
            Assert.That(sortId, Is.EqualTo("7"));
            Assert.That(isGetNew, Is.True);
            Assert.That(restartCallCount, Is.EqualTo(1));
            Assert.That(selectCallCount, Is.EqualTo(1));
        });
    }
}
